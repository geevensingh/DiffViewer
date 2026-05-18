using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// In-memory MRU store of recent launch contexts. App-level singleton —
/// survives in-place context switches. The production implementation
/// (<see cref="RecentContextsService"/>) persists to
/// <c>%APPDATA%\DiffViewer\recents.json</c> via <see cref="RecentsStore"/>
/// using <see cref="System.IO.FileShare.None"/> for cross-process
/// coordination. <see cref="NullRecentContextsService"/> is a no-op
/// double for tests and (historically) the Phase-1 scaffold.
/// </summary>
public interface IRecentContextsService
{
    /// <summary>MRU-ordered snapshot of recent contexts. Empty until <see cref="RecordLaunchAsync"/> is called.</summary>
    IReadOnlyList<RecentLaunchContext> Current { get; }

    /// <summary>Raised after <see cref="Current"/> changes (record / remove).</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Record a successful launch into the MRU. Dedups by
    /// <paramref name="identity"/> (plus PR number when
    /// <paramref name="pullRequest"/> is non-null), bumps the entry's
    /// <see cref="RecentLaunchContext.LastUsedUtc"/> and moves it to the
    /// front, caps total entries at 10. The <paramref name="leftDisplay"/>
    /// / <paramref name="rightDisplay"/> arguments are the user's raw
    /// input and are preserved verbatim for the dropdown render — they
    /// may differ in casing or alias from the identity's sides. The
    /// <paramref name="pullRequest"/> argument, when non-null, marks the
    /// row as a PR-mode entry so the dropdown can render
    /// <c>"PR owner/repo#N"</c> and so re-launching the row re-resolves
    /// the PR (heads can move between launches — see D8).
    /// </summary>
    Task RecordLaunchAsync(
        ContextIdentity identity,
        DiffSide leftDisplay,
        DiffSide rightDisplay,
        PullRequestRef? pullRequest = null,
        CancellationToken ct = default);

    /// <summary>
    /// Remove an entry from the MRU. Used by the failed-switch flow when
    /// a recent's repo no longer resolves.
    /// </summary>
    Task RemoveAsync(
        ContextIdentity identity,
        CancellationToken ct = default);
}
