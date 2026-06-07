namespace DiffViewer.Models;

/// <summary>
/// Captured state of the remote refs that back a pull-request diff:
/// the resolved head SHA (the tip of the PR's head branch) and the
/// merge-base SHA (the common ancestor between head and base, which
/// is what DiffViewer actually pins the left side of the diff to).
///
/// <para>Owned by <c>IPullRequestWatcher</c>: each poll compares the
/// returned <see cref="PullRequestInfo"/> against the current snapshot
/// and re-runs <c>IPullRequestLocalFetcher.FetchAsync</c> only when
/// the head or base SHA on GitHub differs from what we last resolved.
/// The local-fetch step is what produces the new
/// <see cref="MergeBaseSha"/>; the <see cref="HeadSha"/> on the new
/// snapshot is read from the freshly-fetched
/// <c>refs/diffviewer/pr/{N}/head</c> ref, not from the API's
/// (advisory) <c>head.sha</c> field.</para>
///
/// <para>Equality is value-based — two snapshots that match on both
/// SHAs are considered the same.</para>
/// </summary>
public sealed record RemoteRefSnapshot(string HeadSha, string MergeBaseSha);
