using DiffViewer.Services;
using FluentAssertions;
using LibGit2Sharp;
using System.IO;
using Xunit;

namespace DiffViewer.Tests.Services;

public class GitRefEnumeratorTests
{
    [Fact]
    public void Enumerate_OnInvalidPath_ReturnsEmpty()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "diffviewer-not-a-repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bogus);
        try
        {
            var sut = new LibGit2GitRefEnumerator();
            var result = sut.Enumerate(bogus);

            result.Should().BeSameAs(RefEnumerationResult.Empty);
        }
        finally
        {
            Directory.Delete(bogus, recursive: true);
        }
    }

    [Fact]
    public void Enumerate_OnEmptyOrNullPath_ReturnsEmpty()
    {
        var sut = new LibGit2GitRefEnumerator();
        sut.Enumerate("").Should().BeSameAs(RefEnumerationResult.Empty);
        sut.Enumerate("   ").Should().BeSameAs(RefEnumerationResult.Empty);
    }

    [Fact]
    public void Enumerate_OnFreshRepoWithNoCommits_ReturnsEmptyLists()
    {
        using var t = new TempRepo();
        var sut = new LibGit2GitRefEnumerator();

        var result = sut.Enumerate(t.Path);

        // Unborn HEAD: no branches resolve a Tip, no tags exist yet.
        result.LocalBranches.Should().BeEmpty();
        result.RemoteBranches.Should().BeEmpty();
        result.Tags.Should().BeEmpty();
    }

    [Fact]
    public void Enumerate_ReturnsLocalBranchesSortedAlphabetically()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        var c1 = t.InitialCommit("c1");
        t.CreateBranch("feature/zebra", c1);
        t.CreateBranch("feature/alpha", c1);

        var sut = new LibGit2GitRefEnumerator();
        var result = sut.Enumerate(t.Path);

        result.LocalBranches.Select(b => b.FriendlyName)
            .Should().ContainInOrder("feature/alpha", "feature/zebra")
            .And.HaveCount(3); // alpha, zebra, and the default branch
        result.LocalBranches.Should().AllSatisfy(b =>
        {
            b.TipSha.Should().Be(c1.Sha);
            b.TipShortSha.Should().Be(c1.Sha[..7]);
        });
    }

    [Fact]
    public void Enumerate_SeparatesRemoteFromLocalBranches()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        var c1 = t.InitialCommit("c1");
        t.CreateRemoteTrackingBranch("origin", "feature/x", c1);

        var sut = new LibGit2GitRefEnumerator();
        var result = sut.Enumerate(t.Path);

        result.LocalBranches.Should().NotContain(b => b.FriendlyName.StartsWith("origin/"));
        result.RemoteBranches.Select(b => b.FriendlyName)
            .Should().Contain("origin/feature/x");
    }

    [Fact]
    public void Enumerate_ExcludesOriginHeadFromRemoteBranches()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        var c1 = t.InitialCommit("c1");
        t.CreateRemoteTrackingBranch("origin", "master", c1);

        // Create the synthetic origin/HEAD symbolic ref pointing at
        // origin/master — git fetch creates this automatically; we
        // install it manually here.
        using (var repo = new Repository(t.Path))
        {
            repo.Refs.Add("refs/remotes/origin/HEAD", "refs/remotes/origin/master", allowOverwrite: true);
        }

        var sut = new LibGit2GitRefEnumerator();
        var result = sut.Enumerate(t.Path);

        result.RemoteBranches.Should().NotContain(b => b.FriendlyName == "origin/HEAD");
        result.RemoteBranches.Should().Contain(b => b.FriendlyName == "origin/master");
    }

    [Fact]
    public void Enumerate_ReturnsLightweightAndAnnotatedTags()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        var c1 = t.InitialCommit("c1");
        t.CreateLightweightTag("v0.1.0", c1);
        t.CreateAnnotatedTag("v0.2.0", c1, "release 0.2");

        var sut = new LibGit2GitRefEnumerator();
        var result = sut.Enumerate(t.Path);

        result.Tags.Select(x => x.FriendlyName)
            .Should().Contain(new[] { "v0.1.0", "v0.2.0" });
        result.Tags.Should().AllSatisfy(x =>
        {
            x.TipSha.Should().Be(c1.Sha);
        });
    }

    [Fact]
    public void TryComputeMergeBase_ReturnsCommonAncestor()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        var root = t.InitialCommit("root");

        t.CreateBranch("branch-a", root);
        t.Checkout("branch-a");
        t.WriteFile("a.txt", "a-changed\n");
        t.Commit("on branch-a");

        // Default branch is still at root. Branch off another path
        // from root so both sides have moved past it.
        t.CreateBranch("branch-b", root);
        t.Checkout("branch-b");
        t.WriteFile("b.txt", "b\n");
        t.Commit("on branch-b");

        var sut = new LibGit2GitRefEnumerator();
        var mb = sut.TryComputeMergeBase(t.Path, "branch-a", "branch-b");

        mb.Should().Be(root.Sha);
    }

    [Fact]
    public void TryComputeMergeBase_OnUnrelatedHistories_ReturnsNull()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        var c1 = t.InitialCommit("c1");

        // Build a parentless commit via the libgit2 ObjectDatabase
        // primitives and tag it. That commit shares no ancestor with
        // c1, so the merge-base must come back null.
        using (var repo = new Repository(t.Path))
        {
            var tree = repo.Lookup<Commit>(c1.Sha)!.Tree;
            var orphan = repo.ObjectDatabase.CreateCommit(
                t.Author, t.Author, "orphan", tree,
                parents: Array.Empty<Commit>(),
                prettifyMessage: true);
            repo.Refs.Add("refs/heads/orphan", orphan.Sha);
        }

        var sut = new LibGit2GitRefEnumerator();
        var mb = sut.TryComputeMergeBase(t.Path, c1.Sha, "orphan");

        mb.Should().BeNull();
    }

    [Fact]
    public void TryComputeMergeBase_OnUnresolvableRef_ReturnsNull()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        t.InitialCommit("c1");

        var sut = new LibGit2GitRefEnumerator();

        sut.TryComputeMergeBase(t.Path, "master", "does-not-exist").Should().BeNull();
        sut.TryComputeMergeBase(t.Path, "does-not-exist", "master").Should().BeNull();
    }

    [Fact]
    public void TryComputeMergeBase_OnInvalidPath_ReturnsNull()
    {
        var sut = new LibGit2GitRefEnumerator();

        sut.TryComputeMergeBase("", "a", "b").Should().BeNull();
        sut.TryComputeMergeBase(@"C:\nope-" + Guid.NewGuid().ToString("N"), "a", "b").Should().BeNull();
    }

    [Fact]
    public void TryComputeMergeBase_OnEmptyRefArgs_ReturnsNull()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        t.InitialCommit("c1");

        var sut = new LibGit2GitRefEnumerator();

        sut.TryComputeMergeBase(t.Path, "", "master").Should().BeNull();
        sut.TryComputeMergeBase(t.Path, "master", "  ").Should().BeNull();
    }
}
