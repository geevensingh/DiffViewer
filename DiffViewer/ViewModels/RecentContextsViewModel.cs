using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.Utility;

namespace DiffViewer.ViewModels;

/// <summary>
/// Per-shell view-model that drives the recents <c>ComboBox</c> in
/// <see cref="Views.RecentsBarView"/>. Lifetime is per-context (one
/// instance per <see cref="MainViewModel"/> or
/// <see cref="EmptyContextViewModel"/>); the underlying
/// <see cref="IRecentContextsService"/> is App-level.
///
/// <para><b>SelectedItem two-way binding</b>: WPF assigns
/// <see cref="SelectedItem"/> when the user picks an entry; the setter
/// detects whether the picked entry differs from the currently-active
/// identity and, if so, fires off
/// <see cref="IContextSwitcher.SwitchToRecentAsync"/>. The setter
/// short-circuits when the picked entry equals the current identity,
/// so the binding round-trip after a successful switch (when the new
/// VM's getter naturally returns the just-swapped identity) does not
/// loop.</para>
///
/// <para><b>IsEnabled</b> is bound to <c>!ContextSwitcher.IsSwitching</c>
/// so the dropdown disables itself for the duration of an in-flight
/// switch.</para>
///
/// <para><b>NewDiffCommand</b> opens the "New diff" modal dialog
/// (Phase 2/3 of the in-app mode-switching feature). The command is
/// wired only when both a <see cref="IContextSwitcher"/> and a
/// <see cref="INewDiffDialogHost"/> are provided; tests that don't
/// exercise that path leave them null and the button hides.</para>
///
/// <para><b>WorktreePicker</b> re-points the <em>current</em> diff at
/// another worktree of the same repository, keeping both sides
/// untouched. It reuses <see cref="WorktreePickerViewModel"/> — the
/// same VM the "New diff" dialog binds — with a write-back that runs
/// an in-place context switch instead of editing a text box. Wired
/// only when a switcher and a worktree enumerator are supplied.</para>
/// </summary>
public sealed class RecentContextsViewModel : ObservableObject, IDisposable
{
    private readonly IRecentContextsService _service;
    private readonly IContextSwitcher? _switcher;
    private readonly INewDiffDialogHost? _newDiffDialogHost;
    private readonly ContextIdentity? _currentIdentity;
    private bool _disposed;

    public RecentContextsViewModel(
        IRecentContextsService service,
        IContextSwitcher? switcher,
        ContextIdentity? currentIdentity,
        INewDiffDialogHost? newDiffDialogHost = null,
        IGitWorktreeEnumerator? worktreeEnumerator = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _switcher = switcher;
        _newDiffDialogHost = newDiffDialogHost;
        _currentIdentity = currentIdentity;

        _service.Changed += OnRecentsChanged;
        if (_switcher is not null)
        {
            _switcher.PropertyChanged += OnSwitcherPropertyChanged;
        }

        NewDiffCommand = new AsyncRelayCommand(OpenNewDiffAsync, () => IsNewDiffEnabled);

        IsWorktreeSwitchEnabled = worktreeEnumerator is not null
            && _switcher is not null
            && _currentIdentity is not null;
        WorktreePicker = IsWorktreeSwitchEnabled
            ? new WorktreePickerViewModel(
                worktreeEnumerator!,
                writeBack: path => _ = SwitchToWorktreeAsync(path),
                initialCanonicalRepoPath: _currentIdentity!.Value.CanonicalRepoPath)
            : null;
    }

    /// <summary>
    /// Picker listing the worktrees of the active repository, or
    /// <c>null</c> when worktree switching isn't wired (cold-launch
    /// empty state, tests). The view hides its trigger when null.
    /// </summary>
    public WorktreePickerViewModel? WorktreePicker { get; }

    /// <summary>True when the worktree switcher should be shown.</summary>
    public bool IsWorktreeSwitchEnabled { get; }

    /// <summary>
    /// Re-open the current comparison rooted at another worktree.
    /// Both sides are carried over verbatim: the point is to ask the
    /// same question of a different checkout. Note that a ref like
    /// <c>HEAD</c> is per-worktree, so the answer legitimately differs.
    /// </summary>
    private async Task SwitchToWorktreeAsync(string workingDirectory)
    {
        if (_switcher is null || _currentIdentity is null) return;
        if (string.IsNullOrWhiteSpace(workingDirectory)) return;
        if (ContextIdentityFactory.RepoPathsEqual(
                workingDirectory, _currentIdentity.Value.CanonicalRepoPath))
        {
            // Already here; a switch would tear down and rebuild the
            // whole context for no change.
            return;
        }

        var parsed = new ParsedCommandLine(
            workingDirectory,
            _currentIdentity.Value.Left,
            _currentIdentity.Value.Right);

        try
        {
            await _switcher.SwitchToAsync(
                new DiffLaunchSource.Local(parsed), CancellationToken.None).ConfigureAwait(true);
        }
        catch
        {
            // The switcher surfaces its own errors to the user; a
            // failed switch must not take the shell down with it.
        }
    }

    /// <summary>MRU-ordered snapshot from the singleton service.</summary>
    public IReadOnlyList<RecentLaunchContext> Items => _service.Current;

    /// <summary>
    /// Projection of <see cref="Items"/> with pre-computed display
    /// strings (Title, Subtitle, Tooltip). The dropdown binds to this
    /// collection rather than to <see cref="Items"/> so the XAML stays
    /// converter-free.
    /// </summary>
    public IReadOnlyList<RecentContextItem> ItemViews => _service.Current
        .Select(c => new RecentContextItem(c))
        .ToList();

    /// <summary>
    /// The entry corresponding to the currently-loaded context, or
    /// <c>null</c> when there is no active context (cold-launch
    /// empty-state) or when the active identity isn't in the list.
    /// </summary>
    public RecentContextItem? SelectedItem
    {
        get
        {
            if (_currentIdentity is not { } id) return null;
            var match = Items.FirstOrDefault(i => Matches(i.Identity, id));
            return match is null ? null : new RecentContextItem(match);
        }
        set
        {
            // WPF calls the setter on user selection. Setter is the
            // ONLY trigger for switching contexts from the dropdown.
            if (value is null) return;
            var picked = value.Source;
            if (_currentIdentity is { } current && Matches(picked.Identity, current))
            {
                // Selecting the already-active item is a no-op (also
                // covers the post-switch binding round-trip).
                return;
            }
            if (_switcher is null)
            {
                // Switcher hasn't been wired (tests, or pre-coordinator
                // bootstrap). Drop the selection on the floor — re-raise
                // SelectedItem so the ComboBox doesn't latch the unhandled
                // pick.
                OnPropertyChanged(nameof(SelectedItem));
                return;
            }

            // Fire-and-forget; the coordinator handles all errors and the
            // post-switch UI rebind.
            _ = SwitchAsync(picked);
        }
    }

    /// <summary>
    /// <c>true</c> when the dropdown is interactive. Bound (negated)
    /// to <see cref="System.Windows.Controls.ComboBox.IsEnabled"/>.
    /// </summary>
    public bool IsEnabled => _switcher is null || !_switcher.IsSwitching;

    /// <summary>True when there are no entries to show — used to surface a hint label.</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// Whether the <see cref="NewDiffCommand"/> can run. Bound (via
    /// <c>BooleanToVisibilityConverter</c>) to the button's visibility
    /// so tests + degenerate startup paths that lack the host or
    /// switcher don't surface a non-functional button.
    /// </summary>
    public bool IsNewDiffEnabled => _newDiffDialogHost is not null && _switcher is not null;

    /// <summary>
    /// Opens the "New diff" modal dialog. On confirmation, dispatches
    /// the user's selection to <see cref="IContextSwitcher.SwitchToAsync"/>.
    /// </summary>
    public IAsyncRelayCommand NewDiffCommand { get; }

    private async Task OpenNewDiffAsync()
    {
        if (_newDiffDialogHost is null || _switcher is null) return;

        var prefilledRepoPath = _currentIdentity?.CanonicalRepoPath;
        DiffLaunchSource? choice;
        try
        {
            choice = await _newDiffDialogHost.ShowAsync(prefilledRepoPath, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch
        {
            // Host failures (rare; e.g. dispatcher already shut down)
            // are not worth a user-facing toast on the cancel path.
            return;
        }
        if (choice is null) return; // user cancelled

        try
        {
            await _switcher.SwitchToAsync(choice, CancellationToken.None).ConfigureAwait(true);
        }
        catch
        {
            // Same rationale as the recents-row path: the coordinator
            // already surfaces failures via the dialog service.
        }
    }

    private async Task SwitchAsync(RecentLaunchContext picked)
    {
        try
        {
            await _switcher!.SwitchToRecentAsync(picked).ConfigureAwait(true);
        }
        catch
        {
            // Any thrown exception from the coordinator is already
            // surfaced via the dialog service; we just swallow here so
            // the fire-and-forget Task doesn't crash the dispatcher
            // via UnobservedTaskException.
        }
    }

    private void OnRecentsChanged(object? sender, EventArgs e)
    {
        // The service's Changed event can fire on any thread (the service
        // itself doesn't marshal). WPF requires PropertyChanged on the UI
        // thread for ComboBox bindings; in our flow the service is only
        // ever mutated from the UI thread (App.OnStartup load + coordinator
        // record/remove on the UI scheduler) so this is safe in practice.
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(ItemViews));
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnSwitcherPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(IContextSwitcher.IsSwitching))
        {
            OnPropertyChanged(nameof(IsEnabled));
        }
    }

    private static bool Matches(ContextIdentity a, ContextIdentity b)
    {
        return ContextIdentityFactory.RepoPathsEqual(a.CanonicalRepoPath, b.CanonicalRepoPath)
            && Equals(a.Left, b.Left)
            && Equals(a.Right, b.Right);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _service.Changed -= OnRecentsChanged;
        if (_switcher is not null)
        {
            _switcher.PropertyChanged -= OnSwitcherPropertyChanged;
        }
    }
}

/// <summary>
/// Per-row projection of a <see cref="RecentLaunchContext"/> with
/// pre-computed display strings. Equality is by underlying
/// <see cref="ContextIdentity"/> so WPF's <c>SelectedItem</c> matching
/// (which uses <c>Equals</c> against the projected list) round-trips
/// correctly across rebuilds of the projection.
/// </summary>
public sealed class RecentContextItem : IEquatable<RecentContextItem>
{
    public RecentContextItem(RecentLaunchContext source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public RecentLaunchContext Source { get; }

    /// <summary>Primary line. e.g. <c>"DevTools · main → &lt;working-tree&gt;"</c>
    /// for local rows, <c>"DevTools · PR owner/repo#42"</c> for review-mode rows.
    /// A row pointing at a linked worktree carries the worktree's name in
    /// brackets — <c>"DiffViewer [feature-x] · HEAD → WT"</c> — because
    /// otherwise every worktree of a repository renders identically apart
    /// from a path the dropdown doesn't show.</summary>
    public string Title
    {
        get
        {
            var name = RepositoryLabel;
            if (Source.Review is { } review)
            {
                return $"{name} · PR {review.Slug}";
            }
            return $"{name} · {ShortLabelFor(Source.LeftDisplay)} → {ShortLabelFor(Source.RightDisplay)}";
        }
    }

    /// <summary>
    /// The repository portion of <see cref="Title"/>. Prefers the
    /// repository name captured at launch time, falling back to the
    /// path's leaf for rows written before worktree labelling existed.
    /// </summary>
    private string RepositoryLabel
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Source.RepositoryName)
                ? SafeBaseName(Source.Identity.CanonicalRepoPath)
                : Source.RepositoryName!;

            return string.IsNullOrWhiteSpace(Source.WorktreeName)
                ? name
                : $"{name} [{Source.WorktreeName}]";
        }
    }

    /// <summary>Secondary line: relative-time label (e.g. <c>"2h ago"</c>).</summary>
    public string Subtitle => RelativeTimeFormatter.Format(Source.LastUsedUtc);

    /// <summary>Tooltip with the full repo path and full ref strings.</summary>
    public string Tooltip
    {
        get
        {
            if (Source.Review is { } review)
            {
                // For review-mode rows, surface the review identity in
                // the tooltip alongside the resolved (merge-base, head)
                // SHAs so the user can see both "what review this row
                // points at" and "what the diff engine actually
                // compared." The SHAs may become stale between
                // launches (D8) — they reflect the last resolved
                // state, not the live review head.
                return $"Repository: {Source.Identity.CanonicalRepoPath}{Environment.NewLine}" +
                       $"Pull request: {review.WebUrl}{Environment.NewLine}" +
                       $"Last resolved base:  {LabelFor(Source.LeftDisplay)}{Environment.NewLine}" +
                       $"Last resolved head:  {LabelFor(Source.RightDisplay)}{Environment.NewLine}" +
                       $"Last used: {Source.LastUsedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
            }
            return $"Repository: {Source.Identity.CanonicalRepoPath}{Environment.NewLine}" +
                   $"Left:  {LabelFor(Source.LeftDisplay)}{Environment.NewLine}" +
                   $"Right: {LabelFor(Source.RightDisplay)}{Environment.NewLine}" +
                   $"Last used: {Source.LastUsedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
        }
    }

    /// <summary>
    /// Accessibility name read by screen readers. Combines the title with
    /// the relative time so the user hears both pieces of context.
    /// </summary>
    public string AccessibilityName => $"{Title}, {Subtitle}";

    private const int MaxRefLabelChars = 24;

    private static string LabelFor(DiffSide side) => side switch
    {
        DiffSide.WorkingTree => "<working-tree>",
        DiffSide.CommitIsh c => c.Reference,
        _ => side.ToString() ?? string.Empty,
    };

    private static string ShortLabelFor(DiffSide side)
    {
        return side switch
        {
            DiffSide.WorkingTree => "WT",
            DiffSide.CommitIsh c => StringTruncate.MidTruncate(c.Reference, MaxRefLabelChars),
            _ => side.ToString() ?? string.Empty,
        };
    }

    private static string SafeBaseName(string repoPath)
    {
        if (string.IsNullOrEmpty(repoPath)) return string.Empty;
        try
        {
            var trimmed = repoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }
        catch
        {
            return repoPath;
        }
    }

    public bool Equals(RecentContextItem? other)
        => other is not null && Source.Identity.Equals(other.Source.Identity);

    public override bool Equals(object? obj) => Equals(obj as RecentContextItem);

    public override int GetHashCode() => Source.Identity.GetHashCode();
}
