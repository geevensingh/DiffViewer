using System;
using System.Collections.Generic;
using System.Linq;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// On-disk shape for <c>recents.json</c>. <see cref="Version"/> is bumped
/// whenever the JSON shape changes in a non-backward-compatible way; the
/// deserializer's policy is "preserve known rows, drop unknown ones" so a
/// downgraded binary reading a newer file can still load every row whose
/// shape it recognizes (per Phase 7's rollout/downgrade safety design),
/// and a newer binary reading an older file fills the new fields with
/// safe defaults (<c>null</c> for <see cref="RecentLaunchContext.PullRequest"/>).
/// </summary>
public sealed record RecentsDoc(int Version, IReadOnlyList<RecentLaunchContext> Items)
{
    /// <summary>
    /// Schema version. Bumped to 2 in Phase 7 of the PR-review feature
    /// when <see cref="RecentLaunchContext.PullRequest"/> was added as
    /// a sibling field on each row.
    /// </summary>
    public const int CurrentVersion = 2;

    public static RecentsDoc Empty { get; } = new(CurrentVersion, Array.Empty<RecentLaunchContext>());

    /// <summary>
    /// Defensive copy + integrity check for callers that build a new
    /// <see cref="RecentsDoc"/> from a mutation. Rejects null entries and
    /// snapshots the list so later in-place mutation by the caller can't
    /// corrupt the on-disk state.
    /// </summary>
    public static RecentsDoc From(IEnumerable<RecentLaunchContext> items) =>
        new(CurrentVersion, items.Where(i => i is not null).ToArray());
}
