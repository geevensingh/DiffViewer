using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.RecentContexts;

public class RecentContextsServiceTests : IDisposable
{
    private readonly string _path;

    public RecentContextsServiceTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"recents-svc-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { /* best-effort */ }
        foreach (var directory in _scratchDirectories)
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best-effort */ }
        }
    }

    private readonly List<string> _scratchDirectories = new();

    /// <summary>Build a synthetic checkout on disk: a working directory
    /// whose <c>.git</c> is either a real directory (main worktree) or a
    /// pointer file into a shared repo's administrative directory
    /// (linked worktree). Enough for the label capture on the recents
    /// write path, which reads files rather than opening libgit2.</summary>
    private string CreateSyntheticCheckout(string repositoryName, string? worktreeName = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"recents-repo-{Guid.NewGuid():N}");
        _scratchDirectories.Add(root);

        var repository = Path.Combine(root, repositoryName);
        var commonGitDir = Path.Combine(repository, ".git");
        Directory.CreateDirectory(commonGitDir);

        if (worktreeName is null) return repository;

        var administrative = Path.Combine(commonGitDir, "worktrees", worktreeName);
        Directory.CreateDirectory(administrative);
        File.WriteAllText(Path.Combine(administrative, "commondir"), "../..\n");

        // The worktree directory name deliberately differs from the
        // worktree's git name, which is the case that makes the path
        // leaf useless as a label.
        var worktreeDirectory = Path.Combine(root, "checkouts", $"wt-{worktreeName}");
        Directory.CreateDirectory(worktreeDirectory);
        File.WriteAllText(Path.Combine(worktreeDirectory, ".git"), $"gitdir: {administrative}\n");
        return worktreeDirectory;
    }

    [Fact]
    public async Task RecordLaunchAsync_ForAMainWorktree_StampsTheRepositoryNameAndNoWorktreeName()
    {
        var repoPath = CreateSyntheticCheckout("DiffViewer");
        var svc = new RecentContextsService(_path);

        await svc.RecordLaunchAsync(
            ContextIdentityFactory.Create(repoPath, Left, Right), Left, Right);

        svc.Current.Should().ContainSingle();
        svc.Current[0].RepositoryName.Should().Be("DiffViewer");
        svc.Current[0].WorktreeName.Should().BeNull();
    }

    [Fact]
    public async Task RecordLaunchAsync_ForALinkedWorktree_StampsBothLabels()
    {
        var worktreePath = CreateSyntheticCheckout("DiffViewer", worktreeName: "feature-x");
        var svc = new RecentContextsService(_path);

        await svc.RecordLaunchAsync(
            ContextIdentityFactory.Create(worktreePath, Left, Right), Left, Right);

        svc.Current[0].RepositoryName.Should().Be("DiffViewer");
        svc.Current[0].WorktreeName.Should().Be("feature-x");
    }

    [Fact]
    public async Task RecordLaunchAsync_PersistsTheLabelsToDisk()
    {
        var worktreePath = CreateSyntheticCheckout("DiffViewer", worktreeName: "feature-x");
        var svc = new RecentContextsService(_path);

        await svc.RecordLaunchAsync(
            ContextIdentityFactory.Create(worktreePath, Left, Right), Left, Right);

        var reloaded = new RecentContextsService(_path);
        await reloaded.LoadAsync();

        reloaded.Current[0].RepositoryName.Should().Be("DiffViewer");
        reloaded.Current[0].WorktreeName.Should().Be("feature-x");
    }

    [Fact]
    public async Task RecordLaunchAsync_ReRecordingAnUnlabeledRow_HealsItInPlace()
    {
        // A row written before worktree labelling existed carries none.
        // Re-launching the same diff must fill them in rather than
        // leaving a permanently unlabeled duplicate-looking entry.
        var worktreePath = CreateSyntheticCheckout("DiffViewer", worktreeName: "feature-x");
        var identity = ContextIdentityFactory.Create(worktreePath, Left, Right);

        await RecentsStore.ReadAndMutateAsync(_path, _ => RecentsDoc.From(new[]
        {
            new RecentLaunchContext(identity, Left, Right, DateTimeOffset.UtcNow.AddDays(-1)),
        }));

        var svc = new RecentContextsService(_path);
        await svc.LoadAsync();
        svc.Current.Should().ContainSingle();
        svc.Current[0].RepositoryName.Should().BeNull("precondition: the legacy row is unlabeled");

        await svc.RecordLaunchAsync(identity, Left, Right);

        svc.Current.Should().ContainSingle("re-recording bumps the existing row rather than adding one");
        svc.Current[0].RepositoryName.Should().Be("DiffViewer");
        svc.Current[0].WorktreeName.Should().Be("feature-x");
    }

    [Fact]
    public async Task RecordLaunchAsync_RunsTheLabelProbeOffTheCallingThread()
    {
        // The coordinator awaits this from the UI thread during a context
        // swap, and the gate completes synchronously when uncontended, so
        // a synchronous probe would stat the repo path on the dispatcher.
        // A repo on an offline share would then freeze the window.
        var repoPath = CreateSyntheticCheckout("DiffViewer", worktreeName: "feature-x");
        var callingThreadId = Environment.CurrentManagedThreadId;
        var probeThreadId = callingThreadId;

        var svc = new RecentContextsService(_path, labelRunner: probe => Task.Run(() =>
        {
            probeThreadId = Environment.CurrentManagedThreadId;
            return probe();
        }));

        await svc.RecordLaunchAsync(
            ContextIdentityFactory.Create(repoPath, Left, Right), Left, Right);

        probeThreadId.Should().NotBe(callingThreadId);
        // ...and the labels still land, so the move didn't cost behaviour.
        svc.Current[0].RepositoryName.Should().Be("DiffViewer");
        svc.Current[0].WorktreeName.Should().Be("feature-x");
    }

    [Fact]
    public async Task RecordLaunchAsync_YieldsBeforeProbingTheFileSystem()
    {
        // Guards the specific trap: `_gate.WaitAsync` completes
        // synchronously when uncontended, so anything before the first
        // real await stays on the caller's thread. Gated on a TCS rather
        // than Task.Yield so the assertion is deterministic — the probe
        // provably cannot have run when we check.
        var repoPath = CreateSyntheticCheckout("DiffViewer");
        var release = new TaskCompletionSource();
        var probed = false;

        var svc = new RecentContextsService(_path, labelRunner: async probe =>
        {
            await release.Task;
            probed = true;
            return probe();
        });

        var pending = svc.RecordLaunchAsync(
            ContextIdentityFactory.Create(repoPath, Left, Right), Left, Right);

        probed.Should().BeFalse("the probe must not have run synchronously on the caller");
        release.SetResult();
        await pending;
        probed.Should().BeTrue();
    }

    [Fact]
    public async Task RecordLaunchAsync_ForAPathThatIsNotARepository_LeavesTheLabelsNull()
    {
        // The label lookup is best-effort; an unreadable path must not
        // fail the launch record.
        var svc = new RecentContextsService(_path);
        var identity = ContextIdentityFactory.Create(
            Path.Combine(Path.GetTempPath(), $"not-a-repo-{Guid.NewGuid():N}"), Left, Right);

        await svc.RecordLaunchAsync(identity, Left, Right);

        svc.Current.Should().ContainSingle();
        svc.Current[0].RepositoryName.Should().BeNull();
        svc.Current[0].WorktreeName.Should().BeNull();
    }

    private static readonly DiffSide Left = new DiffSide.CommitIsh("HEAD");
    private static readonly DiffSide Right = new DiffSide.WorkingTree();

    [Fact]
    public void Current_BeforeLoad_IsEmpty()
    {
        var svc = new RecentContextsService(_path);
        svc.Current.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_FromMissingFile_LeavesEmptyAndRaisesChanged()
    {
        var svc = new RecentContextsService(_path);
        var fired = 0;
        svc.Changed += (_, _) => fired++;

        await svc.LoadAsync();

        svc.Current.Should().BeEmpty();
        fired.Should().Be(1);
    }

    [Fact]
    public async Task LoadAsync_FromPopulatedFile_HydratesMruSorted()
    {
        var older = MakeContext(@"C:\repos\old", "main", DateTimeOffset.UtcNow.AddDays(-5));
        var newer = MakeContext(@"C:\repos\new", "main", DateTimeOffset.UtcNow.AddMinutes(-1));
        await RecentsStore.ReadAndMutateAsync(_path, _ => RecentsDoc.From(new[] { older, newer }));

        var svc = new RecentContextsService(_path);
        await svc.LoadAsync();

        svc.Current.Select(i => i.Identity.CanonicalRepoPath).Should()
            .ContainInOrder(
                ContextIdentityFactory.CanonicalizeRepoPath(@"C:\repos\new"),
                ContextIdentityFactory.CanonicalizeRepoPath(@"C:\repos\old"));
    }

    [Fact]
    public async Task RecordLaunchAsync_AppendsAndPersists()
    {
        var svc = new RecentContextsService(_path);
        await svc.LoadAsync();

        var (id, left, right) = MakeIdentity(@"C:\repos\foo", "main");
        await svc.RecordLaunchAsync(id, left, right);

        svc.Current.Should().ContainSingle();
        svc.Current[0].Identity.Should().Be(id);

        var reloaded = new RecentContextsService(_path);
        await reloaded.LoadAsync();
        reloaded.Current.Should().ContainSingle()
            .Which.Identity.Should().Be(id);
    }

    [Fact]
    public async Task RecordLaunchAsync_RaisesChangedEvent()
    {
        var svc = new RecentContextsService(_path);
        await svc.LoadAsync();
        var fired = 0;
        svc.Changed += (_, _) => fired++;

        var (id, left, right) = MakeIdentity(@"C:\repos\foo", "main");
        await svc.RecordLaunchAsync(id, left, right);

        fired.Should().Be(1);
    }

    [Fact]
    public async Task RecordLaunchAsync_DedupsBySameIdentity_KeepingNewerTimestamp()
    {
        var svc = new RecentContextsService(_path);
        var (id, left, right) = MakeIdentity(@"C:\repos\foo", "main");

        await svc.RecordLaunchAsync(id, left, right);
        var firstStamp = svc.Current[0].LastUsedUtc;
        await Task.Delay(20);
        await svc.RecordLaunchAsync(id, left, right);

        svc.Current.Should().ContainSingle();
        svc.Current[0].LastUsedUtc.Should().BeAfter(firstStamp);
    }

    [Fact]
    public async Task RecordLaunchAsync_DedupsCaseInsensitivelyOnPath()
    {
        var svc = new RecentContextsService(_path);
        var (id1, left, right) = MakeIdentity(@"C:\Repos\Foo", "main");
        var (id2, _, _) = MakeIdentity(@"c:\repos\foo", "main");

        await svc.RecordLaunchAsync(id1, left, right);
        await svc.RecordLaunchAsync(id2, left, right);

        svc.Current.Should().ContainSingle("paths differ only in case so they refer to the same dir");
    }

    [Fact]
    public async Task RecordLaunchAsync_DistinguishesByDiffSide()
    {
        var svc = new RecentContextsService(_path);
        var (id1, l1, r1) = MakeIdentity(@"C:\repos\foo", "main");
        var (id2, l2, r2) = MakeIdentity(@"C:\repos\foo", "develop");

        await svc.RecordLaunchAsync(id1, l1, r1);
        await svc.RecordLaunchAsync(id2, l2, r2);

        svc.Current.Should().HaveCount(2);
    }

    [Fact]
    public async Task RecordLaunchAsync_DistinguishesByCommitIshCase()
    {
        var svc = new RecentContextsService(_path);
        var (id1, l1, r1) = MakeIdentity(@"C:\repos\foo", "HEAD");
        var (id2, l2, r2) = MakeIdentity(@"C:\repos\foo", "head");

        await svc.RecordLaunchAsync(id1, l1, r1);
        await svc.RecordLaunchAsync(id2, l2, r2);

        svc.Current.Should().HaveCount(2, "CommitIsh refs are case-sensitive by design");
    }

    [Fact]
    public async Task RecordLaunchAsync_CapsAtTen_DroppingOldest()
    {
        var svc = new RecentContextsService(_path);

        // Insert 12 distinct entries spread across time, oldest first.
        for (var i = 0; i < 12; i++)
        {
            var (id, left, right) = MakeIdentity($@"C:\repos\repo{i}", "main");
            await svc.RecordLaunchAsync(id, left, right);
            await Task.Delay(2); // ensure distinct timestamps
        }

        svc.Current.Should().HaveCount(RecentContextsService.MaxEntries);
        // Newest is the most-recently-recorded (repo11).
        svc.Current[0].Identity.CanonicalRepoPath.Should().EndWith("repo11");
        // Oldest two (repo0, repo1) should be evicted.
        svc.Current.Select(i => i.Identity.CanonicalRepoPath)
            .Should().NotContain(p => p.EndsWith("repo0") || p.EndsWith("repo1"));
    }

    [Fact]
    public async Task RecordLaunchAsync_PreservesUserDisplaySides_NotIdentitySides()
    {
        // Caller may pass display sides that differ from the identity's
        // canonical sides (e.g. a user-typed alias). The service must
        // preserve display verbatim; identity drives dedup.
        var svc = new RecentContextsService(_path);
        var canonical = ContextIdentityFactory.Create(@"C:\repos\foo",
            new DiffSide.CommitIsh("main"), new DiffSide.WorkingTree());
        var displayLeft = new DiffSide.CommitIsh("Main"); // different casing
        var displayRight = new DiffSide.WorkingTree();

        await svc.RecordLaunchAsync(canonical, displayLeft, displayRight);

        svc.Current[0].LeftDisplay.Should().Be(displayLeft);
        ((DiffSide.CommitIsh)svc.Current[0].LeftDisplay).Reference.Should().Be("Main");
    }

    [Fact]
    public async Task RemoveAsync_DropsMatchingEntry()
    {
        var svc = new RecentContextsService(_path);
        var (id1, l1, r1) = MakeIdentity(@"C:\repos\foo", "main");
        var (id2, l2, r2) = MakeIdentity(@"C:\repos\bar", "main");
        await svc.RecordLaunchAsync(id1, l1, r1);
        await svc.RecordLaunchAsync(id2, l2, r2);

        await svc.RemoveAsync(id1);

        svc.Current.Should().ContainSingle()
            .Which.Identity.Should().Be(id2);
    }

    [Fact]
    public async Task RemoveAsync_UnknownIdentity_NoOp()
    {
        var svc = new RecentContextsService(_path);
        var (id, left, right) = MakeIdentity(@"C:\repos\foo", "main");
        await svc.RecordLaunchAsync(id, left, right);

        var unknown = ContextIdentityFactory.Create(@"C:\repos\nope",
            new DiffSide.CommitIsh("main"), new DiffSide.WorkingTree());
        await svc.RemoveAsync(unknown);

        svc.Current.Should().ContainSingle();
    }

    [Fact]
    public async Task RemoveAsync_RaisesChangedEvent_EvenWhenNoOp()
    {
        // Acceptable: the event fires whenever the snapshot is replaced,
        // regardless of whether content actually changed. UI consumers
        // re-render but don't visibly flicker.
        var svc = new RecentContextsService(_path);
        var fired = 0;
        svc.Changed += (_, _) => fired++;

        var unknown = ContextIdentityFactory.Create(@"C:\repos\nope",
            new DiffSide.CommitIsh("main"), new DiffSide.WorkingTree());
        await svc.RemoveAsync(unknown);

        fired.Should().Be(1);
    }

    [Fact]
    public async Task RecordLaunchAsync_NullSidesThrow()
    {
        var svc = new RecentContextsService(_path);
        var id = ContextIdentityFactory.Create(@"C:\repos\foo",
            new DiffSide.WorkingTree(), new DiffSide.WorkingTree());

        Func<Task> nullLeft = () => svc.RecordLaunchAsync(id, null!, new DiffSide.WorkingTree());
        Func<Task> nullRight = () => svc.RecordLaunchAsync(id, new DiffSide.WorkingTree(), null!);

        await nullLeft.Should().ThrowAsync<ArgumentNullException>();
        await nullRight.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ConcurrentRecord_FromSameInstance_NoLostUpdate()
    {
        var svc = new RecentContextsService(_path);
        var tasks = new List<Task>();
        for (var i = 0; i < 8; i++)
        {
            var (id, left, right) = MakeIdentity($@"C:\repos\repo{i}", "main");
            tasks.Add(Task.Run(() => svc.RecordLaunchAsync(id, left, right)));
        }
        await Task.WhenAll(tasks);

        svc.Current.Select(i => i.Identity.CanonicalRepoPath)
            .Should().OnlyHaveUniqueItems()
            .And.HaveCount(8);
    }

    [Fact]
    public async Task ConcurrentRecord_FromDifferentInstances_BothPersist()
    {
        // Simulates two DiffViewer processes recording at the same time.
        // RecentsStore's FileShare.None lock + the read-modify-write
        // primitive guarantee both end up on disk.
        var svc1 = new RecentContextsService(_path);
        var svc2 = new RecentContextsService(_path);

        var (id1, l1, r1) = MakeIdentity(@"C:\repos\one", "main");
        var (id2, l2, r2) = MakeIdentity(@"C:\repos\two", "main");

        await Task.WhenAll(
            svc1.RecordLaunchAsync(id1, l1, r1),
            svc2.RecordLaunchAsync(id2, l2, r2));

        // Each instance only sees what it merged with. To verify both
        // ended up on disk, hydrate a fresh instance.
        var observer = new RecentContextsService(_path);
        await observer.LoadAsync();
        observer.Current.Select(i => i.Identity.CanonicalRepoPath)
            .Should().BeEquivalentTo(new[]
            {
                ContextIdentityFactory.CanonicalizeRepoPath(@"C:\repos\one"),
                ContextIdentityFactory.CanonicalizeRepoPath(@"C:\repos\two"),
            });
    }

    // -- Phase 7: PR-review feature ----------------------------------

    [Fact]
    public async Task RecordLaunchAsync_PrMode_TwoPrsSharingIdentitySha_AreDistinctRows()
    {
        // Two different PRs that happen to share the same (merge-base,
        // head) SHA pair must NOT collapse to one row — the user needs
        // to be able to distinguish them in the dropdown. PR-mode dedup
        // folds the PR number into the key.
        var svc = new RecentContextsService(_path);
        var left = new DiffSide.CommitIsh("abc123");
        var right = new DiffSide.CommitIsh("def456");
        var id = ContextIdentityFactory.Create(@"C:\repos\foo", left, right);
        var pr1 = new PullRequestRef("github.com", "owner", "repo", 1);
        var pr2 = new PullRequestRef("github.com", "owner", "repo", 2);

        await svc.RecordLaunchAsync(id, left, right, pr1);
        await svc.RecordLaunchAsync(id, left, right, pr2);

        svc.Current.Should().HaveCount(2);
        svc.Current.Select(i => ((PullRequestRef)i.Review!).Number).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async Task RecordLaunchAsync_PrMode_SamePr_DedupsAndMovesToFront()
    {
        var svc = new RecentContextsService(_path);
        var (id, left, right) = MakeIdentity(@"C:\repos\foo", "abc");
        var pr = new PullRequestRef("github.com", "owner", "repo", 7);

        await svc.RecordLaunchAsync(id, left, right, pr);
        await svc.RecordLaunchAsync(id, left, right, pr);

        svc.Current.Should().ContainSingle()
            .Which.Review.Should().BeOfType<PullRequestRef>()
            .Which.Number.Should().Be(7);
    }

    [Fact]
    public async Task RecordLaunchAsync_PrMode_AndLocalLaunch_SameIdentity_StayDistinct()
    {
        // A non-PR row and a PR row that happen to share ContextIdentity
        // are distinct conceptually: the user might want both "view as
        // local commits" and "view as PR" entries for the same SHA pair.
        var svc = new RecentContextsService(_path);
        var (id, left, right) = MakeIdentity(@"C:\repos\foo", "abc");
        var pr = new PullRequestRef("github.com", "owner", "repo", 7);

        await svc.RecordLaunchAsync(id, left, right);              // local
        await svc.RecordLaunchAsync(id, left, right, pr);           // PR-mode

        svc.Current.Should().HaveCount(2);
        svc.Current.Where(i => i.Review is null).Should().HaveCount(1);
        svc.Current.Where(i => i.Review is not null).Should().HaveCount(1);
    }

    [Fact]
    public async Task RecordLaunchAsync_PrMode_PropagatesPullRequestToSnapshot()
    {
        var svc = new RecentContextsService(_path);
        var (id, left, right) = MakeIdentity(@"C:\repos\foo", "abc");
        var pr = new PullRequestRef("github.com", "geevensingh", "diffviewer", 42);

        await svc.RecordLaunchAsync(id, left, right, pr);

        svc.Current.Should().ContainSingle()
            .Which.Review.Should().Be(pr);
    }

    [Fact]
    public async Task RecordLaunchAsync_PrMode_PersistsAcrossInstances()
    {
        var (id, left, right) = MakeIdentity(@"C:\repos\foo", "abc");
        var pr = new PullRequestRef("github.com", "owner", "repo", 99);

        var writer = new RecentContextsService(_path);
        await writer.RecordLaunchAsync(id, left, right, pr);

        var reader = new RecentContextsService(_path);
        await reader.LoadAsync();
        reader.Current.Should().ContainSingle()
            .Which.Review.Should().Be(pr);
    }

    private static RecentLaunchContext MakeContext(string repo, string leftRef, DateTimeOffset stamp)
    {
        var (id, left, right) = MakeIdentity(repo, leftRef);
        return new RecentLaunchContext(id, left, right, stamp);
    }

    private static (ContextIdentity id, DiffSide left, DiffSide right) MakeIdentity(string repo, string leftRef)
    {
        var left = new DiffSide.CommitIsh(leftRef);
        var right = new DiffSide.WorkingTree();
        return (ContextIdentityFactory.Create(repo, left, right), left, right);
    }
}
