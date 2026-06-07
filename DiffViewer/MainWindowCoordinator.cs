using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.Utility;
using DiffViewer.ViewModels;

namespace DiffViewer;

/// <summary>
/// Owns the <see cref="MainViewModel"/> lifecycle for a single window
/// session: cold-launch (parse args + build context), in-place switch
/// (build new context + atomic swap + dispose outgoing), and shutdown
/// (dispose current).
///
/// <para>The coordinator is the only place that decides "is the app
/// currently switching?", "do we shut down on launch failure?", and
/// "in what order do swap / dispose happen?". Wiring everything through
/// here makes the answer testable instead of scattered across <c>App.xaml.cs</c>
/// and the view-model.</para>
///
/// <para><b>Thread expectation</b>: public methods are intended to be
/// called from the UI thread. Internal awaits use
/// <c>ConfigureAwait(true)</c> so resumption stays on the calling
/// SynchronizationContext (the WPF dispatcher in production).</para>
/// </summary>
public sealed class MainWindowCoordinator : ObservableObject, IContextSwitcher
{
    /// <summary>
    /// Test seam for the per-context build step. Defaults to
    /// <see cref="CompositionRoot.BuildContextAsync"/> in production.
    /// </summary>
    public delegate Task<MainViewModel> ContextFactory(
        ParsedCommandLine parsed,
        AppServices services,
        ContextScope scope,
        CancellationToken ct,
        IReviewRef? review);

    private readonly AppServices _services;
    private readonly IDialogService _dialog;
    private readonly CancellationToken _appShutdownToken;
    private readonly ContextFactory _contextFactory;
    private readonly Action<string>? _stderrWriter;
    private readonly SemaphoreSlim _switchGate = new(1, 1);

    private IShellViewModel? _current;
    private ContextScope? _currentScope;
    private bool _isSwitching;
    private string _switchingStatus = string.Empty;

    // Tracks the live PR-watcher subscription so it can be detached
    // when the current VM is swapped or disposed. Cleared whenever
    // _current changes; re-created post-swap when the new VM has a
    // PullRequestWatcher.
    private MainViewModel? _currentPrSubscriber;
    private EventHandler<PullRequestChangedEventArgs>? _currentPrHandler;
    private PullRequestRef? _currentPrRef;
    private string? _currentPrRepoPath;

    public MainWindowCoordinator(
        AppServices services,
        IDialogService dialog,
        CancellationToken appShutdownToken = default,
        ContextFactory? contextFactory = null,
        Action<int>? shutdownAction = null,
        Action<string>? stderrWriter = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        _appShutdownToken = appShutdownToken;
        _contextFactory = contextFactory ?? ((p, s, sc, ct, review) => CompositionRoot.BuildContextAsync(p, s, sc, ct, review));
        _ = shutdownAction; // Kept for backward compat; no longer used.
        _stderrWriter = stderrWriter;
    }

    /// <summary>
    /// Currently-active shell view-model. <c>null</c> after
    /// <see cref="DisposeCurrentAsync"/>. Concrete type is either
    /// <see cref="MainViewModel"/> (loaded context) or
    /// <see cref="EmptyContextViewModel"/> (cold-launch fallback when
    /// args fail but at least one recent is persisted).
    /// </summary>
    public IShellViewModel? Current => _current;

    /// <summary>Per-context scope owning the currently-active view-model. Exposed for tests.</summary>
    public ContextScope? CurrentScope => _currentScope;

    /// <summary>
    /// True while <see cref="SwitchContextAsync"/> is in flight. Bound to the
    /// dropdown's <c>IsEnabled</c> in Phase 7 so the user can't kick off a
    /// second switch on top of an in-flight one.
    /// </summary>
    public bool IsSwitching
    {
        get => _isSwitching;
        private set => SetProperty(ref _isSwitching, value);
    }

    /// <summary>
    /// Human-readable description of the in-flight switch (e.g.
    /// <c>"Loading PR #296 from owner/repo\u2026"</c>,
    /// <c>"Fetching head and merge base\u2026"</c>). Bound to the window-level
    /// loading overlay so the user has visible feedback during the
    /// otherwise-silent gap between the New-diff / recents click and the
    /// new context appearing. Empty when no switch is in flight.
    /// </summary>
    public string SwitchingStatus
    {
        get => _switchingStatus;
        private set => SetProperty(ref _switchingStatus, value ?? string.Empty);
    }

    /// <summary>Raised after <see cref="Current"/> changes (build, swap, dispose).</summary>
    public event EventHandler? CurrentChanged;

    /// <summary>
    /// Cold-launch entry. Parses args, dispatches to the appropriate
    /// launch path (local repo vs PR URL), and sets <see cref="Current"/>.
    /// Returns <c>true</c> on success (caller can show the window) —
    /// including when the cold-launch falls back to the empty-state
    /// dropdown picker. Returns <c>false</c> only when the coordinator
    /// has shown the error dialog AND requested shutdown (no recents
    /// present).
    /// </summary>
    public async Task<bool> InitialLaunchAsync(
        IReadOnlyList<string> args,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        var plan = CompositionRoot.BuildArgs(args);
        if (plan.IsError)
        {
            return HandleColdLaunchFailure(plan.Error?.Message ?? "Failed to parse command line.");
        }
        if (plan.IsPullRequest)
        {
            return await InitialLaunchFromPullRequestAsync(plan.PullRequest!, ct).ConfigureAwait(true);
        }
        return await StartFromParsedAsync(plan.Local!, ct).ConfigureAwait(true);
    }

    /// <summary>
    /// Cold-launch from a parsed GitHub PR URL. Resolves the PR through
    /// <see cref="AppServices.PullRequestResolver"/>; if the resolver
    /// reports a missing clone, shows the missing-clone dialog and
    /// retries once with the resolved path. On a terminal failure or a
    /// user-cancelled missing-clone dialog, routes through
    /// <see cref="HandleColdLaunchFailure"/> so the user lands on the
    /// empty-state shell (when recents exist) rather than an exit.
    /// </summary>
    /// <remarks>
    /// <para>The resolver's sub-services (libgit2, HTTP, gh-token
    /// process spawn) are documented as free-threaded; the fetcher
    /// already wraps libgit2 work in a <c>Task.Run</c> internally. We
    /// do not add another <c>Task.Run</c> here — that would only add
    /// thread-pool hops, not safety.</para>
    ///
    /// <para>The retry loop is bounded: at most one re-resolve after
    /// the dialog resolves, to keep "user keeps mis-typing the path"
    /// from looping forever. A second <see cref="PullRequestResolution.MissingClone"/>
    /// on retry surfaces a clear "DiffViewer still can't find the clone"
    /// message via <see cref="HandleColdLaunchFailure"/>.</para>
    /// </remarks>
    public async Task<bool> InitialLaunchFromPullRequestAsync(
        PullRequestRef pr,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pr);

        var resolution = await ResolveWithMissingClonePromptAsync(pr, ct).ConfigureAwait(true);

        return resolution switch
        {
            PullRequestResolution.Ready ready =>
                await StartFromPullRequestAsync(ready.Parsed, pr, ct).ConfigureAwait(true),
            PullRequestResolution.MissingClone =>
                HandleColdLaunchFailure(
                    $"DiffViewer still can't find a local clone of {pr.Owner}/{pr.Repo}. "
                    + "Add a repo root in Settings or browse to the clone via the missing-clone dialog."),
            PullRequestResolution.Failed failed =>
                HandleColdLaunchFailure(failed.Message),
            _ => HandleColdLaunchFailure($"Unexpected PR resolution state for {pr.Owner}/{pr.Repo}#{pr.Number}."),
        };
    }

    /// <summary>
    /// Run the missing-clone prompt cycle for a fresh GitHub PR
    /// reference. If the initial resolution is anything other than
    /// <see cref="PullRequestResolution.MissingClone"/>, returns it
    /// unchanged. On MissingClone, shows the missing-clone dialog and
    /// re-resolves once. The caller still receives the post-prompt
    /// resolution (which may be Ready, MissingClone again, or Failed
    /// depending on what the user did in the dialog) and is
    /// responsible for surfacing the appropriate final-state error.
    ///
    /// <para>Used by both the cold-launch path
    /// (<see cref="InitialLaunchFromPullRequestAsync"/>) and the
    /// runtime "New diff" dialog path
    /// (<see cref="SwitchToAsync"/>'s <see cref="DiffLaunchSource.GitHubPullRequest"/>
    /// case). The recents-dropdown path
    /// (<see cref="SwitchToRecentAsync"/>) intentionally does <em>not</em>
    /// call this — a recents row pointed at a clone we used to have,
    /// so missing-clone is surfaced as a hard error there, not a
    /// dialog.</para>
    /// </summary>
    private async Task<PullRequestResolution> ResolveWithMissingClonePromptAsync(
        PullRequestRef pr, CancellationToken ct)
    {
        var progress = new Progress<string>(s => SwitchingStatus = s);
        var resolution = await _services.PullRequestResolver.ResolveAsync(pr, progress, ct).ConfigureAwait(true);
        if (resolution is PullRequestResolution.MissingClone)
        {
            var dialogResult = await _services.MissingClonePromptHost.ShowAsync(pr, ct).ConfigureAwait(true);
            switch (dialogResult)
            {
                case MissingClonePromptResult.Resolved:
                    // Settings now contains the mapping. Re-invoke the
                    // resolver; from this point on we accept whatever
                    // state it returns without prompting again.
                    resolution = await _services.PullRequestResolver.ResolveAsync(pr, progress, ct).ConfigureAwait(true);
                    break;
                case MissingClonePromptResult.Cancelled:
                    return new PullRequestResolution.Failed(
                        pr,
                        $"Cancelled — no local clone available for {pr.Owner}/{pr.Repo}.");
                case MissingClonePromptResult.Failed failed:
                    return new PullRequestResolution.Failed(pr, failed.Message);
            }
        }
        return resolution;
    }

    private async Task<bool> StartFromPullRequestAsync(
        ParsedCommandLine parsed,
        PullRequestRef pr,
        CancellationToken ct)
    {
        var newScope = new ContextScope(_appShutdownToken);
        MainViewModel newVm;
        try
        {
            newVm = await _contextFactory(parsed, _services, newScope, ct, pr).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await newScope.DisposeAsync().ConfigureAwait(true);
            return HandleColdLaunchFailure(
                ex is ContextBuildException ? ex.Message : $"Failed to start: {ex.Message}");
        }

        _current = newVm;
        _currentScope = newScope;
        OnCurrentChanged();
        AttachPullRequestSubscription(newVm, pr, parsed.RepoPath);
        await TryRecordAsync(parsed, review: pr, ct: ct).ConfigureAwait(true);
        return true;
    }

    /// <summary>
    /// Cold-launch from an already-parsed command line. Used directly by
    /// tests; <see cref="InitialLaunchAsync"/> wraps it.
    /// </summary>
    public async Task<bool> StartFromParsedAsync(
        ParsedCommandLine parsed,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        var newScope = new ContextScope(_appShutdownToken);
        MainViewModel newVm;
        try
        {
            newVm = await _contextFactory(parsed, _services, newScope, ct, null).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await newScope.DisposeAsync().ConfigureAwait(true);
            return HandleColdLaunchFailure(
                ex is ContextBuildException ? ex.Message : $"Failed to start: {ex.Message}");
        }

        _current = newVm;
        _currentScope = newScope;
        OnCurrentChanged();
        await TryRecordAsync(parsed, ct: ct).ConfigureAwait(true);
        return true;
    }

    /// <summary>
    /// Runtime in-place switch. Builds a fresh per-context graph and swaps
    /// it in atomically; the outgoing graph is disposed only AFTER the swap
    /// completes so the window never sees a transient null
    /// <see cref="Current"/>. Concurrent calls serialize via an internal
    /// gate.
    ///
    /// <para>On build failure the outgoing context is left untouched and
    /// the user is offered the chance to remove the failing entry from
    /// recents.</para>
    /// </summary>
    public async Task<bool> SwitchContextAsync(
        ParsedCommandLine parsed,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        await _switchGate.WaitAsync(ct).ConfigureAwait(true);
        IsSwitching = true;
        SwitchingStatus = $"Loading {DescribeRepo(parsed.RepoPath)}\u2026";
        try
        {
            return await SwitchContextCoreAsync(parsed, review: null, recordRecent: true, ct).ConfigureAwait(true);
        }
        finally
        {
            IsSwitching = false;
            SwitchingStatus = string.Empty;
            _switchGate.Release();
        }
    }

    private async Task<bool> SwitchContextCoreAsync(
        ParsedCommandLine parsed,
        IReviewRef? review,
        bool recordRecent,
        CancellationToken ct)
    {
        SwitchingStatus = "Loading repository\u2026";
        var newScope = new ContextScope(_appShutdownToken);
        MainViewModel newVm;
        try
        {
            newVm = await _contextFactory(parsed, _services, newScope, ct, review).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Build (or partial construction) failed — tear down whatever
            // was registered with the half-built scope. The current VM /
            // scope are untouched; the user keeps their existing context.
            await newScope.DisposeAsync().ConfigureAwait(true);

            var msg = ex is ContextBuildException ex2
                ? ex2.Message
                : $"Failed to switch context: {ex.Message}";

            if (_dialog.ConfirmRemoveStaleEntry(parsed.RepoPath, msg))
            {
                try
                {
                    var identity = ContextIdentityFactory.Create(parsed.RepoPath, parsed.Left, parsed.Right);
                    await _services.RecentContextsService.RemoveAsync(identity, ct).ConfigureAwait(true);
                }
                catch
                {
                    // Best-effort: failure to remove a recents entry should
                    // not propagate as a switch failure (the switch already
                    // failed for a different reason).
                }
            }
            return false;
        }

        // Atomic swap on the calling (UI) thread.
        var outgoingVm = _current;
        DetachPullRequestSubscription();
        _current = newVm;
        _currentScope = newScope;
        OnCurrentChanged();

        // Subscribe to the new VM's PR watcher (if any) so head/base
        // SHA shifts trigger an auto-rebuild. Wiring lives on the
        // coordinator (not the VM) because rebuilding requires the
        // switch gate the coordinator owns.
        if (review is PullRequestRef pr && newVm.PullRequestWatcher is not null)
        {
            AttachPullRequestSubscription(newVm, pr, parsed.RepoPath);
        }

        // Outgoing VM / scope are dropped from this object's state above;
        // dispose them only after the new context is live so listeners see
        // a non-null Current at all times.
        if (outgoingVm is not null)
        {
            await DisposeShellAsync(outgoingVm).ConfigureAwait(true);
        }

        if (recordRecent)
        {
            await TryRecordAsync(parsed, review: review, ct: ct).ConfigureAwait(true);
        }
        return true;
    }

    /// <summary>
    /// Dispose the currently-active view-model (called from
    /// <c>Window.Closed</c>). Safe to call multiple times.
    /// </summary>
    public async ValueTask DisposeCurrentAsync()
    {
        var outgoing = _current;
        DetachPullRequestSubscription();
        _current = null;
        _currentScope = null;
        OnCurrentChanged();

        if (outgoing is not null)
        {
            await DisposeShellAsync(outgoing).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// <see cref="IContextSwitcher"/> entry point used by the recents
    /// dropdown. For local rows, converts a <see cref="RecentLaunchContext"/>
    /// into a <see cref="ParsedCommandLine"/> using the stored display
    /// sides verbatim and delegates to <see cref="SwitchContextAsync"/>.
    /// For review-mode rows (D8 — always re-resolve), runs the
    /// provider's resolver under <see cref="_switchGate"/> so concurrent
    /// dropdown clicks can't race on the same clone's object DB, then
    /// swaps in the freshly-resolved context. Both branches preserve
    /// the row's <see cref="RecentLaunchContext.Review"/> on the
    /// recorded recent so subsequent clicks continue to re-resolve.
    /// </summary>
    public async Task<bool> SwitchToRecentAsync(RecentLaunchContext recent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(recent);

        if (recent.Review is null)
        {
            var parsed = new ParsedCommandLine(
                recent.Identity.CanonicalRepoPath,
                recent.LeftDisplay,
                recent.RightDisplay);
            return await SwitchContextAsync(parsed, ct).ConfigureAwait(true);
        }

        // Review-mode row: re-resolve under the switch gate so the
        // resolve and the swap are atomic with respect to other
        // dropdown clicks. The recents path intentionally does NOT
        // offer the missing-clone dialog (offerMissingClonePrompt:
        // false): the row's existence implies the user previously had
        // a working clone, so missing-clone surfaces as a hard error
        // and the user can fix Settings.Repo roots — we don't
        // unilaterally remove the row.
        if (recent.Review is PullRequestRef pr)
        {
            await _switchGate.WaitAsync(ct).ConfigureAwait(true);
            IsSwitching = true;
            SwitchingStatus = DescribePullRequestLoading(pr);
            try
            {
                return await SwitchToGitHubPullRequestUnderGateAsync(
                    pr, offerMissingClonePrompt: false, ct).ConfigureAwait(true);
            }
            finally
            {
                IsSwitching = false;
                SwitchingStatus = string.Empty;
                _switchGate.Release();
            }
        }

        // Unknown IReviewRef impl — should be unreachable in v1 (only
        // PullRequestRef exists). Defensive: surface a clear error
        // rather than silently failing.
        _dialog.ShowError("DiffViewer",
            $"Unknown review provider for recents row: {recent.Review.ProviderId}.");
        return false;
    }

    /// <summary>
    /// <see cref="IContextSwitcher"/> entry point used by the "New diff"
    /// dialog. Dispatches by <see cref="DiffLaunchSource"/> variant:
    /// <list type="bullet">
    ///   <item><see cref="DiffLaunchSource.Local"/> →
    ///         <see cref="SwitchContextAsync"/></item>
    ///   <item><see cref="DiffLaunchSource.GitHubPullRequest"/> →
    ///         resolver + missing-clone dialog + atomic swap</item>
    /// </list>
    /// Adding a new source variant adds one new <c>switch</c> arm here.
    /// </summary>
    public async Task<bool> SwitchToAsync(DiffLaunchSource source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        switch (source)
        {
            case DiffLaunchSource.Local local:
                return await SwitchContextAsync(local.Parsed, ct).ConfigureAwait(true);

            case DiffLaunchSource.GitHubPullRequest gh:
                await _switchGate.WaitAsync(ct).ConfigureAwait(true);
                IsSwitching = true;
                SwitchingStatus = DescribePullRequestLoading(gh.Pr);
                try
                {
                    return await SwitchToGitHubPullRequestUnderGateAsync(
                        gh.Pr, offerMissingClonePrompt: true, ct).ConfigureAwait(true);
                }
                finally
                {
                    IsSwitching = false;
                    SwitchingStatus = string.Empty;
                    _switchGate.Release();
                }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(source),
                    $"Unhandled DiffLaunchSource variant: {source.GetType().Name}");
        }
    }

    /// <summary>
    /// Shared body for "switch to a GitHub PR": resolve (optionally via
    /// the missing-clone prompt), swap atomically through
    /// <see cref="SwitchContextCoreAsync"/>, and re-stamp the recents
    /// row. Called by both <see cref="SwitchToAsync"/> (PR variant)
    /// and <see cref="SwitchToRecentAsync"/> (review-row branch); the
    /// switch gate must already be held by the caller.
    /// </summary>
    private async Task<bool> SwitchToGitHubPullRequestUnderGateAsync(
        PullRequestRef pr,
        bool offerMissingClonePrompt,
        CancellationToken ct)
    {
        var progress = new Progress<string>(s => SwitchingStatus = s);
        var resolution = offerMissingClonePrompt
            ? await ResolveWithMissingClonePromptAsync(pr, ct).ConfigureAwait(true)
            : await _services.PullRequestResolver.ResolveAsync(pr, progress, ct).ConfigureAwait(true);

        switch (resolution)
        {
            case PullRequestResolution.Ready ready:
                // SwitchContextCoreAsync handles recents recording when
                // recordRecent: true; we want the same behavior here
                // (re-stamp on a successful interactive PR switch) so
                // recordRecent is left default-true.
                var swapped = await SwitchContextCoreAsync(
                    ready.Parsed, review: pr, recordRecent: true, ct).ConfigureAwait(true);
                return swapped;

            case PullRequestResolution.MissingClone:
                // Two callers, two messages:
                //   * recents row (offerMissingClonePrompt: false): the
                //     row pointed at a clone we used to have; surface a
                //     targeted error and let the user fix Settings.
                //   * new-diff dialog (offerMissingClonePrompt: true):
                //     reached only when the missing-clone dialog
                //     declined to map the clone; surface the same
                //     wording the cold-launch path uses.
                _dialog.ShowError("DiffViewer", offerMissingClonePrompt
                    ? $"DiffViewer still can't find a local clone of {pr.Owner}/{pr.Repo}. "
                      + "Add a repo root in Settings or browse to the clone via the missing-clone dialog."
                    : $"DiffViewer can no longer find the clone for {pr.Owner}/{pr.Repo}. "
                      + "Check the Repo roots setting or relaunch with the PR URL "
                      + "to re-pick the clone path.");
                return false;

            case PullRequestResolution.Failed failed:
                _dialog.ShowError("DiffViewer", failed.Message);
                return false;

            default:
                _dialog.ShowError("DiffViewer",
                    $"Unexpected PR resolution state for {pr.Owner}/{pr.Repo}#{pr.Number}.");
                return false;
        }
    }

    private async Task TryRecordAsync(
        ParsedCommandLine parsed,
        IReviewRef? review = null,
        CancellationToken ct = default)
    {
        try
        {
            // Stash refs (stash@{N}, stash@{N}^1) are reflog aliases
            // that rebind on every `git stash push`, so caching them as
            // a "recent" creates stale pointers. Skip recording.
            if (IsStashRef(parsed.Left) || IsStashRef(parsed.Right))
            {
                return;
            }

            var identity = ContextIdentityFactory.Create(parsed.RepoPath, parsed.Left, parsed.Right);
            await _services.RecentContextsService.RecordLaunchAsync(
                identity, parsed.Left, parsed.Right, review, ct).ConfigureAwait(true);
        }
        catch
        {
            // Recording is best-effort: a launch should not be failed
            // because we couldn't update recents.json.
        }
    }

    /// <summary>
    /// True when the side looks like a stash reflog alias
    /// (<c>stash@{N}</c> or <c>stash@{N}^1</c>). Used to suppress
    /// recents recording for stash comparisons (stash aliases are
    /// transient and rebind on every <c>git stash push</c>).
    /// </summary>
    internal static bool IsStashRef(DiffSide side)
    {
        if (side is not DiffSide.CommitIsh commitIsh) return false;
        var reference = commitIsh.Reference;
        return reference.StartsWith("stash@{", StringComparison.Ordinal);
    }

    /// <summary>
    /// Handle a cold-launch failure. Returns <c>true</c> when the app
    /// can continue (empty-state shell installed, window should show);
    /// <c>false</c> when the user-facing error has been shown and
    /// shutdown has been requested.
    /// </summary>
    /// <remarks>
    /// The structured error message is always also forwarded to the
    /// supplied <c>stderrWriter</c> (when one was wired), regardless of
    /// whether the empty-state fallback fires. This is what lets CLI
    /// consumers (e.g. <c>git difftool</c>) see the failure in the
    /// terminal that launched DiffViewer; the on-screen dialog / empty
    /// state covers the GUI case.
    /// </remarks>
    private bool HandleColdLaunchFailure(string errorMessage)
    {
        // CLI consumers expect parse / launch failures to land in stderr
        // even when the GUI also surfaces them — without this the only
        // signal a script gets is "DiffViewer.exe popped a dialog".
        // Best-effort: a console write throwing must not derail launch.
        if (_stderrWriter is not null)
        {
            try { _stderrWriter(errorMessage); } catch { /* best-effort */ }
        }

        // Cold-launch fallback: swap in an empty-state shell so the
        // user can pick a recent from the dropdown or use "New diff" to
        // open a repository, rather than seeing the app immediately exit.
        var recents = new RecentContextsViewModel(
            _services.RecentContextsService,
            this,
            currentIdentity: null,
            _services.NewDiffDialogHost);

        string guidance = _services.RecentContextsService.Current.Count > 0
            ? "Pick a recent context above, or use \u201cNew diff\u201d to open a repository."
            : "Use \u201cNew diff\u201d to open a repository.";

        var emptyVm = new EmptyContextViewModel(
            recents,
            $"{errorMessage}{Environment.NewLine}{Environment.NewLine}{guidance}");

        _current = emptyVm;
        _currentScope = null;
        OnCurrentChanged();
        return true;
    }

    private static async Task DisposeShellAsync(IShellViewModel shell)
    {
        // MainViewModel implements IAsyncDisposable; EmptyContextViewModel
        // is a plain IDisposable. Call the appropriate one and swallow
        // failures (the outgoing graph is unreachable; let it be GC'd).
        try
        {
            switch (shell)
            {
                case IAsyncDisposable async:
                    await async.DisposeAsync().ConfigureAwait(true);
                    break;
                case IDisposable sync:
                    sync.Dispose();
                    break;
            }
        }
        catch { /* best-effort */ }
    }

    private void OnCurrentChanged()
    {
        OnPropertyChanged(nameof(Current));
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string DescribePullRequestLoading(PullRequestRef pr) =>
        $"Loading PR #{pr.Number} from {pr.Owner}/{pr.Repo}\u2026";

    // ---------------- PR watcher subscription + auto-refresh ----------------

    private void AttachPullRequestSubscription(MainViewModel vm, PullRequestRef pr, string repoPath)
    {
        if (vm.PullRequestWatcher is not { } watcher) return;

        DetachPullRequestSubscription();

        EventHandler<PullRequestChangedEventArgs> handler =
            (_, e) => _ = HandlePullRequestChangedAsync(vm, pr, repoPath, e);

        watcher.Changed += handler;
        _currentPrSubscriber = vm;
        _currentPrHandler = handler;
        _currentPrRef = pr;
        _currentPrRepoPath = repoPath;
    }

    private void DetachPullRequestSubscription()
    {
        if (_currentPrSubscriber?.PullRequestWatcher is { } watcher && _currentPrHandler is not null)
        {
            watcher.Changed -= _currentPrHandler;
        }
        _currentPrSubscriber = null;
        _currentPrHandler = null;
        _currentPrRef = null;
        _currentPrRepoPath = null;
    }

    /// <summary>
    /// Handle a single <see cref="IPullRequestWatcher.Changed"/> event
    /// from the current PR-backed VM. Marshals to the UI thread,
    /// drops events from a watcher whose VM is no longer current
    /// (mid-swap race), and dispatches by change kind:
    /// <list type="bullet">
    ///   <item><see cref="PullRequestChangeKind.PollFailed"/> → toast only.</item>
    ///   <item><see cref="PullRequestChangeKind.StateChanged"/> with no
    ///     SHA movement → toast only ("This PR is now merged.").</item>
    ///   <item><see cref="PullRequestChangeKind.HeadMoved"/> or
    ///     <see cref="PullRequestChangeKind.BaseMoved"/> → try the
    ///     switch gate; if contended, drop the tick. Otherwise
    ///     rebuild the context using the watcher's pre-resolved
    ///     snapshot (no second resolver call) and toast on success.</item>
    /// </list>
    /// </summary>
    private async Task HandlePullRequestChangedAsync(
        MainViewModel vm,
        PullRequestRef pr,
        string repoPath,
        PullRequestChangedEventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(() => _ = HandlePullRequestChangedAsync(vm, pr, repoPath, e));
            return;
        }

        // Defensive: if a swap already moved us off this VM (or onto a
        // different PR), drop the event. The old watcher will be
        // disposed shortly.
        if (!ReferenceEquals(_current, vm)) return;

        if ((e.Kind & PullRequestChangeKind.PollFailed) != 0)
        {
            vm.ToastHandler?.Invoke(e.FailureMessage ?? "PR polling failed.");
            return;
        }

        bool shaMoved = (e.Kind & (PullRequestChangeKind.HeadMoved | PullRequestChangeKind.BaseMoved)) != 0;

        if (!shaMoved)
        {
            // Lifecycle-only change. Toast, do not rebuild.
            if ((e.Kind & PullRequestChangeKind.StateChanged) != 0)
            {
                var msg = DescribeLifecycle(e.NewInfo);
                if (msg is not null) vm.ToastHandler?.Invoke(msg);
            }
            return;
        }

        if (e.NewSnapshot is null) return;

        // Try the switch gate non-blockingly: if a manual switch is in
        // flight (recents click, new-diff dialog), drop the tick. The
        // next periodic poll will discover the same change and retry.
        if (!await _switchGate.WaitAsync(0, _appShutdownToken).ConfigureAwait(true))
        {
            return;
        }
        IsSwitching = true;
        SwitchingStatus = $"Refreshing PR #{pr.Number}\u2026";
        try
        {
            var rebuilt = new ParsedCommandLine(
                repoPath,
                new DiffSide.CommitIsh(e.NewSnapshot.MergeBaseSha),
                new DiffSide.CommitIsh(e.NewSnapshot.HeadSha));

            // recordRecent: false — auto-refresh shouldn't move the
            // recents-row timestamp (decision 5: only user clicks do).
            var swapped = await SwitchContextCoreAsync(
                rebuilt, review: pr, recordRecent: false, _appShutdownToken).ConfigureAwait(true);

            if (swapped && _current is MainViewModel newVm)
            {
                newVm.ToastHandler?.Invoke(DescribeAutoRefresh(e));
            }
        }
        catch (OperationCanceledException)
        {
            // App shutting down; nothing to surface.
        }
        catch
        {
            // Auto-refresh must never throw out — the user is still in
            // the old VM and the next poll will retry.
        }
        finally
        {
            IsSwitching = false;
            SwitchingStatus = string.Empty;
            _switchGate.Release();
        }
    }

    private static string? DescribeLifecycle(PullRequestInfo? info)
    {
        if (info is null) return null;
        if (info.Merged) return "This PR is now merged.";
        if (string.Equals(info.State, "closed", StringComparison.OrdinalIgnoreCase))
            return "This PR is now closed.";
        if (string.Equals(info.State, "open", StringComparison.OrdinalIgnoreCase))
            return "This PR was reopened.";
        return null;
    }

    private static string DescribeAutoRefresh(PullRequestChangedEventArgs e)
    {
        bool head = (e.Kind & PullRequestChangeKind.HeadMoved) != 0;
        bool @base = (e.Kind & PullRequestChangeKind.BaseMoved) != 0;
        var time = e.UtcTimestamp.ToLocalTime().ToString("HH:mm");
        return (head, @base) switch
        {
            (true, true) => $"PR head + base moved at {time} \u2014 refreshed.",
            (true, false) => $"PR head moved at {time} \u2014 refreshed.",
            (false, true) => $"PR base moved at {time} \u2014 refreshed.",
            _ => $"PR refreshed at {time}.",
        };
    }

    private static string DescribeRepo(string repoPath)
    {
        if (string.IsNullOrEmpty(repoPath))
        {
            return "repository";
        }
        try
        {
            var name = System.IO.Path.GetFileName(repoPath.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? repoPath : name;
        }
        catch
        {
            return repoPath;
        }
    }
}
