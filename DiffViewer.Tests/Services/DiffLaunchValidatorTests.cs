using System;
using System.Collections.Generic;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// Tests for <see cref="DiffLaunchValidator"/>. Mirrors the behaviour
/// surfaced by the CLI parser (path discovery, commit-ish resolution),
/// since both flow through the same <see cref="ICommandLineEnvironment"/>
/// seam.
/// </summary>
public class DiffLaunchValidatorTests
{
    /// <summary>Stub env mirroring the one used by CommandLineParserTests.</summary>
    private sealed class StubEnv : ICommandLineEnvironment
    {
        public string CurrentDirectory { get; init; } = @"C:\";
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

    [Fact]
    public void ValidateRepoPath_Empty_ReturnsInvalid()
    {
        var v = new DiffLaunchValidator(new StubEnv());
        v.ValidateRepoPath("").Should().BeOfType<RepoPathValidation.Invalid>();
        v.ValidateRepoPath("   ").Should().BeOfType<RepoPathValidation.Invalid>();
    }

    [Fact]
    public void ValidateRepoPath_PathMissing_ReturnsInvalid()
    {
        var v = new DiffLaunchValidator(new StubEnv());
        v.ValidateRepoPath(@"C:\does\not\exist").Should().BeOfType<RepoPathValidation.Invalid>()
            .Which.Message.Should().Contain("does not exist");
    }

    [Fact]
    public void ValidateRepoPath_RepoRoot_ReturnsValidAtSamePath()
    {
        var env = new StubEnv
        {
            ExistingPaths = { @"C:\repos\foo" },
            GitRepos = { @"C:\repos\foo" },
        };
        var v = new DiffLaunchValidator(env);

        var result = v.ValidateRepoPath(@"C:\repos\foo");

        result.Should().BeOfType<RepoPathValidation.Valid>()
            .Which.CanonicalPath.Should().Be(@"C:\repos\foo");
    }

    [Fact]
    public void ValidateRepoPath_SubdirOfRepo_DiscoversAndReturnsRoot()
    {
        // CLI parser semantics: launching from a subdirectory should
        // resolve back to the enclosing repo root.
        var env = new StubEnv
        {
            ExistingPaths = { @"C:\repos\foo\src\sub" },
            DiscoveredRoots = { [@"C:\repos\foo\src\sub"] = @"C:\repos\foo" },
        };
        var v = new DiffLaunchValidator(env);

        var result = v.ValidateRepoPath(@"C:\repos\foo\src\sub");

        result.Should().BeOfType<RepoPathValidation.Valid>()
            .Which.CanonicalPath.Should().Be(@"C:\repos\foo");
    }

    [Fact]
    public void ValidateRepoPath_NotARepoNotInARepo_ReturnsInvalid()
    {
        var env = new StubEnv
        {
            ExistingPaths = { @"C:\notrepo" },
            // not in GitRepos, not in DiscoveredRoots
        };
        var v = new DiffLaunchValidator(env);

        v.ValidateRepoPath(@"C:\notrepo").Should().BeOfType<RepoPathValidation.Invalid>()
            .Which.Message.Should().Contain("Not a git repository");
    }

    [Fact]
    public void ValidateCommitIsh_Empty_ReturnsInvalid()
    {
        var v = new DiffLaunchValidator(new StubEnv());
        v.ValidateCommitIsh(@"C:\repos\foo", "").Should().BeOfType<CommitIshValidation.Invalid>();
        v.ValidateCommitIsh(@"C:\repos\foo", "  ").Should().BeOfType<CommitIshValidation.Invalid>();
    }

    [Fact]
    public void ValidateCommitIsh_Resolves_ReturnsValid()
    {
        var env = new StubEnv
        {
            Commits = { [(@"C:\repos\foo", "main")] = true },
        };
        var v = new DiffLaunchValidator(env);

        v.ValidateCommitIsh(@"C:\repos\foo", "main").Should().BeOfType<CommitIshValidation.Valid>();
    }

    [Fact]
    public void ValidateCommitIsh_DoesNotResolve_ReturnsInvalid()
    {
        var env = new StubEnv();
        var v = new DiffLaunchValidator(env);

        v.ValidateCommitIsh(@"C:\repos\foo", "bogus").Should().BeOfType<CommitIshValidation.Invalid>()
            .Which.Message.Should().Contain("Cannot resolve");
    }

    [Fact]
    public void ValidatePullRequestUrl_ValidGitHubUrl_ReturnsValid()
    {
        var v = new DiffLaunchValidator(new StubEnv());

        var result = v.ValidatePullRequestUrl("https://github.com/owner/repo/pull/42");

        result.Should().BeOfType<PullRequestUrlValidation.Valid>()
            .Which.Pr.Should().Be(new PullRequestRef("github.com", "owner", "repo", 42));
    }

    [Fact]
    public void ValidatePullRequestUrl_Garbage_ReturnsInvalid()
    {
        var v = new DiffLaunchValidator(new StubEnv());

        v.ValidatePullRequestUrl("not a url").Should().BeOfType<PullRequestUrlValidation.Invalid>();
        v.ValidatePullRequestUrl("").Should().BeOfType<PullRequestUrlValidation.Invalid>();
    }
}
