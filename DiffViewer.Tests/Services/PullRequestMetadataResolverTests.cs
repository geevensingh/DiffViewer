using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public sealed class PullRequestMetadataResolverTests
{
    private sealed class FakeGitHubClient : IGitHubClient
    {
        public Func<PullRequestRef, CancellationToken, Task<PullRequestInfo>> Behavior { get; set; } =
            (_, _) => Task.FromResult(new PullRequestInfo(
                Number: 1,
                Title: "t",
                State: "open",
                Merged: false,
                BaseRef: "main",
                BaseSha: "b",
                HeadRef: "feat",
                HeadSha: "h",
                HeadRepoCloneUrl: "https://github.com/o/r.git",
                BaseRepoCloneUrl: "https://github.com/o/r.git"));

        public int CallCount { get; private set; }
        public PullRequestRef? LastPr { get; private set; }

        public Task<PullRequestInfo> GetPullRequestAsync(PullRequestRef pr, CancellationToken ct)
        {
            CallCount++;
            LastPr = pr;
            return Behavior(pr, ct);
        }
    }

    [Fact]
    public async Task ResolveAsync_DelegatesToGitHubClient()
    {
        var pr = new PullRequestRef("github.com", "owner", "repo", 7);
        var info = new PullRequestInfo(
            Number: 7,
            Title: "Test PR",
            State: "open",
            Merged: false,
            BaseRef: "main",
            BaseSha: "base-sha",
            HeadRef: "branch",
            HeadSha: "head-sha",
            HeadRepoCloneUrl: "https://github.com/owner/repo.git",
            BaseRepoCloneUrl: "https://github.com/owner/repo.git");
        var client = new FakeGitHubClient
        {
            Behavior = (_, _) => Task.FromResult(info),
        };
        var resolver = new PullRequestMetadataResolver(client);

        var result = await resolver.ResolveAsync(pr, CancellationToken.None);

        result.Should().BeSameAs(info);
        client.CallCount.Should().Be(1);
        client.LastPr.Should().Be(pr);
    }

    [Fact]
    public async Task ResolveAsync_GitHubExceptionFromClient_Propagates()
    {
        var pr = new PullRequestRef("github.com", "owner", "repo", 7);
        var client = new FakeGitHubClient
        {
            Behavior = (_, _) => throw new GitHubException("rate limited"),
        };
        var resolver = new PullRequestMetadataResolver(client);

        var act = () => resolver.ResolveAsync(pr, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<GitHubException>();
        ex.Which.Message.Should().Be("rate limited");
    }

    [Fact]
    public async Task ResolveAsync_PassesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var pr = new PullRequestRef("github.com", "owner", "repo", 7);
        var client = new FakeGitHubClient
        {
            Behavior = (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("should have thrown OCE");
            },
        };
        var resolver = new PullRequestMetadataResolver(client);

        var act = () => resolver.ResolveAsync(pr, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Constructor_NullClient_Throws()
    {
        var act = () => new PullRequestMetadataResolver(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
