using System.ComponentModel;
using System.IO;
using LibGit2Sharp;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="IPullRequestLocalFetcher"/>. Uses <c>git.exe</c>
/// (via <see cref="IProcessRunner"/>) for network fetches and LibGit2Sharp
/// for local operations (ref resolution, commit lookup, merge-base).
/// Operates on the local clone path that the orchestrator resolved via
/// <see cref="ILocalRepoLocator"/>.
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
/// <para>Network fetches use <c>git -C &lt;repoPath&gt; fetch &lt;url&gt;
/// &lt;refspec&gt;</c> instead of LibGit2Sharp's in-process fetch, because
/// LibGit2Sharp 0.31.0's bundled TLS stack (OpenSSL) fails to negotiate
/// with some endpoints. System <c>git.exe</c> uses the OS-native TLS
/// stack (Schannel on Windows) and is more robust.</para>
///
/// <para>Refs in <c>refs/diffviewer/pr/N/head</c> and
/// <c>refs/diffviewer/base/&lt;branch&gt;</c> accumulate over time. v1
/// leaves them in place; a "clean up DiffViewer refs" follow-up is
/// captured in the plan.</para>
/// </remarks>
internal sealed class PullRequestLocalFetcher : IPullRequestLocalFetcher
{
    private readonly IProcessRunner _processRunner;

    public PullRequestLocalFetcher(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<PullRequestFetchResult> FetchAsync(
        string repoPath,
        PullRequestInfo info,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        ArgumentNullException.ThrowIfNull(info);

        var prNumber = info.Number;
        var headRefName = $"refs/diffviewer/pr/{prNumber}/head";
        var prHeadRefspec = $"+refs/pull/{prNumber}/head:{headRefName}";

        await GitFetchAsync(repoPath, info.BaseRepoCloneUrl, prHeadRefspec,
            $"refs/pull/{prNumber}/head", ct).ConfigureAwait(false);

        // Ensure the base commit is present locally before reading
        // refs + computing merge-base.
        await EnsureBaseCommitAsync(repoPath, info, ct).ConfigureAwait(false);

        // LibGit2Sharp local operations run on a background thread to
        // keep the UI thread free (they hold the ODB lock and can block).
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return ReadLocalState(repoPath, info, headRefName);
        }, ct).ConfigureAwait(false);
    }

    private static PullRequestFetchResult ReadLocalState(
        string repoPath,
        PullRequestInfo info,
        string headRefName)
    {
        using var repo = new Repository(repoPath);

        var prHeadRef = repo.Refs[headRefName];
        var headSha = prHeadRef?.ResolveToDirectReference()?.TargetIdentifier
            ?? throw new PullRequestFetchException(
                $"refs/pull/{info.Number}/head was not advertised by " +
                $"{info.BaseRepoCloneUrl}. The PR may have been deleted or its head " +
                "pruned. Try opening it on GitHub.");

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

    /// <summary>
    /// Ensures the base commit is present in the local clone, fetching it
    /// from upstream if necessary. Tries the base branch first; falls back
    /// to fetching the SHA directly.
    /// </summary>
    internal async Task EnsureBaseCommitAsync(
        string repoPath,
        PullRequestInfo info,
        CancellationToken ct)
    {
        bool basePresent;
        using (var repo = new Repository(repoPath))
        {
            basePresent = repo.Lookup<Commit>(info.BaseSha) is not null;
        }

        if (basePresent)
        {
            return;
        }

        // Try the branch refspec first (cheap, works in the common case).
        var branchRefspec = $"+refs/heads/{info.BaseRef}:refs/diffviewer/base/{info.BaseRef}";
        try
        {
            await GitFetchAsync(repoPath, info.BaseRepoCloneUrl, branchRefspec,
                $"refs/heads/{info.BaseRef}", ct).ConfigureAwait(false);

            using var repo = new Repository(repoPath);
            if (repo.Lookup<Commit>(info.BaseSha) is not null)
            {
                return;
            }
        }
        catch (PullRequestFetchException)
        {
            // Branch renamed or deleted upstream — fall through to the
            // by-SHA fetch.
        }

        // Last resort: fetch the SHA directly.
        try
        {
            await GitFetchAsync(repoPath, info.BaseRepoCloneUrl, $"+{info.BaseSha}",
                $"base commit {info.BaseSha}", ct).ConfigureAwait(false);
        }
        catch (PullRequestFetchException ex)
        {
            throw new PullRequestFetchException(
                $"Base commit {info.BaseSha} is not in the local clone and the " +
                $"upstream wouldn't let us fetch it (branch {info.BaseRef} may have " +
                $"been renamed or deleted): {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Runs <c>git -C &lt;repoPath&gt; fetch &lt;url&gt; &lt;refspec&gt;</c>.
    /// Maps non-zero exit codes and missing-executable errors to
    /// <see cref="PullRequestFetchException"/>.
    /// </summary>
    /// <param name="repoPath">Path to the local clone.</param>
    /// <param name="url">Remote URL to fetch from.</param>
    /// <param name="refspec">Refspec (e.g. <c>+refs/pull/N/head:refs/diffviewer/pr/N/head</c>).</param>
    /// <param name="humanLabel">Human-readable label for the ref being
    /// fetched, used in error messages (e.g. <c>"refs/pull/42/head"</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task GitFetchAsync(
        string repoPath,
        string url,
        string refspec,
        string humanLabel,
        CancellationToken ct)
    {
        ProcessRunResult result;
        try
        {
            result = await _processRunner.RunAsync(
                "git",
                ["-C", repoPath, "fetch", url, refspec],
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            throw new PullRequestFetchException(
                "git is not installed or not on PATH. DiffViewer needs git " +
                "to fetch pull request refs. Install Git for Windows from " +
                "https://git-scm.com and ensure git.exe is on your PATH.", ex);
        }

        if (result.ExitCode != 0)
        {
            var stderr = result.Stderr.Trim();
            throw new PullRequestFetchException(
                $"Failed to fetch {humanLabel} from {url}: " +
                (string.IsNullOrEmpty(stderr) ? $"git exited with code {result.ExitCode}" : stderr));
        }
    }
}