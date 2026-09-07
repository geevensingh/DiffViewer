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
        OnPropertyChanged(nameof(Worktrees));
        OnPropertyChanged(nameof(HasAlternativeWorktrees));
    }

    /// <summary>True when the picker has a repo path to work from; the
    /// trigger button binds to this.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(CanonicalRepoPath);

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoaded;

    private IReadOnlyList<WorktreeEntry> _worktrees = Array.Empty<WorktreeEntry>();

    /// <summary>Every worktree of the current repository, main first.</summary>
    public IReadOnlyList<WorktreeEntry> Worktrees => _worktrees;

    /// <summary>
    /// True once loading found somewhere else to go. Drives the popup's
    /// empty state: a repository with no linked worktrees should say so
    /// rather than showing a one-row list of where the user already is.
    /// </summary>
    public bool HasAlternativeWorktrees => _worktrees.Count > 1;

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
            var repoPath = CanonicalRepoPath;
            var enumerate = () => _enumerator.Enumerate(repoPath!);
            var result = _enumerateRunner is not null
                ? await _enumerateRunner(enumerate).ConfigureAwait(true)
                : await Task.Run(enumerate).ConfigureAwait(true);

            // The user may have re-pointed the picker mid-load. Drop
            // stale results rather than overwrite the new state.
            if (!string.Equals(CanonicalRepoPath, repoPath, StringComparison.Ordinal))
            {
                return;
            }

            _worktrees = result;
            IsLoaded = true;
            OnPropertyChanged(nameof(Worktrees));
            OnPropertyChanged(nameof(HasAlternativeWorktrees));
        }
        finally
        {
            IsLoading = false;
        }
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
