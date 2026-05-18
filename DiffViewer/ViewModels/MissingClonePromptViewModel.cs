using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// Backs the missing-clone dialog that pops up when
/// <see cref="ILocalRepoLocator"/> can't find a local clone for the PR
/// the user is trying to view. Offers three exits:
/// </summary>
/// <list type="bullet">
///   <item><b>Browse to existing clone</b> — the user already cloned the
///     repo (private repos route here). Picks a folder, validates it's
///     a git repo whose remote points at the PR's owner/repo via
///     <see cref="RemoteUrlMatcher"/>, records the mapping, and resolves
///     with the picked path.</item>
///   <item><b>Clone for me</b> — public-repo convenience. Picks a parent
///     directory, clones via <see cref="IGitHubCloner"/> into
///     <c>&lt;parent&gt;/&lt;repo&gt;</c>, records the mapping, and resolves
///     with the new clone path. Auth failure routes the user toward
///     Browse (with cloning instructions).</item>
///   <item><b>Cancel</b> — abort the PR launch.</item>
/// </list>
/// <remarks>
/// <para>The VM is intentionally TaskCompletionSource-driven: callers
/// (the coordinator in Phase 8) <c>await Completion</c> after showing
/// the dialog and the result indicates which exit the user chose.</para>
///
/// <para>The dialog never invokes the libgit2 / file-system / picker
/// machinery itself — every side-effecting seam (folder picker, repo
/// inspector for validating a Browse path, cloner, settings writer,
/// confirmation prompts) is injected so the VM is testable without
/// WPF, libgit2, or disk.</para>
/// </remarks>
public sealed partial class MissingClonePromptViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IRepoInspector _inspector;
    private readonly IGitHubCloner _cloner;
    private readonly Func<string?, string?> _pickFolder;
    private readonly Func<string, bool> _confirmUseUnmatchedRemote;
    private readonly Func<string, bool>? _confirmRememberDefaultClone;
    private readonly TaskCompletionSource<MissingClonePromptResult> _tcs;
    private CancellationTokenSource? _cloneCts;

    public PullRequestRef Pr { get; }

    public string Title => $"DiffViewer needs a local clone of {Pr.Owner}/{Pr.Repo}";

    public string Description =>
        $"To open PR #{Pr.Number}, DiffViewer needs to know where you've cloned "
        + $"{Pr.Host}/{Pr.Owner}/{Pr.Repo}. Browse to an existing clone, or let "
        + "DiffViewer clone the public repo for you.";

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressLabel = string.Empty;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private bool _cloneInProgress;

    public Task<MissingClonePromptResult> Completion => _tcs.Task;

    public MissingClonePromptViewModel(
        PullRequestRef pr,
        ISettingsService settings,
        IRepoInspector inspector,
        IGitHubCloner cloner,
        Func<string?, string?> pickFolder,
        Func<string, bool> confirmUseUnmatchedRemote,
        Func<string, bool>? confirmRememberDefaultClone = null)
    {
        Pr = pr ?? throw new ArgumentNullException(nameof(pr));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _cloner = cloner ?? throw new ArgumentNullException(nameof(cloner));
        _pickFolder = pickFolder ?? throw new ArgumentNullException(nameof(pickFolder));
        _confirmUseUnmatchedRemote = confirmUseUnmatchedRemote
            ?? throw new ArgumentNullException(nameof(confirmUseUnmatchedRemote));
        _confirmRememberDefaultClone = confirmRememberDefaultClone;
        _tcs = new TaskCompletionSource<MissingClonePromptResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// User picked "Browse to existing clone". Validates the picked
    /// folder, optionally confirms with the user if no remote matches,
    /// records the mapping, and resolves with the picked path.
    /// </summary>
    [RelayCommand]
    private void BrowseExisting()
    {
        if (IsBusy)
        {
            return;
        }

        var picked = _pickFolder(null);
        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        if (!_inspector.IsRepository(picked))
        {
            ErrorMessage = $"`{picked}` is not a git repository.";
            return;
        }

        var remotes = SafeGetRemoteUrls(picked);
        var matchesPr = AnyRemoteMatchesPr(remotes);

        if (!matchesPr)
        {
            var summary = remotes.Count == 0
                ? "(no remotes configured)"
                : string.Join(", ", remotes);
            var question =
                $"None of the remotes in `{picked}` look like {Pr.Owner}/{Pr.Repo} "
                + $"(found: {summary}). Use this clone anyway?";
            if (!_confirmUseUnmatchedRemote(question))
            {
                StatusMessage = "Cancelled — picked clone does not match the PR.";
                return;
            }
        }

        RecordMapping(picked);
        Complete(new MissingClonePromptResult.Resolved(picked));
    }

    /// <summary>
    /// User picked "Clone for me". Public-repo only by design (private
    /// repos auth-fail and get routed back to Browse). Updates progress
    /// as the clone runs; cancellable mid-flight.
    /// </summary>
    [RelayCommand]
    private async Task CloneForMeAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var defaultDest = _settings.Current.DefaultCloneDestination;
        var parent = _pickFolder(defaultDest);
        if (string.IsNullOrWhiteSpace(parent))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(defaultDest)
            && _confirmRememberDefaultClone is not null
            && _confirmRememberDefaultClone(parent))
        {
            _settings.Update(s => s with { DefaultCloneDestination = parent });
        }

        var target = Path.Combine(parent, Pr.Repo);
        var cloneUrl = $"https://{Pr.Host}/{Pr.Owner}/{Pr.Repo}.git";

        if (Directory.Exists(target))
        {
            ErrorMessage =
                $"`{target}` already exists. Pick a different parent directory, "
                + "or use Browse to point DiffViewer at the existing clone.";
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        CloneInProgress = true;
        ProgressPercent = 0;
        ProgressLabel = $"Cloning {Pr.Owner}/{Pr.Repo}…";

        _cloneCts = new CancellationTokenSource();
        var progress = new Progress<CloneProgress>(ReportProgress);

        CloneResult result;
        try
        {
            result = await _cloner
                .CloneAsync(cloneUrl, target, progress, _cloneCts.Token)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            result = new CloneResult.Failed(ex.Message);
        }
        finally
        {
            IsBusy = false;
            CloneInProgress = false;
            ProgressLabel = string.Empty;
            ProgressPercent = 0;
            _cloneCts?.Dispose();
            _cloneCts = null;
        }

        switch (result)
        {
            case CloneResult.Success success:
                RecordMapping(success.ClonePath);
                Complete(new MissingClonePromptResult.Resolved(success.ClonePath));
                break;
            case CloneResult.AuthFailed authFailed:
                ErrorMessage = authFailed.Message;
                StatusMessage =
                    "Use Browse to point DiffViewer at an existing local clone.";
                break;
            case CloneResult.Cancelled:
                StatusMessage = "Clone cancelled.";
                break;
            case CloneResult.Failed failed:
                ErrorMessage = failed.Message;
                break;
        }
    }

    /// <summary>
    /// Cancel a clone that's currently in progress. Has no effect if no
    /// clone is running — Cancel-the-whole-dialog is a separate command.
    /// </summary>
    [RelayCommand]
    private void CancelClone()
    {
        _cloneCts?.Cancel();
    }

    /// <summary>User aborted the PR launch entirely.</summary>
    [RelayCommand]
    private void Cancel()
    {
        _cloneCts?.Cancel();
        Complete(new MissingClonePromptResult.Cancelled());
    }

    private void ReportProgress(CloneProgress progress)
    {
        // Combine transfer + checkout into a single 0..100. Both halves
        // can be active simultaneously near the end of a clone, but a
        // simple weighted blend is good enough for a progress bar.
        int transferPercent = progress.TotalObjects > 0
            ? (int)(50.0 * progress.IndexedObjects / progress.TotalObjects)
            : 0;
        int checkoutPercent = progress.CheckoutTotal > 0
            ? (int)(50.0 * progress.CheckoutCompleted / progress.CheckoutTotal)
            : 0;

        int combined = Math.Clamp(transferPercent + checkoutPercent, 0, 100);
        ProgressPercent = combined;

        if (progress.CheckoutTotal > 0)
        {
            ProgressLabel =
                $"Checking out files ({progress.CheckoutCompleted}/{progress.CheckoutTotal})…";
        }
        else if (progress.TotalObjects > 0)
        {
            ProgressLabel =
                $"Receiving objects ({progress.IndexedObjects}/{progress.TotalObjects})…";
        }
    }

    private IReadOnlyList<string> SafeGetRemoteUrls(string path)
    {
        try
        {
            return _inspector.GetRemoteUrls(path);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private bool AnyRemoteMatchesPr(IReadOnlyList<string> remotes)
    {
        var prKey = RepoUrlKey.From(Pr);
        foreach (var url in remotes)
        {
            var key = RemoteUrlMatcher.TryExtractKey(url);
            if (key is not null && key.Equals(prKey))
            {
                return true;
            }
        }
        return false;
    }

    private void RecordMapping(string clonePath)
    {
        _settings.Update(s =>
        {
            var key = RepoUrlKey.From(Pr);
            var next = new Dictionary<RepoUrlKey, string>(s.RepoUrlMappings)
            {
                [key] = clonePath,
            };
            return s with { RepoUrlMappings = next };
        });
    }

    private void Complete(MissingClonePromptResult result)
    {
        _tcs.TrySetResult(result);
    }
}

/// <summary>Outcome of the missing-clone dialog.</summary>
public abstract record MissingClonePromptResult
{
    /// <summary>User chose a local clone path (via Browse or successful Clone).</summary>
    public sealed record Resolved(string ClonePath) : MissingClonePromptResult;

    /// <summary>User cancelled the PR launch.</summary>
    public sealed record Cancelled : MissingClonePromptResult;

    /// <summary>Dialog was forcibly closed with a failure message (used by hosts).</summary>
    public sealed record Failed(string Message) : MissingClonePromptResult;
}
