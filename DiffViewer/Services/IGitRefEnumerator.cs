using System.Collections.Generic;

namespace DiffViewer.Services;

/// <summary>
/// Stateless, repo-path-keyed read-only enumeration over a Git
/// repository's refs and merge-base resolution. Used by the
/// <see cref="DiffViewer.ViewModels.RefPickerViewModel"/> in the "New
/// diff" dialog so the user can pick a commit-ish from a list of
/// branches / tags / recent refs instead of typing it freeform.
///
/// <para><b>Why a sibling to <see cref="IRepoInspector"/> rather than
/// extending it</b>: <see cref="IRepoInspector"/> is the
/// cheapest-possible probe used by <see cref="LocalRepoLocator"/>
/// against every immediate child of every configured repo root.
/// Bolting branch enumeration on it would muddy the single-method-pair
/// clarity that keeps that scan cheap.</para>
///
/// <para><b>Why not an addition to <see cref="IRepositoryService"/></b>:
/// that service is per-context (lifetime = open
/// <see cref="DiffViewer.ViewModels.MainViewModel"/>). The picker is
/// invoked from the "New diff" dialog <em>before</em> a context is
/// opened, against a repo path the user just typed. A stateless
/// open-read-close service is the right shape.</para>
///
/// <para><b>Error policy</b>: every method is defensive — invalid
/// paths, non-repos, or libgit2 errors return an empty
/// <see cref="RefEnumerationResult"/> or <c>null</c> merge-base, never
/// a throw. The picker UI surfaces "no refs found" as a neutral hint
/// rather than an error.</para>
/// </summary>
public interface IGitRefEnumerator
{
    /// <summary>
    /// Enumerate local branches, remote-tracking branches, and tags
    /// in the repository at <paramref name="canonicalRepoPath"/>.
    /// Returns empty lists (never <c>null</c>) when the path doesn't
    /// resolve or libgit2 throws.
    /// </summary>
    RefEnumerationResult Enumerate(string canonicalRepoPath);

    /// <summary>
    /// Resolve the merge-base SHA of two commit-ish refs in the
    /// repository at <paramref name="canonicalRepoPath"/>. Returns
    /// <c>null</c> when either ref doesn't resolve, the histories
    /// don't share a common ancestor, or libgit2 throws.
    /// </summary>
    string? TryComputeMergeBase(string canonicalRepoPath, string refA, string refB);

    /// <summary>
    /// Look up the default branch of the <c>origin</c> remote in the
    /// repository at <paramref name="canonicalRepoPath"/> and return
    /// its friendly remote-tracking name (e.g. <c>"origin/main"</c>).
    /// Returns <c>null</c> when the repo doesn't have an
    /// <c>origin/HEAD</c> symref (some clones never set one), when
    /// the symref points at something other than a remote-tracking
    /// branch, when the path doesn't resolve, or when libgit2 throws.
    ///
    /// <para>Used by the "New diff" dialog's branch-vs-merge-base form
    /// to pre-fill the merge-base partner field — the dominant
    /// PR-review case is comparing a feature branch against the
    /// upstream default branch, and forcing the user to remember
    /// whether that's <c>origin/main</c> or <c>origin/master</c> is
    /// friction we can erase whenever the clone already knows.</para>
    /// </summary>
    string? TryGetDefaultRemoteBranch(string canonicalRepoPath);
}

/// <summary>
/// One ref returned by <see cref="IGitRefEnumerator.Enumerate"/>. The
/// short SHA is included so the picker UI can show
/// <c>"feature/x &nbsp;a1b2c3d"</c> without a second libgit2 round-trip.
/// </summary>
public sealed record RefEntry(string FriendlyName, string TipSha, string TipShortSha);

/// <summary>
/// One stash entry returned by <see cref="IGitRefEnumerator.Enumerate"/>.
/// Carries the positional index, the human-readable symbolic name
/// (<c>stash@{0}</c>), the stash message, creation timestamp, and the
/// tip SHA of the stash's working-tree commit (the commit
/// <c>stash@{N}</c> resolves to).
/// </summary>
/// <param name="Index">Zero-based positional index in the stash reflog
/// (0 = most recent, matching <c>git stash list</c> order).</param>
/// <param name="SymbolicName">The symbolic reflog name, e.g.
/// <c>stash@{0}</c>. This is the string written back into a commit-ish
/// textbox when the user picks a stash.</param>
/// <param name="Subject">The stash message — either the auto-generated
/// <c>"WIP on branch: …"</c> text or a user-supplied
/// <c>git stash push -m "…"</c> message.</param>
/// <param name="CreatedAt">Timestamp of the stash operation, read from
/// the stash commit's author date.</param>
/// <param name="TipSha">Full SHA of the stash's working-tree commit.</param>
/// <param name="TipShortSha">7-char abbreviated SHA for display.</param>
public sealed record StashEntry(
    int Index,
    string SymbolicName,
    string Subject,
    DateTimeOffset CreatedAt,
    string TipSha,
    string TipShortSha);

/// <summary>
/// Result of <see cref="IGitRefEnumerator.Enumerate"/>. Empty lists
/// (never <c>null</c>) when the repo couldn't be opened. Branch and
/// tag lists are pre-sorted alphabetically by
/// <see cref="RefEntry.FriendlyName"/> using the ordinal comparer for
/// deterministic UI ordering. <see cref="Stashes"/> is ordered
/// most-recent-first (index 0 = newest), matching <c>git stash list</c>.
/// </summary>
public sealed record RefEnumerationResult(
    IReadOnlyList<RefEntry> LocalBranches,
    IReadOnlyList<RefEntry> RemoteBranches,
    IReadOnlyList<RefEntry> Tags,
    IReadOnlyList<StashEntry> Stashes)
{
    /// <summary>Singleton empty result returned on any failure.</summary>
    public static RefEnumerationResult Empty { get; } = new(
        System.Array.Empty<RefEntry>(),
        System.Array.Empty<RefEntry>(),
        System.Array.Empty<RefEntry>(),
        System.Array.Empty<StashEntry>());
}
