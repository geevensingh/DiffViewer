using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Reads pull-request metadata from a GitHub host. The v1 surface only
/// covers what the PR resolver (Phase 6) needs: a single PR's base/head
/// refs and SHAs plus the clone URL the fetcher will pull
/// <c>refs/pull/N/head</c> from. Comment posting, review-thread fetching,
/// and any other PR-related read/write APIs are out of scope (plan D1)
/// and will land as additions to this interface or a sibling client.
/// </summary>
/// <remarks>
/// <para>Implementations must be free-threaded and safe to use as an
/// app-singleton: the underlying <c>HttpClient</c> outlives every diff
/// context, and the auth/host plumbing is per-call.</para>
///
/// <para>Implementations must enumerate every row of the error matrix
/// documented in the plan (Phase 4) and surface each as a
/// <see cref="GitHubException"/> with a clear, user-actionable message.
/// Callers route those messages through
/// <c>MainWindowCoordinator.HandleColdLaunchFailure</c>.</para>
/// </remarks>
public interface IGitHubClient
{
    Task<PullRequestInfo> GetPullRequestAsync(PullRequestRef pr, CancellationToken ct);
}

/// <summary>
/// PR metadata projected from GitHub's REST <c>/repos/{owner}/{repo}/pulls/{number}</c>
/// response, narrowed to the fields the resolver actually consumes. The
/// fork-aware <see cref="BaseRepoCloneUrl"/> is what makes the Phase 6
/// fetcher work for fork PRs: <c>refs/pull/N/head</c> is published on the
/// upstream (base) repo, not the fork.
/// </summary>
/// <param name="Title">PR title.</param>
/// <param name="State">GitHub's literal API value: <c>"open"</c> or <c>"closed"</c>.</param>
/// <param name="Merged">
/// True if the PR was merged. GitHub returns this as a separate boolean
/// alongside <see cref="State"/> (a merged PR has <c>state == "closed"</c>
/// and <c>merged == true</c>).
/// </param>
/// <param name="BaseRef">Branch name on the base repo, e.g. <c>"main"</c>.</param>
/// <param name="BaseSha">Tip commit of the base branch at PR creation, advisory.</param>
/// <param name="HeadRef">Branch name on the head repo.</param>
/// <param name="HeadSha">
/// Advisory head SHA — the resolver re-reads the actual tip from
/// <c>refs/pull/N/head</c> after fetching, because the PR may have been
/// force-pushed between this API call and the local fetch.
/// </param>
/// <param name="HeadRepoCloneUrl">
/// Clone URL of the head repo (i.e., the fork in a fork PR, or the same
/// repo in a same-repo PR). Reserved for future use; the v1 fetcher only
/// uses <see cref="BaseRepoCloneUrl"/>.
/// </param>
/// <param name="BaseRepoCloneUrl">
/// Clone URL of the base/upstream repo. The fetcher fetches
/// <c>refs/pull/N/head</c> from this URL using LibGit2Sharp's
/// anonymous-remote form so it doesn't mutate the user's remote config.
/// </param>
public sealed record PullRequestInfo(
    string Title,
    string State,
    bool Merged,
    string BaseRef,
    string BaseSha,
    string HeadRef,
    string HeadSha,
    string HeadRepoCloneUrl,
    string BaseRepoCloneUrl);
