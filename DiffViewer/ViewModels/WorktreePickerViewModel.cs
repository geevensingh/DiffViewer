using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// Backs the worktree-picker <see cref="System.Windows.Controls.Primitives.Popup"/>
/// attached to the repo-path input in the "New diff" dialog. Lists every
/// worktree of the repository the user has already typed, so switching
/// from one checkout to another is a click instead of remembering where
/// <c>git worktree add</c> put things.
///
/// <para><b>Why every local form gets one</b>: it would be tempting to
/// show this only for working-tree modes, since two worktrees of a
/// repository share one object database and all shared refs — so
/// <c>main..feature</c> resolves identically from either. But
/// <c>HEAD</c> is per-worktree, and the dialog advertises
/// <c>HEAD~3</c> as valid input, so which worktree you point at changes
/// the answer for commit-ish forms too.</para>
///
/// <para><b>Lifetime</b> mirrors <see cref="RefPickerViewModel"/>: one
/// per form, constructed once, handed a write-back callback that
/// replaces the form's repo path, and re-pointed via
/// <see cref="CanonicalRepoPath"/> whenever that path changes.</para>
/// </summary>
public sealed partial class WorktreePickerViewModel : ObservableObject
{
    private readonly IGitWorktreeEnumerator _enumerator;
    private readonly Action<string> _writeBack;
    private readonly Func<Func<IReadOnlyList<WorktreeEntry>>, Task<IReadOnlyList<WorktreeEntry>>>? _enumerateRunner;

    public WorktreePickerViewModel(
        IGitWorktreeEnumerator enumerator,
        Action<string> writeBack,
        string? initialCanonicalRepoPath = null,
        Func<Func<IReadOnlyList<WorktreeEntry>>, Task<IReadOnlyList<WorktreeEntry>>>? enumerateRunner = null)
    {
        _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
        _writeBack = writeBack ?? throw new ArgumentNullException(nameof(writeBack));
        _enumerateRunner = enumerateRunner;
        _canonicalRepoPath = initialCanonicalRepoPath;
    }

    /// <summary>The canonical repo path the picker operates against.
    /// Setting this discards the cached enumeration;
    /// <see cref="EnsureLoadedAsync"/> re-enumerates on demand.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnabled))]
    private string? _canonicalRepoPath;

    partial void OnCanonicalRepoPathChanged(string? value)
    {
        _worktrees = Array.Empty<WorktreeEntry>();
        IsLoaded = false;
        RaiseListDerivedChanged();
    }

    /// <summary>True when the picker has a repo path to work from; the
    /// trigger button binds to this.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(CanonicalRepoPath);

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEmptyState))]
    private bool _isLoaded;

    private IReadOnlyList<WorktreeEntry> _worktrees = Array.Empty<WorktreeEntry>();

    /// <summary>Every worktree of the current repository, main first.</summary>
    public IReadOnlyList<WorktreeEntry> Worktrees => _worktrees;

    /// <summary>
    /// True when the enumeration found somewhere other than here to go.
    ///
    /// <para>Deliberately "any entry that isn't current" rather than a
    /// row count. The enumerator omits the main worktree for a bare hub,
    /// so counting rows would report a single linked worktree as no
    /// alternative at all. A row that is present but unreachable (a
    /// pruned worktree) still counts, so the popup never claims there is
    /// nothing to switch to while visibly listing something.</para>
    /// </summary>
    public bool HasAlternativeWorktrees => _worktrees.Any(entry => !entry.IsCurrent);

    /// <summary>
    /// True when the popup should say the repository has nowhere else to
    /// go. Gated on <see cref="IsLoaded"/> so the message does not sit
    /// next to "Loading…" claiming an answer enumeration hasn't produced
    /// yet.
    /// </summary>
    public bool ShowsEmptyState => IsLoaded && !HasAlternativeWorktrees;

    /// <summary>
    /// Enumerate worktrees for the current repo path, off the UI thread.
    /// Idempotent — repeated calls while loaded or loading are no-ops,
    /// so the popup can call it on every open.
    /// </summary>
    public async Task EnsureLoadedAsync()
    {
        if (IsLoaded || IsLoading) return;
        if (string.IsNullOrWhiteSpace(CanonicalRepoPath)) return;

        IsLoading = true;
        try
        {
            // Loop rather than bail on a stale result. The picker can be
            // re-pointed mid-flight, and a caller that arrives during
            // the load is turned away by the IsLoading guard above — so
            // simply dropping the stale result would leave an
            // already-open popup empty with nothing left to trigger a
            // reload. Re-enumerating here is what that turned-away
            // caller is relying on.
            while (true)
            {
                var repoPath = CanonicalRepoPath;
                if (string.IsNullOrWhiteSpace(repoPath)) return;

                var enumerate = () => _enumerator.Enumerate(repoPath!);
                var result = _enumerateRunner is not null
                    ? await _enumerateRunner(enumerate).ConfigureAwait(true)
                    : await Task.Run(enumerate).ConfigureAwait(true);

                if (!string.Equals(CanonicalRepoPath, repoPath, StringComparison.Ordinal))
                {
                    continue;
                }

                _worktrees = result;
                IsLoaded = true;
                RaiseListDerivedChanged();
                return;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RaiseListDerivedChanged()
    {
        OnPropertyChanged(nameof(Worktrees));
        OnPropertyChanged(nameof(HasAlternativeWorktrees));
        OnPropertyChanged(nameof(ShowsEmptyState));
    }

    /// <summary>
    /// Write the chosen worktree's working directory back into the
    /// form's repo path. Worktrees whose directory is gone are rejected
    /// — the row is shown so the user understands what git still knows
    /// about, not so they can diff against a directory that isn't there.
    /// </summary>
    [RelayCommand]
    private void PickWorktree(WorktreeEntry? entry)
    {
        if (entry is null || entry.IsMissing) return;
        _writeBack(entry.WorkingDirectory);
    }
}
