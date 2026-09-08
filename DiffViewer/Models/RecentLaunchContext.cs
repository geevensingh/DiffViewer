using System;

namespace DiffViewer.Models;

/// <summary>
/// One row in the recent-launch-contexts MRU. Combines the canonical
/// dedup <see cref="Identity"/> with the user's raw input
/// (<see cref="LeftDisplay"/> / <see cref="RightDisplay"/>) so the UI can
/// render exactly what was typed even though we dedup against the
/// canonical form. When the row was created by a review-mode launch
/// (e.g. a GitHub PR URL), the originating <see cref="Review"/>
/// reference is preserved so the dropdown can render
/// <c>"PR owner/repo#N"</c> instead of two SHAs and so re-launching
/// the row re-resolves the review (heads can move between launches —
/// see D8).
///
/// <para><b>Equality</b> is record-equality (all members), but the
/// recents service dedups <em>by Identity (plus
/// (<see cref="IReviewRef.ProviderId"/>, <see cref="IReviewRef.IdentityNumber"/>)
/// when present) only</em>: re-launching with a differently-cased path
/// or different ref alias bumps the existing entry rather than
/// creating a new one, while two reviews that happen to share
/// <c>(merge-base, head)</c> SHAs stay distinct rows. The provider
/// tag is part of the dedup tuple so a GitHub PR #42 and an ADO PR
/// #42 in the same repo remain separate rows. See
/// <c>RecentContextsService</c> for that policy.</para>
/// </summary>
/// <param name="RepositoryName">Name of the repository this row's path
/// belongs to (e.g. <c>DiffViewer</c>), captured at launch time.
/// <c>null</c> for rows written before worktree labelling existed, or
/// when the path could not be inspected; the UI falls back to the
/// path's leaf name.</param>
/// <param name="WorktreeName">Git's name for the linked worktree this
/// row points at, or <c>null</c> when the path is a repository's main
/// worktree. Two worktrees of one repository are separate rows with
/// identical ref labels, so this is what makes them tellable
/// apart.</param>
public sealed record RecentLaunchContext(
    ContextIdentity Identity,
    DiffSide LeftDisplay,
    DiffSide RightDisplay,
    DateTimeOffset LastUsedUtc,
    IReviewRef? Review = null,
    string? RepositoryName = null,
    string? WorktreeName = null);

