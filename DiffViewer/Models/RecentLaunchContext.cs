using System;

namespace DiffViewer.Models;

/// <summary>
/// One row in the recent-launch-contexts MRU. Combines the canonical
/// dedup <see cref="Identity"/> with the user's raw input
/// (<see cref="LeftDisplay"/> / <see cref="RightDisplay"/>) so the UI can
/// render exactly what was typed even though we dedup against the
/// canonical form. When the row was created by a PR-URL launch, the
/// originating <see cref="PullRequest"/> reference is preserved so the
/// dropdown can render "PR owner/repo#N" instead of two SHAs and so
/// re-launching the row re-resolves the PR (heads can move between
/// launches — see D8).
///
/// <para><b>Equality</b> is record-equality (all five members), but the
/// recents service dedups <em>by Identity (plus PR number when present)
/// only</em>: re-launching with a differently-cased path or different
/// ref alias bumps the existing entry rather than creating a new one,
/// while two PRs that happen to share <c>(merge-base, head)</c> SHAs
/// stay distinct rows. See <c>RecentContextsService</c> for that policy.</para>
/// </summary>
public sealed record RecentLaunchContext(
    ContextIdentity Identity,
    DiffSide LeftDisplay,
    DiffSide RightDisplay,
    DateTimeOffset LastUsedUtc,
    PullRequestRef? PullRequest = null);
