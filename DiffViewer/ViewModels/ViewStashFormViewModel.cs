using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// "View stash" form. Two inputs: a repo path and a stash selection.
/// The stash list is enumerated inline (not a popup — this mode's
/// entire purpose is picking a stash, so the list IS the primary
/// control). Builds a <see cref="ParsedCommandLine"/> with
/// <c>left = CommitIsh("{stash}^1"), right = CommitIsh("{stash}")</c>,
/// matching <c>git stash show</c> semantics: the stash's
/// working-tree commit compared against its parent (HEAD at stash
/// time).
/// </summary>
public sealed partial class ViewStashFormViewModel : LocalRepoFormViewModelBase
{
    private readonly IGitRefEnumerator _enumerator;
    private readonly Func<Func<RefEnumerationResult>, Task<RefEnumerationResult>>? _enumerateRunner;
    private IReadOnlyList<StashEntry> _stashes = Array.Empty<StashEntry>();

    /// <summary>The enumerated stash list. Bound to the inline ListBox.</summary>
    public IReadOnlyList<StashEntry> Stashes => _stashes;

    /// <summary>True when enumeration completed and at least one stash exists.
    /// Drives ListBox visibility so an empty box doesn't render as a
    /// confusing thin line.</summary>
    public bool HasStashes => IsLoaded && _stashes.Count > 0;

    /// <summary>True when enumeration completed and zero stashes exist.
    /// Drives the inline empty-state hint.</summary>
    public bool IsEmpty => IsLoaded && _stashes.Count == 0;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>The stash the user clicked. <c>null</c> when nothing is
    /// selected yet.</summary>
    [ObservableProperty]
    private StashEntry? _selectedStash;

    public ViewStashFormViewModel(
        FormDependencies deps,
        Func<Func<RefEnumerationResult>, Task<RefEnumerationResult>>? enumerateRunner = null)
        : base(deps)
    {
        _enumerator = deps.RefEnumerator ?? throw new ArgumentNullException(nameof(deps));
        _enumerateRunner = enumerateRunner;
        InitializeRepoPath();
    }

    protected override void OnRepoPathResolved()
    {
        // Reset stash state when the repo changes.
        _stashes = Array.Empty<StashEntry>();
        SelectedStash = null;
        IsLoaded = false;
        OnPropertyChanged(nameof(Stashes));
        OnPropertyChanged(nameof(HasStashes));
        OnPropertyChanged(nameof(IsEmpty));

        if (!string.IsNullOrWhiteSpace(RepoPath))
        {
            _ = EnumerateStashesAsync();
        }
    }

    partial void OnSelectedStashChanged(StashEntry? value) => Validate();

    protected override bool HasRequiredLocalInputs => SelectedStash is not null;

    protected override string? ComputeValidationError()
    {
        if (RepoPathError is not null) return RepoPathError;
        if (CanonicalRepoPath is null) return null;

        if (IsLoaded && _stashes.Count == 0)
        {
            return "No stashes in this repository.";
        }

        return null;
    }

    /// <summary>
    /// Enumerate stashes for the current repo path off the UI thread.
    /// Fires on initial construction (when prefilled) and when the
    /// repo path changes.
    /// </summary>
    internal async Task EnumerateStashesAsync()
    {
        if (IsLoading) return;
        if (string.IsNullOrWhiteSpace(RepoPath)) return;

        var repoResult = Validator.ValidateRepoPath(RepoPath);
        if (repoResult is not RepoPathValidation.Valid valid) return;

        var repoPath = valid.CanonicalPath;
        IsLoading = true;
        try
        {
            var enumerate = () => _enumerator.Enumerate(repoPath);
            var result = _enumerateRunner is not null
                ? await _enumerateRunner(enumerate).ConfigureAwait(true)
                : await Task.Run(enumerate).ConfigureAwait(true);

            // Drop stale results if the repo path changed mid-flight.
            var currentValidation = Validator.ValidateRepoPath(RepoPath);
            if (currentValidation is not RepoPathValidation.Valid currentValid
                || !string.Equals(currentValid.CanonicalPath, repoPath, StringComparison.Ordinal))
            {
                return;
            }

            _stashes = result.Stashes;
            IsLoaded = true;
            OnPropertyChanged(nameof(Stashes));
            OnPropertyChanged(nameof(HasStashes));
            OnPropertyChanged(nameof(IsEmpty));
            Validate();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Build the launch source: <c>left = stash^1</c> (HEAD at stash
    /// time), <c>right = stash</c> (the stash's working-tree commit).
    /// </summary>
    public override DiffLaunchSource BuildLaunchSource()
    {
        var stash = SelectedStash
            ?? throw new InvalidOperationException("No stash selected.");
        var parsed = new ParsedCommandLine(
            CanonicalRepoPath ?? RepoPath,
            new DiffSide.CommitIsh($"{stash.SymbolicName}^1"),
            new DiffSide.CommitIsh(stash.SymbolicName));
        return new DiffLaunchSource.Local(parsed);
    }
}
