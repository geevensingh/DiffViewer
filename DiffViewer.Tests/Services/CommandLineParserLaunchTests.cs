using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// Tests for the launch-aware parser entry point. The legacy local-mode
/// parsing is covered by <see cref="CommandLineParserTests"/>; this class
/// only exercises the new three-variant return shape and the PR-URL branch.
/// </summary>
public class CommandLineParserLaunchTests
{
    private const string Cwd = @"C:\Repos\foo";

    private sealed class StubEnv : ICommandLineEnvironment
    {
        public string CurrentDirectory { get; init; } = Cwd;
        public HashSet<string> ExistingPaths { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GitRepos { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<(string repo, string commit), bool> Commits { get; init; } = new();
        public Dictionary<string, string> DiscoveredRoots { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public bool PathExists(string path) => ExistingPaths.Contains(path);
        public bool IsGitRepository(string path) => GitRepos.Contains(path);
        public bool TryResolveCommitIsh(string repoPath, string commitIsh)
            => Commits.TryGetValue((repoPath, commitIsh), out var ok) && ok;
        public string? TryDiscoverRepoRoot(string path)
            => DiscoveredRoots.TryGetValue(path, out var root) ? root : null;
    }

    private static StubEnv RepoOnlyEnv() => new() { GitRepos = { Cwd } };

    [Fact]
    public void ParseLaunch_PullRequestUrl_ReturnsPullRequestVariant()
    {
        var parser = new CommandLineParser();
        var env = new StubEnv();

        var plan = parser.ParseLaunch(
            new[] { "https://github.com/geevensingh/jotjson/pull/268" }, env);

        plan.IsPullRequest.Should().BeTrue();
        plan.IsLocal.Should().BeFalse();
        plan.IsError.Should().BeFalse();
        plan.PullRequest.Should().Be(new PullRequestRef("github.com", "geevensingh", "jotjson", 268));
    }

    [Fact]
    public void ParseLaunch_PullRequestUrlWithTrailingPath_ReturnsPullRequestVariant()
    {
        var parser = new CommandLineParser();
        var env = new StubEnv();

        var plan = parser.ParseLaunch(
            new[] { "https://github.com/owner/repo/pull/7/files" }, env);

        plan.IsPullRequest.Should().BeTrue();
        plan.PullRequest!.Number.Should().Be(7);
    }

    [Fact]
    public void ParseLaunch_NoArgs_ReturnsLocalVariant()
    {
        var parser = new CommandLineParser();
        var env = RepoOnlyEnv();

        var plan = parser.ParseLaunch(Array.Empty<string>(), env);

        plan.IsLocal.Should().BeTrue();
        plan.Local!.RepoPath.Should().Be(Cwd);
        plan.Local.Left.Should().BeOfType<DiffSide.CommitIsh>();
        plan.Local.Right.Should().BeOfType<DiffSide.WorkingTree>();
    }

    [Fact]
    public void ParseLaunch_NotARepo_ReturnsErrorVariant()
    {
        var parser = new CommandLineParser();
        var env = new StubEnv();

        var plan = parser.ParseLaunch(Array.Empty<string>(), env);

        plan.IsError.Should().BeTrue();
        plan.Error!.Kind.Should().Be(CommandLineErrorKind.NotAGitRepository);
    }

    [Fact]
    public void ParseLaunch_UnknownFlag_ReturnsErrorVariant()
    {
        var parser = new CommandLineParser();
        var env = RepoOnlyEnv();

        var plan = parser.ParseLaunch(new[] { "--nope" }, env);

        plan.IsError.Should().BeTrue();
        plan.Error!.Kind.Should().Be(CommandLineErrorKind.UnknownFlag);
    }

    [Fact]
    public void ParseLaunch_PrUrlPlusExtraArg_FallsThroughToLocalParse()
    {
        // Two args, even if the first looks like a PR URL, are not the
        // single-PR-URL launch shape. Fall through to local parsing, which
        // will reject the URL as not-a-path / not-a-commit.
        var parser = new CommandLineParser();
        var env = RepoOnlyEnv();

        var plan = parser.ParseLaunch(
            new[] { "https://github.com/owner/repo/pull/7", "HEAD~1" }, env);

        plan.IsPullRequest.Should().BeFalse();
        plan.IsError.Should().BeTrue();
    }

    [Fact]
    public void ParseLaunch_NonPrGitHubUrl_FallsThroughToLocalParse()
    {
        // A GitHub issue URL is not a PR URL, so the launch-aware parser
        // hands it to the local parser, which rejects it.
        var parser = new CommandLineParser();
        var env = RepoOnlyEnv();

        var plan = parser.ParseLaunch(
            new[] { "https://github.com/owner/repo/issues/7" }, env);

        plan.IsPullRequest.Should().BeFalse();
        plan.IsError.Should().BeTrue();
    }
}
