using System;
using DiffViewer.Models;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.RecentContexts;

/// <summary>
/// Phase 7 of the PR-review feature: <see cref="RecentContextItem"/>
/// branches on <see cref="RecentLaunchContext.Review"/> to render
/// PR-mode rows distinctly in the dropdown. These tests pin the
/// presentation contract so future label refactors don't regress it.
/// </summary>
public class RecentContextItemTests
{
    [Fact]
    public void Title_LocalRow_RendersBranchArrowFormat()
    {
        var ctx = MakeLocal(@"C:\repos\diffviewer", leftRef: "main");
        var item = new RecentContextItem(ctx);

        item.Title.Should().Be("diffviewer · main → WT");
    }

    [Fact]
    public void Title_PrRow_RendersPullRequestFormat()
    {
        var ctx = MakePr(
            @"C:\repos\diffviewer",
            new PullRequestRef("github.com", "geevensingh", "diffviewer", 42));
        var item = new RecentContextItem(ctx);

        item.Title.Should().Be("diffviewer · PR geevensingh/diffviewer#42");
    }

    [Fact]
    public void Tooltip_LocalRow_DoesNotMentionPullRequest()
    {
        var ctx = MakeLocal(@"C:\repos\diffviewer", leftRef: "main");
        var item = new RecentContextItem(ctx);

        item.Tooltip.Should().NotContain("Pull request");
        item.Tooltip.Should().Contain("Left:");
        item.Tooltip.Should().Contain("Right:");
    }

    [Fact]
    public void Tooltip_PrRow_IncludesPullRequestUrl()
    {
        var ctx = MakePr(
            @"C:\repos\diffviewer",
            new PullRequestRef("github.com", "geevensingh", "diffviewer", 42));
        var item = new RecentContextItem(ctx);

        item.Tooltip.Should().Contain("Pull request: https://github.com/geevensingh/diffviewer/pull/42");
        item.Tooltip.Should().Contain("Last resolved base:");
        item.Tooltip.Should().Contain("Last resolved head:");
    }

    private static RecentLaunchContext MakeLocal(string repoPath, string leftRef)
    {
        var left = new DiffSide.CommitIsh(leftRef);
        var right = new DiffSide.WorkingTree();
        var id = ContextIdentityFactory.Create(repoPath, left, right);
        return new RecentLaunchContext(id, left, right, DateTimeOffset.UtcNow);
    }

    private static RecentLaunchContext MakePr(string repoPath, PullRequestRef pr)
    {
        // PR-mode rows always carry CommitIsh sides (SHA-pinned). The
        // specific SHA values don't matter for rendering tests.
        var left = new DiffSide.CommitIsh("abc1234");
        var right = new DiffSide.CommitIsh("def5678");
        var id = ContextIdentityFactory.Create(repoPath, left, right);
        return new RecentLaunchContext(id, left, right, DateTimeOffset.UtcNow, pr);
    }
}
