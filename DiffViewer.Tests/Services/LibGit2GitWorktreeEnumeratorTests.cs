using System;
using System.IO;
using System.Linq;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// Behavioral tests for <see cref="LibGit2GitWorktreeEnumerator"/> against
/// real on-disk repositories. The interesting cases are the ones libgit2
/// does <em>not</em> hand us directly: the main worktree (absent from the
/// worktree list) and prunable worktrees (whose directory is gone).
/// </summary>
public sealed class LibGit2GitWorktreeEnumeratorTests : IDisposable
{
    private readonly TempRepo _repo = new();
    private readonly LibGit2GitWorktreeEnumerator _enumerator = new();

    public LibGit2GitWorktreeEnumeratorTests()
    {
        _repo.WriteFile("a.txt", "hello\n");
        _repo.InitialCommit();
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public void Enumerate_WhenRepoHasNoLinkedWorktrees_ReturnsOnlyTheMainWorktree()
    {
        var result = _enumerator.Enumerate(_repo.Path);

        result.Should().ContainSingle();
        result[0].IsMain.Should().BeTrue();
        result[0].IsCurrent.Should().BeTrue();
        result[0].IsMissing.Should().BeFalse();
        SamePath(result[0].WorkingDirectory, _repo.Path).Should().BeTrue();
    }

    [Fact]
    public void Enumerate_WhenLinkedWorktreeExists_ReturnsBothMainAndLinked()
    {
        var worktreePath = _repo.AddWorktree("feature-a");

        var result = _enumerator.Enumerate(_repo.Path);

        result.Should().HaveCount(2);
        result.Should().ContainSingle(e => e.IsMain);
        var linked = result.Single(e => !e.IsMain);
        linked.Name.Should().Be("feature-a");
        SamePath(linked.WorkingDirectory, worktreePath).Should().BeTrue();
    }

    [Fact]
    public void Enumerate_AlwaysListsTheMainWorktreeFirst()
    {
        _repo.AddWorktree("zzz-last");
        _repo.AddWorktree("aaa-first");

        var result = _enumerator.Enumerate(_repo.Path);

        result[0].IsMain.Should().BeTrue();
        result.Skip(1).Select(e => e.Name).Should().ContainInOrder("aaa-first", "zzz-last");
    }

    [Fact]
    public void Enumerate_FromInsideLinkedWorktree_StillReportsTheMainWorktree()
    {
        // libgit2 lists only *linked* worktrees, so the main entry has to
        // be synthesized by resolving the commondir pointer. This is the
        // case that regresses if that resolution breaks.
        var worktreePath = _repo.AddWorktree("feature-a");

        var result = _enumerator.Enumerate(worktreePath);

        result.Should().HaveCount(2);
        var main = result.Single(e => e.IsMain);
        SamePath(main.WorkingDirectory, _repo.Path).Should().BeTrue();
    }

    [Fact]
    public void Enumerate_FromInsideLinkedWorktree_ReturnsTheSameWorktreeSetAsFromMain()
    {
        _repo.AddWorktree("feature-a");
        var second = _repo.AddWorktree("feature-b");

        var fromMain = _enumerator.Enumerate(_repo.Path);
        var fromLinked = _enumerator.Enumerate(second);

        fromLinked.Select(e => e.WorkingDirectory.ToLowerInvariant())
            .Should().BeEquivalentTo(fromMain.Select(e => e.WorkingDirectory.ToLowerInvariant()));
    }

    [Fact]
    public void Enumerate_MarksOnlyTheQueriedWorktreeAsCurrent()
    {
        var worktreePath = _repo.AddWorktree("feature-a");

        var result = _enumerator.Enumerate(worktreePath);

        result.Should().ContainSingle(e => e.IsCurrent);
        SamePath(result.Single(e => e.IsCurrent).WorkingDirectory, worktreePath).Should().BeTrue();
    }

    [Fact]
    public void Enumerate_ReportsTheBranchCheckedOutInEachWorktree()
    {
        _repo.AddWorktree("feature-a");

        var result = _enumerator.Enumerate(_repo.Path);

        // `git worktree add <name>` creates and checks out a branch of
        // the same name; the main worktree keeps its own branch. The two
        // are necessarily different — git forbids sharing a branch.
        result.Single(e => !e.IsMain).HeadFriendlyName.Should().Be("feature-a");
        result.Single(e => e.IsMain).HeadFriendlyName.Should().NotBeNullOrWhiteSpace();
        result.Single(e => e.IsMain).HeadFriendlyName.Should().NotBe("feature-a");
    }

    [Fact]
    public void Enumerate_WhenWorktreeDirectoryWasDeleted_StillReportsItAsMissing()
    {
        var worktreePath = _repo.AddWorktree("feature-a");
        TempRepo.DeleteDirectory(worktreePath);

        var result = _enumerator.Enumerate(_repo.Path);

        var linked = result.Single(e => !e.IsMain);
        linked.IsMissing.Should().BeTrue();
        SamePath(linked.WorkingDirectory, worktreePath).Should().BeTrue();
    }

    [Fact]
    public void Enumerate_WhenOneWorktreeIsMissing_StillReportsTheHealthyOnes()
    {
        var doomed = _repo.AddWorktree("feature-doomed");
        _repo.AddWorktree("feature-healthy");
        TempRepo.DeleteDirectory(doomed);

        var result = _enumerator.Enumerate(_repo.Path);

        result.Should().HaveCount(3);
        result.Single(e => e.Name == "feature-healthy").IsMissing.Should().BeFalse();
    }

    [Fact]
    public void Enumerate_WhenPathIsNotARepository_ReturnsEmpty()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "diffviewer-not-a-repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            _enumerator.Enumerate(scratch).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Enumerate_WhenPathIsBlank_ReturnsEmpty(string path)
    {
        _enumerator.Enumerate(path).Should().BeEmpty();
    }

    [Fact]
    public void Enumerate_WhenPathDoesNotExist_ReturnsEmpty()
    {
        _enumerator.Enumerate(Path.Combine(Path.GetTempPath(), "definitely-not-here-" + Guid.NewGuid().ToString("N")))
            .Should().BeEmpty();
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);
}
