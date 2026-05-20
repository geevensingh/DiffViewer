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

    [Fact]
    public void TryGetDefaultRemoteBranch_OnInvalidOrEmptyPath_ReturnsNull()
    {
        var sut = new LibGit2GitRefEnumerator();
        sut.TryGetDefaultRemoteBranch("").Should().BeNull();
        sut.TryGetDefaultRemoteBranch("   ").Should().BeNull();
        sut.TryGetDefaultRemoteBranch(@"C:\nope-" + Guid.NewGuid().ToString("N")).Should().BeNull();
    }

    [Fact]
    public void TryGetDefaultRemoteBranch_WithoutOriginHead_ReturnsNull()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        var c1 = t.InitialCommit("c1");
        // Remote-tracking branch exists, but no symbolic origin/HEAD.
        // Real-world case: --no-tags clone or a remote that didn't
        // advertise a default branch.
        t.CreateRemoteTrackingBranch("origin", "main", c1);

        var sut = new LibGit2GitRefEnumerator();
        sut.TryGetDefaultRemoteBranch(t.Path).Should().BeNull();
    }

    [Fact]
    public void TryGetDefaultRemoteBranch_WithOriginHeadPointingAtMain_ReturnsOriginMain()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        var c1 = t.InitialCommit("c1");
        t.CreateRemoteTrackingBranch("origin", "main", c1);
        t.SetRemoteHead("origin", "main");

        var sut = new LibGit2GitRefEnumerator();
        sut.TryGetDefaultRemoteBranch(t.Path).Should().Be("origin/main");
    }

    [Fact]
    public void TryGetDefaultRemoteBranch_WithOriginHeadPointingAtMaster_ReturnsOriginMaster()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        var c1 = t.InitialCommit("c1");
        t.CreateRemoteTrackingBranch("origin", "master", c1);
        t.SetRemoteHead("origin", "master");

        var sut = new LibGit2GitRefEnumerator();
        sut.TryGetDefaultRemoteBranch(t.Path).Should().Be("origin/master");
    }

    [Fact]
    public void TryGetDefaultRemoteBranch_WithOriginHeadPointingAtCustomBranch_ReturnsThatBranch()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        var c1 = t.InitialCommit("c1");
        t.CreateRemoteTrackingBranch("origin", "develop", c1);
        t.SetRemoteHead("origin", "develop");

        var sut = new LibGit2GitRefEnumerator();
        // A non-main/master default branch (some shops ship trunk or
        // develop). We surface whatever the clone recorded; the form
        // VM just treats the result as a string seed.
        sut.TryGetDefaultRemoteBranch(t.Path).Should().Be("origin/develop");
    }

    // ---- Stash enumeration tests ----

    [Fact]
    public void Enumerate_NoStashes_ReturnsEmptyStashList()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        t.InitialCommit("c1");

        var sut = new LibGit2GitRefEnumerator();
        var result = sut.Enumerate(t.Path);

        result.Stashes.Should().BeEmpty();
    }

    [Fact]
    public void Enumerate_ReturnsStashesMostRecentFirst()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "initial\n");
        t.InitialCommit("c1");

        // Create three stashes, each with a different subject.
        t.WriteFile("a.txt", "change-1\n");
        t.Stash("first stash");
        t.WriteFile("a.txt", "change-2\n");
        t.Stash("second stash");
        t.WriteFile("a.txt", "change-3\n");
        t.Stash("third stash");

        var sut = new LibGit2GitRefEnumerator();
        var result = sut.Enumerate(t.Path);

        result.Stashes.Should().HaveCount(3);

        // Most-recent-first: index 0 = newest, matching git stash list.
        result.Stashes[0].Index.Should().Be(0);
        result.Stashes[0].SymbolicName.Should().Be("stash@{0}");
        result.Stashes[0].Subject.Should().Contain("third stash");

        result.Stashes[1].Index.Should().Be(1);
        result.Stashes[1].SymbolicName.Should().Be("stash@{1}");
        result.Stashes[1].Subject.Should().Contain("second stash");

        result.Stashes[2].Index.Should().Be(2);
        result.Stashes[2].SymbolicName.Should().Be("stash@{2}");
        result.Stashes[2].Subject.Should().Contain("first stash");
    }

    [Fact]
    public void Enumerate_StashEntriesHaveValidShaAndTimestamp()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "initial\n");
        t.InitialCommit("c1");
        t.WriteFile("a.txt", "changed\n");
        var stashCommit = t.Stash("test stash");

        var sut = new LibGit2GitRefEnumerator();
        var result = sut.Enumerate(t.Path);

        var entry = result.Stashes.Should().ContainSingle().Which;
        entry.TipSha.Should().Be(stashCommit.Sha);
        entry.TipShortSha.Should().Be(stashCommit.Sha[..7]);
        entry.CreatedAt.Should().NotBe(default);
    }

    [Fact]
    public void Enumerate_OnFreshRepoWithNoCommits_ReturnsEmptyStashList()
    {
        using var t = new TempRepo();
        var sut = new LibGit2GitRefEnumerator();

        var result = sut.Enumerate(t.Path);

        result.Stashes.Should().BeEmpty();
    }
}
