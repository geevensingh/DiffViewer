using DiffViewer.Models;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

/// <summary>
/// Window-title composition. Kept as a static seam on
/// <see cref="MainViewModel"/> so it is testable without constructing the
/// whole per-context view-model graph.
/// </summary>
public class MainViewModelWindowTitleTests
{
    [Fact]
    public void BuildWindowTitle_ForAMainWorktree_ShowsPathAndSides()
    {
        var title = MainViewModel.BuildWindowTitle(
            MakeShape(@"C:\Repos\DiffViewer", worktreeName: null),
            new DiffSide.CommitIsh("HEAD"),
            new DiffSide.WorkingTree());

        title.Should().Be(@"DiffViewer — C:\Repos\DiffViewer (HEAD ⇢ working tree)");
    }

    [Fact]
    public void BuildWindowTitle_ForALinkedWorktree_AppendsTheWorktreeName()
    {
        var title = MainViewModel.BuildWindowTitle(
            MakeShape(@"C:\worktrees\wt-feature-x", worktreeName: "feature-x"),
            new DiffSide.CommitIsh("HEAD"),
            new DiffSide.WorkingTree());

        title.Should().Be(@"DiffViewer — C:\worktrees\wt-feature-x [feature-x] (HEAD ⇢ working tree)");
    }

    [Fact]
    public void BuildWindowTitle_ForACommitComparison_RendersBothReferences()
    {
        var title = MainViewModel.BuildWindowTitle(
            MakeShape(@"C:\Repos\DiffViewer", worktreeName: null),
            new DiffSide.CommitIsh("main"),
            new DiffSide.CommitIsh("feature/x"));

        title.Should().Be(@"DiffViewer — C:\Repos\DiffViewer (main ⇢ feature/x)");
    }

    private static RepositoryShape MakeShape(string repoRoot, string? worktreeName) =>
        new(RepoRoot: repoRoot,
            WorkingDirectory: repoRoot,
            GitDir: System.IO.Path.Combine(repoRoot, ".git"),
            IsBare: false,
            IsHeadUnborn: false,
            IsSparseCheckout: false,
            IsPartialClone: false,
            HasInProgressOperation: false,
            CommonGitDirectory: @"C:\Repos\DiffViewer\.git",
            WorktreeName: worktreeName);
}
