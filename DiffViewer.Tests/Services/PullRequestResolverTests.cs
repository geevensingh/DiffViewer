using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public sealed class PullRequestResolverTests
{
    private static readonly PullRequestRef SamplePr =
        new("github.com", "owner", "repo", 42);

    private static PullRequestInfo SampleInfo() => new(
        Number: 42,
        Title: "Add feature X",
        State: "open",
        Merged: false,
        BaseRef: "main",
        BaseSha: "0000000000000000000000000000000000000001",
        HeadRef: "feature-x",
        HeadSha: "0000000000000000000000000000000000000002",
        HeadRepoCloneUrl: "https://github.com/contributor/repo.git",
        BaseRepoCloneUrl: "https://github.com/owner/repo.git");

    private sealed class FakeLocalRepoLocator : ILocalRepoLocator
    {
        public LocalRepoLookup Lookup { get; set; } =
            new(null, LocalRepoMatchSource.NotFound);

        public int CallCount { get; private set; }
        public (string Host, string Owner, string Repo)? LastCall { get; private set; }

        public LocalRepoLookup TryLocate(string host, string owner, string repo)
        {
            CallCount++;
            LastCall = (host, owner, repo);
            return Lookup;
        }
    }

    private sealed class FakeMetadataResolver : IPullRequestMetadataResolver
    {
        public Func<PullRequestRef, CancellationToken, Task<PullRequestInfo>> Behavior { get; set; } =
            (_, _) => Task.FromResult(SampleInfo());

        public int CallCount { get; private set; }

        public Task<PullRequestInfo> ResolveAsync(PullRequestRef pr, CancellationToken ct)
        {
            CallCount++;
            return Behavior(pr, ct);
        }
    }

    private sealed class FakeLocalFetcher : IPullRequestLocalFetcher
    {
        public Func<string, PullRequestInfo, CancellationToken, Task<PullRequestFetchResult>> Behavior { get; set; } =
            (_, _, _) => Task.FromResult(new PullRequestFetchResult(
                MergeBaseSha: "000000000000000000000000000000000000000a",
                HeadSha: "000000000000000000000000000000000000000b"));

        public int CallCount { get; private set; }
        public string? LastRepoPath { get; private set; }
        public PullRequestInfo? LastInfo { get; private set; }

        public Task<PullRequestFetchResult> FetchAsync(
            string repoPath, PullRequestInfo info, CancellationToken ct)
        {
            CallCount++;
            LastRepoPath = repoPath;
            LastInfo = info;
            return Behavior(repoPath, info, ct);
        }
    }

    [Fact]
    public async Task ResolveAsync_NoLocalClone_ReturnsMissingClone()
    {
        var locator = new FakeLocalRepoLocator
        {
            Lookup = new LocalRepoLookup(null, LocalRepoMatchSource.NotFound),
        };
        var metadata = new FakeMetadataResolver();
        var fetcher = new FakeLocalFetcher();
        var resolver = new PullRequestResolver(locator, metadata, fetcher);

        var result = await resolver.ResolveAsync(SamplePr, CancellationToken.None);

        result.Should().BeOfType<PullRequestResolution.MissingClone>()
            .Which.Pr.Should().Be(SamplePr);
        metadata.CallCount.Should().Be(0, "the resolver must not hit the API before a clone is located");
        fetcher.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_HappyPath_ReturnsReadyWithCommitIshSides()
    {
        var locator = new FakeLocalRepoLocator
        {
            Lookup = new LocalRepoLookup(@"C:\repos\owner\repo", LocalRepoMatchSource.RepoRootScan),
        };
        var metadata = new FakeMetadataResolver();
        var fetcher = new FakeLocalFetcher
        {
            Behavior = (_, _, _) => Task.FromResult(new PullRequestFetchResult(
                MergeBaseSha: "merge-base-sha-aaaaaaaaaaaaaaaaaaaaaaaaaa",
                HeadSha: "head-sha-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")),
        };
        var resolver = new PullRequestResolver(locator, metadata, fetcher);

        var result = await resolver.ResolveAsync(SamplePr, CancellationToken.None);

        var ready = result.Should().BeOfType<PullRequestResolution.Ready>().Subject;
        ready.Pr.Should().Be(SamplePr);
        ready.Parsed.RepoPath.Should().Be(@"C:\repos\owner\repo");
        ready.Parsed.Left.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("merge-base-sha-aaaaaaaaaaaaaaaaaaaaaaaaaa");
        ready.Parsed.Right.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("head-sha-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        fetcher.LastRepoPath.Should().Be(@"C:\repos\owner\repo");
        fetcher.LastInfo.Should().NotBeNull();
        fetcher.LastInfo!.Number.Should().Be(42);
    }

    [Fact]
    public async Task ResolveAsync_MetadataThrowsGitHubException_ReturnsFailed()
    {
        var locator = new FakeLocalRepoLocator
        {
            Lookup = new LocalRepoLookup(@"C:\repos\owner\repo", LocalRepoMatchSource.RepoRootScan),
        };
        var metadata = new FakeMetadataResolver
        {
            Behavior = (_, _) => throw new GitHubException("PR not found or visible to this token"),
        };
        var fetcher = new FakeLocalFetcher();
        var resolver = new PullRequestResolver(locator, metadata, fetcher);

        var result = await resolver.ResolveAsync(SamplePr, CancellationToken.None);

        var failed = result.Should().BeOfType<PullRequestResolution.Failed>().Subject;
        failed.Pr.Should().Be(SamplePr);
        failed.Message.Should().Contain("PR not found");
        fetcher.CallCount.Should().Be(0, "fetcher must not be invoked after metadata failure");
    }

    [Fact]
    public async Task ResolveAsync_FetcherThrowsPullRequestFetchException_ReturnsFailed()
    {
        var locator = new FakeLocalRepoLocator
        {
            Lookup = new LocalRepoLookup(@"C:\repos\owner\repo", LocalRepoMatchSource.RepoRootScan),
        };
        var metadata = new FakeMetadataResolver();
        var fetcher = new FakeLocalFetcher
        {
            Behavior = (_, _, _) =>
                throw new PullRequestFetchException(
                    "refs/pull/42/head was not advertised by the upstream"),
        };
        var resolver = new PullRequestResolver(locator, metadata, fetcher);

        var result = await resolver.ResolveAsync(SamplePr, CancellationToken.None);

        var failed = result.Should().BeOfType<PullRequestResolution.Failed>().Subject;
        failed.Pr.Should().Be(SamplePr);
        failed.Message.Should().Contain("refs/pull/42/head");
    }

    [Fact]
    public async Task ResolveAsync_CancellationDuringMetadata_PropagatesOperationCanceled()
    {
        var locator = new FakeLocalRepoLocator
        {
            Lookup = new LocalRepoLookup(@"C:\repos\owner\repo", LocalRepoMatchSource.RepoRootScan),
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var metadata = new FakeMetadataResolver
        {
            Behavior = (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(SampleInfo());
            },
        };
        var fetcher = new FakeLocalFetcher();
        var resolver = new PullRequestResolver(locator, metadata, fetcher);

        var act = () => resolver.ResolveAsync(SamplePr, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fetcher.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_CancellationDuringFetch_PropagatesOperationCanceled()
    {
        var locator = new FakeLocalRepoLocator
        {
            Lookup = new LocalRepoLookup(@"C:\repos\owner\repo", LocalRepoMatchSource.RepoRootScan),
        };
        using var cts = new CancellationTokenSource();
        var metadata = new FakeMetadataResolver();
        var fetcher = new FakeLocalFetcher
        {
            Behavior = (_, _, ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new PullRequestFetchResult("aaa", "bbb"));
            },
        };
        var resolver = new PullRequestResolver(locator, metadata, fetcher);

        var act = () => resolver.ResolveAsync(SamplePr, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ResolveAsync_PassesHostOwnerRepoLowercaseToLocator()
    {
        var locator = new FakeLocalRepoLocator
        {
            Lookup = new LocalRepoLookup(null, LocalRepoMatchSource.NotFound),
        };
        var resolver = new PullRequestResolver(
            locator, new FakeMetadataResolver(), new FakeLocalFetcher());

        await resolver.ResolveAsync(
            new PullRequestRef("github.com", "owner", "repo", 7),
            CancellationToken.None);

        locator.LastCall.Should().Be(("github.com", "owner", "repo"));
    }

    [Fact]
    public async Task ResolveAsync_NullArgument_Throws()
    {
        var resolver = new PullRequestResolver(
            new FakeLocalRepoLocator(),
            new FakeMetadataResolver(),
            new FakeLocalFetcher());

        var act = () => resolver.ResolveAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
