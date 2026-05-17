using DiffViewer.Models;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests;

public class PullRequestRefTests
{
    [Theory]
    [InlineData("https://github.com/owner/repo/pull/1", "github.com", "owner", "repo", 1)]
    [InlineData("https://github.com/geevensingh/jotjson/pull/268", "github.com", "geevensingh", "jotjson", 268)]
    [InlineData("http://github.com/owner/repo/pull/42", "github.com", "owner", "repo", 42)]
    [InlineData("https://github.com/owner/repo/pull/12345", "github.com", "owner", "repo", 12345)]
    public void TryParse_AcceptsCanonicalUrls(string url, string host, string owner, string repo, int number)
    {
        PullRequestRef.TryParse(url, out var pr, out var error).Should().BeTrue();
        error.Should().BeNull();
        pr.Should().Be(new PullRequestRef(host, owner, repo, number));
    }

    [Theory]
    [InlineData("https://github.com/owner/repo/pull/7/files")]
    [InlineData("https://github.com/owner/repo/pull/7/files/abc")]
    [InlineData("https://github.com/owner/repo/pull/7/commits/abc123")]
    [InlineData("https://github.com/owner/repo/pull/7?diff=unified")]
    [InlineData("https://github.com/owner/repo/pull/7#discussion_r123")]
    [InlineData("https://github.com/owner/repo/pull/7/files?diff=unified#issuecomment-1")]
    public void TryParse_AcceptsTrailingSegmentsQueryAndFragment(string url)
    {
        PullRequestRef.TryParse(url, out var pr, out var error).Should().BeTrue();
        error.Should().BeNull();
        pr.Should().Be(new PullRequestRef("github.com", "owner", "repo", 7));
    }

    [Theory]
    [InlineData("https://GITHUB.com/Owner/Repo/pull/7")]
    [InlineData("https://github.com/OWNER/REPO/pull/7")]
    [InlineData("https://github.com/MixedCase/RepoName/pull/7")]
    public void TryParse_LowercasesHostOwnerAndRepo(string url)
    {
        PullRequestRef.TryParse(url, out var pr, out _).Should().BeTrue();
        pr!.Host.Should().Be("github.com");
        pr.Owner.Should().Be(pr.Owner.ToLowerInvariant());
        pr.Repo.Should().Be(pr.Repo.ToLowerInvariant());
    }

    [Fact]
    public void TryParse_RecordEquality_IsCaseInsensitiveAfterNormalization()
    {
        PullRequestRef.TryParse("https://github.com/Owner/Repo/pull/7", out var a, out _);
        PullRequestRef.TryParse("https://GITHUB.com/owner/REPO/pull/7", out var b, out _);
        a.Should().Be(b);
    }

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    public void TryParse_RejectsEmpty(string? url, string expectedFragment)
    {
        PullRequestRef.TryParse(url, out var pr, out var error).Should().BeFalse();
        pr.Should().BeNull();
        error.Should().NotBeNull().And.Contain(expectedFragment);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("owner/repo/pull/7")]
    [InlineData("github.com/owner/repo/pull/7")]
    public void TryParse_RejectsNonAbsoluteUrls(string url)
    {
        PullRequestRef.TryParse(url, out var pr, out var error).Should().BeFalse();
        pr.Should().BeNull();
        error.Should().NotBeNull();
    }

    [Theory]
    [InlineData("ftp://github.com/owner/repo/pull/7")]
    [InlineData("file:///c:/repo/pull/7")]
    public void TryParse_RejectsNonHttpSchemes(string url)
    {
        PullRequestRef.TryParse(url, out var pr, out var error).Should().BeFalse();
        pr.Should().BeNull();
        error.Should().NotBeNull().And.Contain("scheme");
    }

    [Theory]
    [InlineData("https://gitlab.com/owner/repo/pull/7")]
    [InlineData("https://gist.github.com/owner/repo/pull/7")]
    [InlineData("https://ghe.example.com/owner/repo/pull/7")]
    public void TryParse_RejectsNonGitHubHosts(string url)
    {
        PullRequestRef.TryParse(url, out var pr, out var error).Should().BeFalse();
        pr.Should().BeNull();
        error.Should().NotBeNull().And.Contain("github.com");
    }

    [Theory]
    [InlineData("https://github.com/owner/repo/issues/7", "pull")]
    [InlineData("https://github.com/owner/repo/tree/main", "pull")]
    [InlineData("https://github.com/owner/repo/blob/main/README.md", "pull")]
    [InlineData("https://github.com/owner/repo/compare/a...b", "pull")]
    public void TryParse_RejectsOtherGitHubPaths(string url, string expectedFragment)
    {
        PullRequestRef.TryParse(url, out var pr, out var error).Should().BeFalse();
        pr.Should().BeNull();
        error.Should().NotBeNull().And.Contain(expectedFragment);
    }

    [Theory]
    [InlineData("https://github.com/owner/repo/pull")]
    [InlineData("https://github.com/owner/repo")]
    [InlineData("https://github.com/owner")]
    [InlineData("https://github.com/")]
    public void TryParse_RejectsTooFewSegments(string url)
    {
        PullRequestRef.TryParse(url, out var pr, out var error).Should().BeFalse();
        pr.Should().BeNull();
        error.Should().NotBeNull();
    }

    [Theory]
    [InlineData("https://github.com/owner/repo/pull/abc")]
    [InlineData("https://github.com/owner/repo/pull/-5")]
    [InlineData("https://github.com/owner/repo/pull/0")]
    [InlineData("https://github.com/owner/repo/pull/7.0")]
    [InlineData("https://github.com/owner/repo/pull/+7")]
    public void TryParse_RejectsNonPositiveIntegerNumber(string url)
    {
        PullRequestRef.TryParse(url, out var pr, out var error).Should().BeFalse();
        pr.Should().BeNull();
        error.Should().NotBeNull();
    }
}
