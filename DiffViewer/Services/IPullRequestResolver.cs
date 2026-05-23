using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Resolves a <see cref="PullRequestRef"/> to GitHub's view of the PR
/// (title, base/head refs, base/head SHAs, base/head clone URLs).
/// HTTP only, no disk. Free-threaded. The v1 implementation forwards
/// straight to <see cref="IGitHubClient.GetPullRequestAsync"/> — this
/// extra seam exists so the orchestrator (<see cref="IPullRequestResolver"/>)
/// can be tested with a fake that doesn't need an HTTP handler stack.
/// </summary>
public interface IPullRequestMetadataResolver
{
    Task<PullRequestInfo> ResolveAsync(PullRequestRef pr, CancellationToken ct);
}

/// <summary>
/// Fetches the PR's head and base into the local clone, then returns the
/// SHAs the diff viewer should compare. Network fetches use
/// <c>git.exe</c> (system TLS); local operations (ref resolution,
/// merge-base) use LibGit2Sharp in-process. Free-threaded; the
/// implementation manages its own background work.
/// </summary>
/// <remarks>
/// The fetcher uses <c>git -C &lt;path&gt; fetch &lt;url&gt;
/// &lt;refspec&gt;</c> so it never mutates the user's remote
/// configuration. It always fetches from
/// <see cref="PullRequestInfo.BaseRepoCloneUrl"/> — for fork PRs,
/// <c>refs/pull/N/head</c> is published on the base/upstream repo, not
/// on the fork.
/// </remarks>
public interface IPullRequestLocalFetcher
{
    Task<PullRequestFetchResult> FetchAsync(
        string repoPath,
        PullRequestInfo info,
        CancellationToken ct);
}

/// <summary>
/// Result of <see cref="IPullRequestLocalFetcher.FetchAsync"/>. SHAs
/// are pinned strings: the merge-base is computed locally after the
/// fetch, and the head is re-read from the local
/// <c>refs/diffviewer/pr/N/head</c> ref instead of trusting the API's
/// advisory <see cref="PullRequestInfo.HeadSha"/> (the PR can be
/// force-pushed between metadata fetch and refs fetch).
/// </summary>
public sealed record PullRequestFetchResult(string MergeBaseSha, string HeadSha);

/// <summary>
/// Thrown by <see cref="IPullRequestLocalFetcher"/> on terminal local-fetch
/// failures (missing refs/pull/N/head upstream, orphan PR with no common
/// ancestor, etc.). The orchestrator catches and folds the message into
/// <see cref="PullRequestResolution.Failed"/>.
/// </summary>
public sealed class PullRequestFetchException : Exception
{
    public PullRequestFetchException(string message) : base(message) { }
    public PullRequestFetchException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// End-to-end orchestrator for "launch DiffViewer with a PR URL". Locates
/// the clone, fetches metadata, fetches refs, and returns a
/// <see cref="PullRequestResolution"/> the coordinator interprets.
/// </summary>
/// <remarks>
/// The resolver intentionally does NOT take an <c>IDialogService</c> /
/// user-prompt seam. Missing clones are returned as
/// <see cref="PullRequestResolution.MissingClone"/> for the coordinator to
/// dispatch to the Phase 5 dialog and then re-invoke the resolver. This
/// keeps the service testable without faking UI prompts.
/// </remarks>
public interface IPullRequestResolver
{
    Task<PullRequestResolution> ResolveAsync(
        PullRequestRef pr,
        IProgress<string>? progress,
        CancellationToken ct);
}

/// <summary>Outcome of <see cref="IPullRequestResolver.ResolveAsync"/>.</summary>
public abstract record PullRequestResolution
{
    /// <summary>
    /// Ready to launch: <see cref="Parsed"/> points at a real local
    /// clone with <c>Left = CommitIsh(mergeBaseSha)</c> and
    /// <c>Right = CommitIsh(headSha)</c>.
    /// </summary>
    public sealed record Ready(
        ParsedCommandLine Parsed,
        PullRequestRef Pr) : PullRequestResolution;

    /// <summary>
    /// The locator couldn't find a local clone. The coordinator should
    /// show the missing-clone dialog and retry the resolver after the
    /// user picks one.
    /// </summary>
    public sealed record MissingClone(PullRequestRef Pr) : PullRequestResolution;

    /// <summary>
    /// Terminal failure — surface <see cref="Message"/> to the user via
    /// the normal cold-launch failure UX.
    /// </summary>
    public sealed record Failed(PullRequestRef Pr, string Message) : PullRequestResolution;
}
