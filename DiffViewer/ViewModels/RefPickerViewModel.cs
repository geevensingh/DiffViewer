using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// Backs the ref-picker <see cref="System.Windows.Controls.Primitives.Popup"/>
/// embedded in the "New diff" dialog's commit-ish input fields.
/// Enumerates local / remote / tag refs for the supplied repo path
/// (off the UI thread, per AGENTS.md §9), surfaces the user's recent
/// refs in that repo (derived from
/// <see cref="IRecentContextsService"/>; no new persistence), and
/// composes a merge-base of two refs.
///
/// <para><b>Lifetime</b>: one VM per commit-ish input. The owning
/// form VM constructs the picker once, hands it a write-back
/// <see cref="Action{T}"/> callback that updates the bound commit-ish
/// property, and re-points <see cref="CanonicalRepoPath"/> whenever
/// the form's repo path changes.</para>
///
/// <para><b>Filter</b>: case-insensitive substring match applied to
/// every group's <see cref="RefEntry.FriendlyName"/>. The filter is
/// applied at render time via <see cref="VisibleRecentRefs"/> /
/// <see cref="VisibleLocalBranches"/> / etc., so the underlying
/// snapshots stay intact across keystrokes.</para>
///
/// <para><b>Recent refs source</b>: walks
/// <see cref="IRecentContextsService.Current"/>, filters to entries
/// whose <see cref="ContextIdentity.CanonicalRepoPath"/> matches the
/// active repo (via
/// <see cref="ContextIdentityFactory.RepoPathsEqual"/>), pulls each
/// <see cref="DiffSide.CommitIsh.Reference"/>, dedups
/// (case-sensitive — Git treats <c>HEAD</c> and <c>head</c>
/// differently), MRU-orders, caps at
/// <see cref="MaxRecentRefs"/>.</para>
/// </summary>
public sealed partial class RefPickerViewModel : ObservableObject
{
    /// <summary>Cap on the "Recent in this repo" group. Five is enough
    /// to cover the dominant review-flow recency without crowding the
    /// popup's other groups.</summary>
    public const int MaxRecentRefs = 5;

    private readonly IGitRefEnumerator _enumerator;
    private readonly IRecentContextsService _recents;
    private readonly Action<string> _writeBack;
    private readonly Func<Func<RefEnumerationResult>, Task<RefEnumerationResult>>? _enumerateRunner;

    public RefPickerViewModel(
        IGitRefEnumerator enumerator,
        IRecentContextsService recents,
        Action<string> writeBack,
        string? initialCanonicalRepoPath = null,
        Func<Func<RefEnumerationResult>, Task<RefEnumerationResult>>? enumerateRunner = null)
    {
        _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
        _recents = recents ?? throw new ArgumentNullException(nameof(recents));
        _writeBack = writeBack ?? throw new ArgumentNullException(nameof(writeBack));
        _enumerateRunner = enumerateRunner;
        _canonicalRepoPath = initialCanonicalRepoPath;
    }

    /// <summary>The canonical repo path the picker operates against.
    /// Setting this clears the cached enumeration and the
    /// merge-base error; <see cref="EnsureLoadedAsync"/> re-enumerates
    /// on demand (typically called when the popup is opened).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnabled))]
    private string? _canonicalRepoPath;

    partial void OnCanonicalRepoPathChanged(string? value)
    {
        // Repo changed → throw out cached snapshots; the next
        // EnsureLoadedAsync will re-enumerate against the new path.
        _localBranches = Array.Empty<RefEntry>();
        _remoteBranches = Array.Empty<RefEntry>();
        _tags = Array.Empty<RefEntry>();
        _stashes = Array.Empty<StashEntry>();
        _recentRefs = Array.Empty<string>();
        MergeBaseError = null;
        ComputedMergeBase = null;
        IsLoaded = false;
        RaiseFilteredCollectionsChanged();
    }

    /// <summary>True when the picker has a valid repo path; the
    /// "Pick…" button binds to this so an empty / unresolved repo
    /// path disables the trigger.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(CanonicalRepoPath);

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleRecentRefs))]
    [NotifyPropertyChangedFor(nameof(VisibleLocalBranches))]
    [NotifyPropertyChangedFor(nameof(VisibleRemoteBranches))]
    [NotifyPropertyChangedFor(nameof(VisibleTags))]
    [NotifyPropertyChangedFor(nameof(VisibleStashes))]
    [NotifyPropertyChangedFor(nameof(HasAnyVisibleRefs))]
    private string _filter = string.Empty;

    private IReadOnlyList<string> _recentRefs = Array.Empty<string>();
    private IReadOnlyList<RefEntry> _localBranches = Array.Empty<RefEntry>();
    private IReadOnlyList<RefEntry> _remoteBranches = Array.Empty<RefEntry>();
    private IReadOnlyList<RefEntry> _tags = Array.Empty<RefEntry>();
    private IReadOnlyList<StashEntry> _stashes = Array.Empty<StashEntry>();

    /// <summary>Read-only view of the recent refs after applying <see cref="Filter"/>.</summary>
    public IReadOnlyList<string> VisibleRecentRefs =>
        _recentRefs.Where(MatchesFilter).ToArray();

    public IReadOnlyList<RefEntry> VisibleLocalBranches =>
        _localBranches.Where(e => MatchesFilter(e.FriendlyName)).ToArray();

    public IReadOnlyList<RefEntry> VisibleRemoteBranches =>
        _remoteBranches.Where(e => MatchesFilter(e.FriendlyName)).ToArray();

    public IReadOnlyList<RefEntry> VisibleTags =>
        _tags.Where(e => MatchesFilter(e.FriendlyName)).ToArray();

    /// <summary>Read-only view of the stash entries after applying <see cref="Filter"/>.
    /// Matches against <see cref="StashEntry.SymbolicName"/>, <see cref="StashEntry.Subject"/>,
    /// and <see cref="StashEntry.TipShortSha"/>.</summary>
    public IReadOnlyList<StashEntry> VisibleStashes =>
        _stashes.Where(s => MatchesFilter(s.SymbolicName)
                          || MatchesFilter(s.Subject)
                          || MatchesFilter(s.TipShortSha)).ToArray();

    /// <summary>True iff at least one of the four visible groups has
    /// at least one entry. Drives the "no refs found" empty-state
    /// hint in the popup.</summary>
    public bool HasAnyVisibleRefs =>
        VisibleRecentRefs.Count > 0
        || VisibleLocalBranches.Count > 0
        || VisibleRemoteBranches.Count > 0
        || VisibleTags.Count > 0
        || VisibleStashes.Count > 0;

    /// <summary>The user's inputs for the inline merge-base composer.</summary>
    [ObservableProperty]
    private string _mergeBaseRefA = string.Empty;

    [ObservableProperty]
    private string _mergeBaseRefB = string.Empty;

    /// <summary>Last-computed merge-base SHA (informational; the
    /// commit happens through <see cref="UseMergeBaseCommand"/>).</summary>
    [ObservableProperty]
    private string? _computedMergeBase;

    /// <summary>Non-null when the last merge-base attempt failed —
    /// either an unresolvable ref or unrelated histories.</summary>
    [ObservableProperty]
    private string? _mergeBaseError;

    /// <summary>
    /// Enumerate refs for <see cref="CanonicalRepoPath"/> off the UI
    /// thread. Idempotent: a second call after <see cref="IsLoaded"/>
    /// is a no-op. Callers (typically the view's "popup opened"
    /// handler) should fire-and-forget this.
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

            _localBranches = result.LocalBranches;
            _remoteBranches = result.RemoteBranches;
            _tags = result.Tags;
            _stashes = result.Stashes;
            _recentRefs = ComputeRecentRefs(repoPath!);
            IsLoaded = true;
            RaiseFilteredCollectionsChanged();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Write a literal ref string back into the form's
    /// commit-ish field. Closes-on-pick is the popup view's job.</summary>
    [RelayCommand]
    private void PickRef(string? refName)
    {
        if (string.IsNullOrWhiteSpace(refName)) return;
        _writeBack(refName);
    }

    /// <summary>Resolve the merge-base of
    /// <see cref="MergeBaseRefA"/> and <see cref="MergeBaseRefB"/>
    /// and, on success, write the resulting SHA back. On failure
    /// (unresolvable ref or unrelated histories) sets
    /// <see cref="MergeBaseError"/>.</summary>
    [RelayCommand]
    private void UseMergeBase()
    {
        MergeBaseError = null;
        ComputedMergeBase = null;

        if (string.IsNullOrWhiteSpace(CanonicalRepoPath))
        {
            MergeBaseError = "Pick a valid repository path first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(MergeBaseRefA) || string.IsNullOrWhiteSpace(MergeBaseRefB))
        {
            MergeBaseError = "Fill both refs to compute their merge-base.";
            return;
        }

        var mergeBase = _enumerator.TryComputeMergeBase(
            CanonicalRepoPath, MergeBaseRefA, MergeBaseRefB);
        if (mergeBase is null)
        {
            MergeBaseError = $"No common ancestor between `{MergeBaseRefA}` and `{MergeBaseRefB}` " +
                "(or one of them doesn't resolve in this repo).";
            return;
        }

        ComputedMergeBase = mergeBase;
        _writeBack(mergeBase);
    }

    private bool MatchesFilter(string candidate)
    {
        if (string.IsNullOrEmpty(Filter)) return true;
        return candidate.IndexOf(Filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private IReadOnlyList<string> ComputeRecentRefs(string repoPath)
    {
        // Walk MRU-ordered recents (already MRU per
        // RecentContextsService.SortAndCap), pluck CommitIsh refs from
        // both sides of every entry whose canonical repo matches,
        // dedup case-sensitively (Git: HEAD ≠ head), cap.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var recent in _recents.Current)
        {
            if (!ContextIdentityFactory.RepoPathsEqual(recent.Identity.CanonicalRepoPath, repoPath))
            {
                continue;
            }

            foreach (var side in new[] { recent.LeftDisplay, recent.RightDisplay })
            {
                if (side is DiffSide.CommitIsh ci
                    && !string.IsNullOrWhiteSpace(ci.Reference)
                    && seen.Add(ci.Reference))
                {
                    result.Add(ci.Reference);
                    if (result.Count >= MaxRecentRefs) return result;
                }
            }
        }
        return result;
    }

    private void RaiseFilteredCollectionsChanged()
    {
        OnPropertyChanged(nameof(VisibleRecentRefs));
        OnPropertyChanged(nameof(VisibleLocalBranches));
        OnPropertyChanged(nameof(VisibleRemoteBranches));
        OnPropertyChanged(nameof(VisibleTags));
        OnPropertyChanged(nameof(VisibleStashes));
        OnPropertyChanged(nameof(HasAnyVisibleRefs));
    }
}
