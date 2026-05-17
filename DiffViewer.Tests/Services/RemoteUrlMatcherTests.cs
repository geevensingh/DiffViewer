using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public sealed class RemoteUrlMatcherTests
{
    [Theory]
    [InlineData("https://github.com/owner/repo")]
    [InlineData("https://github.com/owner/repo.git")]
    [InlineData("https://github.com/owner/repo/")]
    [InlineData("https://github.com/owner/repo.git/")]
    [InlineData("http://github.com/owner/repo.git")]
    public void TryExtractKey_HttpsForms_Parse(string url)
    {
        var key = RemoteUrlMatcher.TryExtractKey(url);
        key.Should().Be(RepoUrlKey.From("github.com", "owner", "repo"));
    }

    [Theory]
    [InlineData("git@github.com:owner/repo")]
    [InlineData("git@github.com:owner/repo.git")]
    [InlineData("git@github.com:owner/repo/")]
    public void TryExtractKey_SshShortForms_Parse(string url)
    {
        var key = RemoteUrlMatcher.TryExtractKey(url);
        key.Should().Be(RepoUrlKey.From("github.com", "owner", "repo"));
    }

    [Theory]
    [InlineData("ssh://git@github.com/owner/repo")]
    [InlineData("ssh://git@github.com/owner/repo.git")]
    [InlineData("ssh://github.com/owner/repo.git")]
    [InlineData("ssh://git@github.com:22/owner/repo.git")]
    public void TryExtractKey_SshLongForms_Parse(string url)
    {
        var key = RemoteUrlMatcher.TryExtractKey(url);
        key.Should().Be(RepoUrlKey.From("github.com", "owner", "repo"));
    }

    [Fact]
    public void TryExtractKey_GheHost_PreservesHost()
    {
        // Phase 5 host plumbing — make sure non-github.com hosts make
        // it through unchanged (still lowercased).
        var key = RemoteUrlMatcher.TryExtractKey("https://ghe.example.com/team/project.git");
        key.Should().Be(RepoUrlKey.From("ghe.example.com", "team", "project"));
    }

    [Fact]
    public void TryExtractKey_LowercasesAllSegments()
    {
        var key = RemoteUrlMatcher.TryExtractKey("https://GitHub.com/GeevenSingh/JotJson.git");
        key.Should().Be(RepoUrlKey.From("github.com", "geevensingh", "jotjson"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("file:///c:/repos/local")]
    [InlineData("https://github.com/owner")]                              // missing repo
    [InlineData("https://github.com/owner/repo/extra/path")]              // extra trailing path
    [InlineData("git@github.com")]                                        // missing colon+path
    [InlineData("ssh://git@github.com")]                                  // no path
    public void TryExtractKey_RejectsMalformed(string? url)
    {
        RemoteUrlMatcher.TryExtractKey(url).Should().BeNull();
    }

    [Fact]
    public void TryExtractKey_TrimsLeadingTrailingWhitespace()
    {
        // Defensive: git config values sometimes have trailing whitespace.
        var key = RemoteUrlMatcher.TryExtractKey("  https://github.com/owner/repo.git  ");
        key.Should().Be(RepoUrlKey.From("github.com", "owner", "repo"));
    }
}
