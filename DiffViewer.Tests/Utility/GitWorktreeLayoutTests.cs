using System;
using System.IO;
using DiffViewer.Utility;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Utility;

/// <summary>
/// Tests for <see cref="GitWorktreeLayout"/> against synthetic on-disk
/// layouts. Real repositories are exercised by the enumerator and
/// repository-service tests; these cover the pure path reasoning,
/// including the layouts that are awkward to create for real (a bare
/// hub) and the malformed ones that must degrade rather than throw.
/// </summary>
public sealed class GitWorktreeLayoutTests : IDisposable
{
    private readonly string _root;

    public GitWorktreeLayoutTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "diffviewer-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void IsLinkedWorktree_ForAMainWorktreeGitDirectory_IsFalse()
    {
        var gitDir = CreateDirectory(".git");

        GitWorktreeLayout.IsLinkedWorktree(gitDir).Should().BeFalse();
    }

    [Fact]
    public void IsLinkedWorktree_WhenCommondirPointerIsPresent_IsTrue()
    {
        var gitDir = CreateLinkedWorktreeGitDirectory("feature-a");

        GitWorktreeLayout.IsLinkedWorktree(gitDir).Should().BeTrue();
    }

    [Fact]
    public void ResolveCommonDirectory_ForAMainWorktree_ReturnsTheDirectoryUnchanged()
    {
        var gitDir = CreateDirectory(".git");

        GitWorktreeLayout.ResolveCommonDirectory(gitDir).Should().Be(gitDir);
    }

    [Fact]
    public void ResolveCommonDirectory_StripsATrailingSeparator()
    {
        // libgit2 reports a main worktree's git directory with a
        // trailing separator, but the commondir pointer resolves
        // without one. Both must land on the same string or two
        // worktrees of a repository compare as unrelated.
        var gitDir = CreateDirectory(".git");
        var withSeparator = gitDir + Path.DirectorySeparatorChar;

        GitWorktreeLayout.ResolveCommonDirectory(withSeparator)
            .Should().Be(GitWorktreeLayout.ResolveCommonDirectory(gitDir));
    }

    [Fact]
    public void ResolveCommonDirectory_FromMainAndLinkedWorktrees_ReturnsTheIdenticalString()
    {
        var mainGitDir = CreateDirectory(".git") + Path.DirectorySeparatorChar;
        var linkedGitDir = CreateLinkedWorktreeGitDirectory("feature-a");

        GitWorktreeLayout.ResolveCommonDirectory(linkedGitDir)
            .Should().Be(GitWorktreeLayout.ResolveCommonDirectory(mainGitDir));
    }

    [Fact]
    public void ResolveCommonDirectory_FollowsARelativeCommondirPointer()
    {
        var gitDir = CreateLinkedWorktreeGitDirectory("feature-a");

        var resolved = GitWorktreeLayout.ResolveCommonDirectory(gitDir);

        Normalize(resolved).Should().Be(Normalize(Path.Combine(_root, ".git")));
    }

    [Fact]
    public void ResolveCommonDirectory_FollowsAnAbsoluteCommondirPointer()
    {
        var commonDir = CreateDirectory(".git");
        var gitDir = CreateDirectory(Path.Combine(".git", "worktrees", "feature-a"));
        File.WriteAllText(Path.Combine(gitDir, "commondir"), commonDir);

        Normalize(GitWorktreeLayout.ResolveCommonDirectory(gitDir)).Should().Be(Normalize(commonDir));
    }

    [Fact]
    public void ResolveCommonDirectory_WhenPointerIsEmpty_FallsBackToTheGivenDirectory()
    {
        var gitDir = CreateDirectory(Path.Combine(".git", "worktrees", "feature-a"));
        File.WriteAllText(Path.Combine(gitDir, "commondir"), "   \n");

        GitWorktreeLayout.ResolveCommonDirectory(gitDir).Should().Be(gitDir);
    }

    [Fact]
    public void TryGetWorktreeName_ForALinkedWorktree_ReturnsTheAdministrativeDirectoryName()
    {
        var gitDir = CreateLinkedWorktreeGitDirectory("feature-a");

        GitWorktreeLayout.TryGetWorktreeName(gitDir).Should().Be("feature-a");
    }

    [Fact]
    public void TryGetWorktreeName_ForAMainWorktree_ReturnsNull()
    {
        var gitDir = CreateDirectory(".git");

        GitWorktreeLayout.TryGetWorktreeName(gitDir).Should().BeNull();
    }

    [Fact]
    public void TryGetRepositoryName_ForAStandardCheckout_ReturnsTheDirectoryHoldingDotGit()
    {
        GitWorktreeLayout.TryGetRepositoryName(@"C:\Repos\DiffViewer\.git").Should().Be("DiffViewer");
    }

    [Fact]
    public void TryGetRepositoryName_IgnoresATrailingSeparator()
    {
        GitWorktreeLayout.TryGetRepositoryName(@"C:\Repos\DiffViewer\.git\").Should().Be("DiffViewer");
    }

    [Fact]
    public void TryGetRepositoryName_ForABareHub_TrimsTheDotGitSuffix()
    {
        // The "one bare clone plus N worktrees" layout: the common
        // directory is the bare repo itself, conventionally <name>.git.
        GitWorktreeLayout.TryGetRepositoryName(@"C:\Repos\DiffViewer.git").Should().Be("DiffViewer");
    }

    [Fact]
    public void TryGetRepositoryName_ForABareHubWithoutTheSuffix_UsesTheDirectoryName()
    {
        GitWorktreeLayout.TryGetRepositoryName(@"C:\Repos\DiffViewer").Should().Be("DiffViewer");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryGetRepositoryName_WhenPathIsBlank_ReturnsNull(string path)
    {
        GitWorktreeLayout.TryGetRepositoryName(path).Should().BeNull();
    }

    [Fact]
    public void Describe_ForAnOrdinaryCheckout_ReturnsTheRepositoryNameAndNoWorktreeName()
    {
        var workingDirectory = CreateDirectory("DiffViewer");
        Directory.CreateDirectory(Path.Combine(workingDirectory, ".git"));

        var labels = GitWorktreeLayout.Describe(workingDirectory);

        labels.RepositoryName.Should().Be("DiffViewer");
        labels.WorktreeName.Should().BeNull();
    }

    [Fact]
    public void Describe_ForALinkedWorktree_ReturnsBothTheRepositoryAndWorktreeName()
    {
        // A linked worktree's .git is a *file* pointing at the shared
        // repo's administrative directory - the layout that makes the
        // repository name invisible from the worktree path alone.
        CreateLinkedWorktreeGitDirectory("feature-x");
        var workingDirectory = CreateDirectory("some-unrelated-folder-name");
        File.WriteAllText(
            Path.Combine(workingDirectory, ".git"),
            $"gitdir: {Path.Combine(_root, ".git", "worktrees", "feature-x")}\n");

        var labels = GitWorktreeLayout.Describe(workingDirectory);

        labels.RepositoryName.Should().Be(Path.GetFileName(_root));
        labels.WorktreeName.Should().Be("feature-x");
    }

    [Fact]
    public void Describe_WhenPathIsNotARepository_ReturnsNothing()
    {
        var labels = GitWorktreeLayout.Describe(CreateDirectory("plain-folder"));

        labels.RepositoryName.Should().BeNull();
        labels.WorktreeName.Should().BeNull();
    }

    [Fact]
    public void TryResolveGitDirectory_WhenPointerFileIsMalformed_ReturnsNull()
    {
        var workingDirectory = CreateDirectory("broken");
        File.WriteAllText(Path.Combine(workingDirectory, ".git"), "this is not a gitdir pointer");

        GitWorktreeLayout.TryResolveGitDirectory(workingDirectory).Should().BeNull();
    }

    private string CreateDirectory(string relativePath)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(full);
        return full;
    }

    /// <summary>Build <c>&lt;root&gt;/.git/worktrees/&lt;name&gt;</c> with the
    /// relative <c>commondir</c> pointer git itself writes.</summary>
    private string CreateLinkedWorktreeGitDirectory(string name)
    {
        CreateDirectory(".git");
        var gitDir = CreateDirectory(Path.Combine(".git", "worktrees", name));
        File.WriteAllText(Path.Combine(gitDir, "commondir"), "../..\n");
        return gitDir;
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToLowerInvariant();
}
