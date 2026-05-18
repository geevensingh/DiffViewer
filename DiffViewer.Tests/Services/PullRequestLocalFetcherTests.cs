using System.IO;
using DiffViewer.Services;
using FluentAssertions;
using LibGit2Sharp;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// On-disk integration tests for <see cref="PullRequestLocalFetcher"/>.
/// Sets up a bare "upstream" repo with <c>refs/pull/N/head</c> published,
/// and a "local clone" against which we run the fetcher. No GitHub access.
/// </summary>
public sealed class PullRequestLocalFetcherTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly Signature _author = new("Test", "t@example.com", DateTimeOffset.UtcNow);

    public PullRequestLocalFetcherTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(),
            "diffviewer-prfetcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                foreach (var f in Directory.EnumerateFiles(_tempRoot, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Sets up an "upstream" bare repo with:
    ///   - a default branch at commit B
    ///   - a feature commit F branching from B
    ///   - <c>refs/pull/N/head</c> published at F
    /// and a "clone" of the upstream (containing only the default branch
    /// at B initially). Returns the paths, branch name, and SHAs.
    /// </summary>
    private Fixture CreateFixture(int prNumber, bool publishPrHead = true,
        bool extraCommitAfterFork = false)
    {
        var upstreamPath = Path.Combine(_tempRoot, "upstream-" + Guid.NewGuid().ToString("N"));
        var workPath = Path.Combine(_tempRoot, "work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workPath);
        Repository.Init(workPath);

        string baseSha, headSha, defaultBranchName;
        using (var repo = new Repository(workPath))
        {
            repo.Config.Set("core.autocrlf", "false");
            File.WriteAllText(Path.Combine(workPath, "README.md"), "v1");
            Commands.Stage(repo, "README.md");
            repo.Commit("initial", _author, _author);

            File.WriteAllText(Path.Combine(workPath, "README.md"), "v2");
            Commands.Stage(repo, "README.md");
            var cBase = repo.Commit("base", _author, _author);
            baseSha = cBase.Sha;
            defaultBranchName = repo.Head.FriendlyName;

            var featureBranch = repo.CreateBranch("feature", cBase);
            Commands.Checkout(repo, featureBranch);
            File.WriteAllText(Path.Combine(workPath, "feature.txt"), "feature work");
            Commands.Stage(repo, "feature.txt");
            var cFeature = repo.Commit("feature work", _author, _author);
            headSha = cFeature.Sha;

            Commands.Checkout(repo, repo.Branches[defaultBranchName]);
            if (extraCommitAfterFork)
            {
                File.WriteAllText(Path.Combine(workPath, "main-extra.txt"), "main moves on");
                Commands.Stage(repo, "main-extra.txt");
                repo.Commit("main extra", _author, _author);
            }
        }

        Repository.Init(upstreamPath, isBare: true);
        using (var repo = new Repository(workPath))
        {
            var remoteName = "upstream-test";
            repo.Network.Remotes.Add(remoteName, upstreamPath);
            var refspecs = new List<string>
            {
                $"+refs/heads/{defaultBranchName}:refs/heads/{defaultBranchName}",
                "+refs/heads/feature:refs/heads/feature",
            };
            if (publishPrHead)
            {
                refspecs.Add($"+refs/heads/feature:refs/pull/{prNumber}/head");
            }
            repo.Network.Push(repo.Network.Remotes[remoteName], refspecs, new PushOptions());
        }

        var clonePath = Path.Combine(_tempRoot, "clone-" + Guid.NewGuid().ToString("N"));
        Repository.Clone(upstreamPath, clonePath);

        return new Fixture(upstreamPath, clonePath, baseSha, headSha, defaultBranchName);
    }

    private sealed record Fixture(
        string UpstreamPath,
        string ClonePath,
        string BaseSha,
        string HeadSha,
        string DefaultBranchName);

    private PullRequestInfo MakeInfo(Fixture fx, int prNumber, string? baseShaOverride = null,
        string? headShaOverride = null)
    {
        // LibGit2Sharp's anonymous-remote fetch path expects a URL, not a
        // raw Windows path. file:// URIs are accepted and exercise the
        // production code path without needing a real network endpoint.
        var url = new Uri(fx.UpstreamPath).AbsoluteUri;
        return new PullRequestInfo(
            Number: prNumber,
            Title: "Test PR",
            State: "open",
            Merged: false,
            BaseRef: fx.DefaultBranchName,
            BaseSha: baseShaOverride ?? fx.BaseSha,
            HeadRef: "feature",
            HeadSha: headShaOverride ?? fx.HeadSha,
            HeadRepoCloneUrl: url,
            BaseRepoCloneUrl: url);
    }

    [Fact]
    public async Task FetchAsync_HappyPath_ReturnsExpectedMergeBaseAndHead()
    {
        var prNumber = 42;
        using var fx = new FixtureHandle(CreateFixture(prNumber));
        var info = MakeInfo(fx.Inner, prNumber);
        var fetcher = new PullRequestLocalFetcher();

        var result = await fetcher.FetchAsync(fx.Inner.ClonePath, info, CancellationToken.None);

        result.HeadSha.Should().Be(fx.Inner.HeadSha);
        // Merge base of (main tip == base, feature == head) is the base
        // commit itself, since feature branches directly off main with no
        // intervening commits in this fixture.
        result.MergeBaseSha.Should().Be(fx.Inner.BaseSha);

        // The fetcher must have written the PR head into the namespaced ref.
        using var clone = new Repository(fx.Inner.ClonePath);
        clone.Refs[$"refs/diffviewer/pr/{prNumber}/head"].Should().NotBeNull();
    }

    [Fact]
    public async Task FetchAsync_HeadShaInfoStale_RereadsLocalRef()
    {
        var prNumber = 42;
        using var fx = new FixtureHandle(CreateFixture(prNumber));
        // Pass an obviously-bogus advisory head SHA — the fetcher must
        // ignore it and re-read the real value from the local ref.
        var info = MakeInfo(fx.Inner, prNumber,
            headShaOverride: "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef");
        var fetcher = new PullRequestLocalFetcher();

        var result = await fetcher.FetchAsync(fx.Inner.ClonePath, info, CancellationToken.None);

        result.HeadSha.Should().Be(fx.Inner.HeadSha);
    }

    [Fact]
    public async Task FetchAsync_PullRefNotPublishedUpstream_ThrowsPullRequestFetchException()
    {
        var prNumber = 99;
        using var fx = new FixtureHandle(CreateFixture(prNumber, publishPrHead: false));
        var info = MakeInfo(fx.Inner, prNumber);
        var fetcher = new PullRequestLocalFetcher();

        var act = () => fetcher.FetchAsync(fx.Inner.ClonePath, info, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<PullRequestFetchException>();
        ex.Which.Message.Should().Contain($"refs/pull/{prNumber}/head");
    }

    [Fact]
    public async Task FetchAsync_Cancelled_ThrowsOperationCanceled()
    {
        var prNumber = 7;
        using var fx = new FixtureHandle(CreateFixture(prNumber));
        var info = MakeInfo(fx.Inner, prNumber);
        var fetcher = new PullRequestLocalFetcher();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => fetcher.FetchAsync(fx.Inner.ClonePath, info, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FetchAsync_NullRepoPath_Throws()
    {
        var fetcher = new PullRequestLocalFetcher();
        var info = new PullRequestInfo(
            1, "t", "open", false, "main", "b", "feat", "h",
            "https://example.com/r.git", "https://example.com/r.git");

        var act = () => fetcher.FetchAsync(null!, info, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task FetchAsync_NullInfo_Throws()
    {
        var fetcher = new PullRequestLocalFetcher();

        var act = () => fetcher.FetchAsync("path", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// Wrap a <see cref="Fixture"/> in a disposable that aggressively
    /// clears read-only bits before letting xUnit clean up the test root —
    /// LibGit2Sharp leaves index files locked on Windows and breaks naive
    /// recursive deletes.
    /// </summary>
    private sealed class FixtureHandle : IDisposable
    {
        public Fixture Inner { get; }
        public FixtureHandle(Fixture fx) { Inner = fx; }

        public void Dispose()
        {
            foreach (var path in new[] { Inner.UpstreamPath, Inner.ClonePath })
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                        {
                            try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                        }
                    }
                }
                catch { /* best-effort */ }
            }
        }
    }
}
