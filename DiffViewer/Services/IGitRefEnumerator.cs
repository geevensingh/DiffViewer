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
}

/// <summary>
/// One ref returned by <see cref="IGitRefEnumerator.Enumerate"/>. The
/// short SHA is included so the picker UI can show
/// <c>"feature/x &nbsp;a1b2c3d"</c> without a second libgit2 round-trip.
/// </summary>
public sealed record RefEntry(string FriendlyName, string TipSha, string TipShortSha);

/// <summary>
/// Result of <see cref="IGitRefEnumerator.Enumerate"/>. Empty lists
/// (never <c>null</c>) when the repo couldn't be opened. Lists are
/// pre-sorted alphabetically by <see cref="RefEntry.FriendlyName"/>
/// using the ordinal comparer for deterministic UI ordering.
/// </summary>
public sealed record RefEnumerationResult(
    IReadOnlyList<RefEntry> LocalBranches,
    IReadOnlyList<RefEntry> RemoteBranches,
    IReadOnlyList<RefEntry> Tags)
{
    /// <summary>Singleton empty result returned on any failure.</summary>
    public static RefEnumerationResult Empty { get; } = new(
        System.Array.Empty<RefEntry>(),
        System.Array.Empty<RefEntry>(),
        System.Array.Empty<RefEntry>());
}
