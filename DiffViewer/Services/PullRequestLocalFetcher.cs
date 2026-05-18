using LibGit2Sharp;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="IPullRequestLocalFetcher"/> wrapping LibGit2Sharp's
/// fetch / lookup / merge-base APIs. Operates on the local clone path that
/// the orchestrator resolved via <see cref="ILocalRepoLocator"/>.
/// </summary>
/// <remarks>
/// <para>The fetch shape is:
/// <list type="number">
///   <item>Fetch <c>refs/pull/{N}/head</c> from
///         <see cref="PullRequestInfo.BaseRepoCloneUrl"/> into
///         <c>refs/diffviewer/pr/{N}/head</c>. The <c>+</c> prefix is
///         mandatory because PR heads get force-pushed routinely.</item>
///   <item>Re-read the local ref's tip — treat <c>info.HeadSha</c> as
///         advisory because the PR may have been force-pushed between
///         the metadata fetch and the refs fetch.</item>
///   <item>If <c>info.BaseSha</c> isn't already in the object database,
///         fetch the base branch's <c>refs/heads/{ref}</c>; if that
///         fails (branch renamed or deleted upstream), fall back to
///         fetching the SHA directly.</item>
///   <item>Compute the merge-base locally and return both SHAs.</item>
/// </list>
/// </para>
///
/// <para>Refs in <c>refs/diffviewer/pr/N/head</c> and
/// <c>refs/diffviewer/base/&lt;branch&gt;</c> accumulate over time. v1
/// leaves them in place; a "clean up DiffViewer refs" follow-up is
/// captured in the plan.</para>
/// </remarks>
internal sealed class PullRequestLocalFetcher : IPullRequestLocalFetcher
{
    public Task<PullRequestFetchResult> FetchAsync(
        string repoPath,
        PullRequestInfo info,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        ArgumentNullException.ThrowIfNull(info);

        return Task.Run(() => RunFetch(repoPath, info, ct), ct);
    }

    private static PullRequestFetchResult RunFetch(
        string repoPath,
        PullRequestInfo info,
        CancellationToken ct)
    {
        using var repo = new Repository(repoPath);

        var prNumber = info.Number;
        var headRefName = $"refs/diffviewer/pr/{prNumber}/head";

        var fetchOptions = new FetchOptions
        {
            OnTransferProgress = p => !ct.IsCancellationRequested,
            OnUpdateTips = (refName, oldId, newId) => !ct.IsCancellationRequested,
        };

        var prHeadRefspec = $"+refs/pull/{prNumber}/head:{headRefName}";

        try
        {
            FetchFromUrl(repo, info.BaseRepoCloneUrl, new[] { prHeadRefspec },
                fetchOptions);
        }
        catch (UserCancelledException)
        {
            throw new OperationCanceledException(ct);
        }
        catch (LibGit2SharpException ex)
        {
            throw new PullRequestFetchException(
                $"Failed to fetch refs/pull/{prNumber}/head from " +
                $"{info.BaseRepoCloneUrl}: {ex.Message}", ex);
        }

        ct.ThrowIfCancellationRequested();

        var prHeadRef = repo.Refs[headRefName];
        var headSha = prHeadRef?.ResolveToDirectReference()?.TargetIdentifier
            ?? throw new PullRequestFetchException(
                $"refs/pull/{prNumber}/head was not advertised by " +
                $"{info.BaseRepoCloneUrl}. The PR may have been deleted or its head " +
                "pruned. Try opening it on GitHub.");

        // Ensure the base commit is present locally.
        if (repo.Lookup<Commit>(info.BaseSha) is null)
        {
            FetchBaseCommit(repo, info, fetchOptions, ct);
        }

        var baseCommit = repo.Lookup<Commit>(info.BaseSha)
            ?? throw new PullRequestFetchException(
                $"Base commit {info.BaseSha} not found locally after fetch.");
        var headCommit = repo.Lookup<Commit>(headSha)
            ?? throw new PullRequestFetchException(
                $"Head commit {headSha} not found locally after fetch.");

        var mergeBase = repo.ObjectDatabase.FindMergeBase(baseCommit, headCommit)
            ?? throw new PullRequestFetchException(
                $"No common ancestor between base ({info.BaseSha}) and head " +
                $"({headSha}). The PR may be orphaned.");

        return new PullRequestFetchResult(mergeBase.Sha, headSha);
    }

    private static void FetchBaseCommit(
        Repository repo,
        PullRequestInfo info,
        FetchOptions fetchOptions,
        CancellationToken ct)
    {
        // Try the branch refspec first (cheap, works in the common case).
        var branchRefspec = $"+refs/heads/{info.BaseRef}:refs/diffviewer/base/{info.BaseRef}";
        try
        {
            FetchFromUrl(repo, info.BaseRepoCloneUrl, new[] { branchRefspec },
                fetchOptions);
            if (repo.Lookup<Commit>(info.BaseSha) is not null)
            {
                return;
            }
        }
        catch (UserCancelledException)
        {
            throw new OperationCanceledException(ct);
        }
        catch (LibGit2SharpException)
        {
            // Branch renamed or deleted upstream — fall through to the
            // by-SHA fetch.
        }

        // Last resort: fetch the SHA directly. Server-side support is
        // common but not universal; if it fails, surface a clear error.
        try
        {
            FetchFromUrl(repo, info.BaseRepoCloneUrl, new[] { $"+{info.BaseSha}" },
                fetchOptions);
        }
        catch (UserCancelledException)
        {
            throw new OperationCanceledException(ct);
        }
        catch (LibGit2SharpException ex)
        {
            throw new PullRequestFetchException(
                $"Base commit {info.BaseSha} is not in the local clone and the " +
                $"upstream wouldn't let us fetch it (branch {info.BaseRef} may have " +
                $"been renamed or deleted): {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Anonymous-remote fetch helper. LibGit2Sharp 0.31.0's
    /// <c>Commands.Fetch(repo, url, ...)</c> and <c>repo.Network.Fetch(url, ...)</c>
    /// paths fail for URL forms that libgit2 rejects as a "remote name"
    /// before falling back to anonymous-remote-create (e.g. <c>file://</c>
    /// URLs throw <c>InvalidSpecificationException</c> instead of returning
    /// not-found). The workaround is to add a transient remote with a
    /// unique name, fetch from it, and remove it in a finally block — this
    /// produces no net mutation of the user's remote configuration.
    /// </summary>
    private static void FetchFromUrl(
        Repository repo,
        string url,
        IEnumerable<string> refspecs,
        FetchOptions fetchOptions)
    {
        var remoteName = "diffviewer-transient-" + Guid.NewGuid().ToString("N");
        var remote = repo.Network.Remotes.Add(remoteName, url);
        try
        {
            Commands.Fetch(repo, remote.Name, refspecs, fetchOptions, logMessage: null);
        }
        finally
        {
            try { repo.Network.Remotes.Remove(remoteName); } catch { /* best-effort */ }
        }
    }

}