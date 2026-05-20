using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public class RefPickerViewModelTests
{
    [Fact]
    public void IsEnabled_RequiresRepoPath()
    {
        var sut = NewPicker(repoPath: null);
        sut.IsEnabled.Should().BeFalse();

        sut.CanonicalRepoPath = @"C:\repos\foo";
        sut.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureLoadedAsync_PopulatesAllGroups()
    {
        var enumerator = new FakeEnumerator
        {
            Result = new RefEnumerationResult(
                LocalBranches: new[]
                {
                    new RefEntry("feature/x", "aaaaaaa0000000000000000000000000000000aa", "aaaaaaa"),
                    new RefEntry("master",    "bbbbbbb0000000000000000000000000000000bb", "bbbbbbb"),
                },
                RemoteBranches: new[]
                {
                    new RefEntry("origin/master", "bbbbbbb0000000000000000000000000000000bb", "bbbbbbb"),
                },
                Tags: new[]
                {
                    new RefEntry("v0.1.0", "ccccccc0000000000000000000000000000000cc", "ccccccc"),
                }),
        };

        var sut = NewPicker(@"C:\repos\foo", enumerator: enumerator);
        await sut.EnsureLoadedAsync();

        sut.IsLoaded.Should().BeTrue();
        sut.IsLoading.Should().BeFalse();
        sut.VisibleLocalBranches.Select(b => b.FriendlyName).Should().BeEquivalentTo("feature/x", "master");
        sut.VisibleRemoteBranches.Select(b => b.FriendlyName).Should().BeEquivalentTo("origin/master");
        sut.VisibleTags.Select(b => b.FriendlyName).Should().BeEquivalentTo("v0.1.0");
        sut.HasAnyVisibleRefs.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureLoadedAsync_IsIdempotent()
    {
        var enumerator = new FakeEnumerator { Result = SmallResult() };
        var sut = NewPicker(@"C:\repos\foo", enumerator: enumerator);

        await sut.EnsureLoadedAsync();
        await sut.EnsureLoadedAsync();
        await sut.EnsureLoadedAsync();

        enumerator.EnumerateCalls.Should().Be(1);
    }

    [Fact]
    public async Task EnsureLoadedAsync_NoOpWhenRepoPathBlank()
    {
        var enumerator = new FakeEnumerator { Result = SmallResult() };
        var sut = NewPicker(repoPath: null, enumerator: enumerator);

        await sut.EnsureLoadedAsync();

        sut.IsLoaded.Should().BeFalse();
        enumerator.EnumerateCalls.Should().Be(0);
    }

    [Fact]
    public async Task ChangingRepoPath_ClearsCachedSnapshotsAndForcesReload()
    {
        var enumerator = new FakeEnumerator { Result = SmallResult() };
        var sut = NewPicker(@"C:\repos\foo", enumerator: enumerator);

        await sut.EnsureLoadedAsync();
        sut.IsLoaded.Should().BeTrue();

        sut.CanonicalRepoPath = @"C:\repos\bar";

        sut.IsLoaded.Should().BeFalse();
        sut.VisibleLocalBranches.Should().BeEmpty();

        await sut.EnsureLoadedAsync();
        enumerator.EnumerateCalls.Should().Be(2);
    }

    [Fact]
    public async Task Filter_NarrowsAllGroupsCaseInsensitively()
    {
        var enumerator = new FakeEnumerator
        {
            Result = new RefEnumerationResult(
                LocalBranches: new[]
                {
                    new RefEntry("feature/Xray", "1", "1"),
                    new RefEntry("master",       "2", "2"),
                },
                RemoteBranches: new[]
                {
                    new RefEntry("origin/feature/xenon", "3", "3"),
                    new RefEntry("origin/master",        "4", "4"),
                },
                Tags: new[]
                {
                    new RefEntry("v0.1.0", "5", "5"),
                    new RefEntry("xmas",   "6", "6"),
                }),
        };
        var sut = NewPicker(@"C:\repos\foo", enumerator: enumerator);
        await sut.EnsureLoadedAsync();

        sut.Filter = "X";  // upper-case X; should match xenon, xmas, Xray

        sut.VisibleLocalBranches.Select(b => b.FriendlyName).Should().BeEquivalentTo("feature/Xray");
        sut.VisibleRemoteBranches.Select(b => b.FriendlyName).Should().BeEquivalentTo("origin/feature/xenon");
        sut.VisibleTags.Select(b => b.FriendlyName).Should().BeEquivalentTo("xmas");
    }

    [Fact]
    public async Task RecentRefs_DerivedFromRecentContextsForSameRepo()
    {
        var repo = @"C:\repos\foo";
        var otherRepo = @"C:\repos\bar";
        var recents = new FakeRecents
        {
            // Note: RecentContextsService stores entries in MRU order.
            Items = new[]
            {
                MakeRecent(repo,      left: "feature/x", right: "master"),
                MakeRecent(otherRepo, left: "anything",  right: "else"),    // wrong repo → ignored
                MakeRecent(repo,      left: "HEAD",      right: "master"),  // master = dup
                MakeRecent(repo,      left: "v0.1.0",    right: "feature/x"), // feature/x = dup
            },
        };

        var sut = NewPicker(repo, enumerator: new FakeEnumerator { Result = SmallResult() }, recents: recents);
        await sut.EnsureLoadedAsync();

        // MRU order, deduped, capped at MaxRecentRefs (5).
        sut.VisibleRecentRefs.Should().ContainInOrder("feature/x", "master", "HEAD", "v0.1.0");
    }

    [Fact]
    public async Task RecentRefs_CappedAtMaxRecentRefs()
    {
        var repo = @"C:\repos\foo";
        var recents = new FakeRecents
        {
            Items = new[]
            {
                MakeRecent(repo, "r1", "r2"),
                MakeRecent(repo, "r3", "r4"),
                MakeRecent(repo, "r5", "r6"),  // would be 6th distinct ref
            },
        };

        var sut = NewPicker(repo, enumerator: new FakeEnumerator { Result = SmallResult() }, recents: recents);
        await sut.EnsureLoadedAsync();

        sut.VisibleRecentRefs.Should().HaveCount(RefPickerViewModel.MaxRecentRefs);
        sut.VisibleRecentRefs.Should().ContainInOrder("r1", "r2", "r3", "r4", "r5");
    }

    [Fact]
    public async Task RecentRefs_IgnoresWorkingTreeSides()
    {
        var repo = @"C:\repos\foo";
        var recents = new FakeRecents
        {
            Items = new[]
            {
                new RecentLaunchContext(
                    new ContextIdentity(repo,
                        new DiffSide.CommitIsh("feature/x"),
                        new DiffSide.WorkingTree()),
                    new DiffSide.CommitIsh("feature/x"),
                    new DiffSide.WorkingTree(),
                    DateTimeOffset.UtcNow),
            },
        };

        var sut = NewPicker(repo, enumerator: new FakeEnumerator { Result = SmallResult() }, recents: recents);
        await sut.EnsureLoadedAsync();

        // The WorkingTree side must not appear as a ref.
        sut.VisibleRecentRefs.Should().ContainSingle().Which.Should().Be("feature/x");
    }

    [Fact]
    public void PickRef_WritesToCallback()
    {
        string? written = null;
        var sut = NewPicker(@"C:\repos\foo", writeBack: s => written = s);

        sut.PickRefCommand.Execute("feature/x");

        written.Should().Be("feature/x");
    }

    [Fact]
    public void PickRef_IgnoresEmptyOrNullInput()
    {
        var calls = 0;
        var sut = NewPicker(@"C:\repos\foo", writeBack: _ => calls++);

        sut.PickRefCommand.Execute(null);
        sut.PickRefCommand.Execute("");
        sut.PickRefCommand.Execute("   ");

        calls.Should().Be(0);
    }

    [Fact]
    public void UseMergeBase_OnSuccess_WritesShaAndSetsComputedMergeBase()
    {
        var enumerator = new FakeEnumerator
        {
            Result = SmallResult(),
            MergeBaseLookup = (a, b) => a == "branch-a" && b == "branch-b"
                ? "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef"
                : null,
        };
        string? written = null;
        var sut = NewPicker(@"C:\repos\foo",
            enumerator: enumerator,
            writeBack: s => written = s);
        sut.MergeBaseRefA = "branch-a";
        sut.MergeBaseRefB = "branch-b";

        sut.UseMergeBaseCommand.Execute(null);

        sut.MergeBaseError.Should().BeNull();
        sut.ComputedMergeBase.Should().Be("deadbeefdeadbeefdeadbeefdeadbeefdeadbeef");
        written.Should().Be("deadbeefdeadbeefdeadbeefdeadbeefdeadbeef");
    }

    [Fact]
    public void UseMergeBase_OnUnrelatedHistories_SetsErrorAndDoesNotWrite()
    {
        var enumerator = new FakeEnumerator
        {
            Result = SmallResult(),
            MergeBaseLookup = (_, _) => null,
        };
        var calls = 0;
        var sut = NewPicker(@"C:\repos\foo",
            enumerator: enumerator,
            writeBack: _ => calls++);
        sut.MergeBaseRefA = "branch-a";
        sut.MergeBaseRefB = "branch-b";

        sut.UseMergeBaseCommand.Execute(null);

        sut.MergeBaseError.Should().Contain("No common ancestor");
        sut.MergeBaseError.Should().Contain("branch-a").And.Contain("branch-b");
        sut.ComputedMergeBase.Should().BeNull();
        calls.Should().Be(0);
    }

    [Fact]
    public void UseMergeBase_OnMissingRefs_SetsErrorAndDoesNotInvokeLookup()
    {
        var enumerator = new FakeEnumerator { Result = SmallResult() };
        var sut = NewPicker(@"C:\repos\foo", enumerator: enumerator);
        sut.MergeBaseRefA = "branch-a";
        sut.MergeBaseRefB = "";

        sut.UseMergeBaseCommand.Execute(null);

        sut.MergeBaseError.Should().Contain("Fill both refs");
        enumerator.MergeBaseCalls.Should().Be(0);
    }

    [Fact]
    public void UseMergeBase_OnMissingRepoPath_SetsErrorAndDoesNotInvokeLookup()
    {
        var enumerator = new FakeEnumerator { Result = SmallResult() };
        var sut = NewPicker(repoPath: null, enumerator: enumerator);
        sut.MergeBaseRefA = "branch-a";
        sut.MergeBaseRefB = "branch-b";

        sut.UseMergeBaseCommand.Execute(null);

        sut.MergeBaseError.Should().Contain("Pick a valid repository path");
        enumerator.MergeBaseCalls.Should().Be(0);
    }

    [Fact]
    public async Task EnsureLoadedAsync_RepoChangedMidFlight_DropsStaleResults()
    {
        // The enumerate runner blocks until we release a TCS, so we
        // can re-point the picker between "enumerate started" and
        // "enumerate returned" and verify the stale result is dropped.
        var firstGate = new TaskCompletionSource<RefEnumerationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResult = new RefEnumerationResult(
            new[] { new RefEntry("first-branch", "11", "11") },
            Array.Empty<RefEntry>(),
            Array.Empty<RefEntry>());

        var enumerator = new FakeEnumerator { Result = SmallResult() };
        Func<Func<RefEnumerationResult>, Task<RefEnumerationResult>> runner = work =>
        {
            // First call → wait for the gate so the test can advance state.
            // Subsequent calls → run synchronously.
            if (!firstGate.Task.IsCompleted)
            {
                return firstGate.Task;
            }
            return Task.FromResult(work());
        };

        var sut = new RefPickerViewModel(
            enumerator,
            new FakeRecents { Items = Array.Empty<RecentLaunchContext>() },
            writeBack: _ => { },
            initialCanonicalRepoPath: @"C:\repos\foo",
            enumerateRunner: runner);

        var firstLoad = sut.EnsureLoadedAsync();

        // Re-point the picker; this clears IsLoaded and snapshots.
        sut.CanonicalRepoPath = @"C:\repos\bar";

        // Now release the first enumerate — its result should be
        // ignored because the canonical path has changed.
        firstGate.SetResult(firstResult);
        await firstLoad;

        sut.IsLoaded.Should().BeFalse();
        sut.VisibleLocalBranches.Should().BeEmpty();
    }

    // ---- helpers -------------------------------------------------------

    private static RefPickerViewModel NewPicker(
        string? repoPath = @"C:\repos\foo",
        IGitRefEnumerator? enumerator = null,
        IRecentContextsService? recents = null,
        Action<string>? writeBack = null)
    {
        return new RefPickerViewModel(
            enumerator ?? new FakeEnumerator { Result = SmallResult() },
            recents ?? new FakeRecents { Items = Array.Empty<RecentLaunchContext>() },
            writeBack ?? (_ => { }),
            initialCanonicalRepoPath: repoPath,
            // Run synchronously so tests don't have to round-trip the
            // thread pool just to observe loaded state.
            enumerateRunner: work => Task.FromResult(work()));
    }

    private static RefEnumerationResult SmallResult() => new(
        new[] { new RefEntry("master", "00", "00") },
        Array.Empty<RefEntry>(),
        Array.Empty<RefEntry>());

    private static RecentLaunchContext MakeRecent(string repo, string left, string right)
    {
        var identity = new ContextIdentity(repo,
            new DiffSide.CommitIsh(left),
            new DiffSide.CommitIsh(right));
        return new RecentLaunchContext(
            identity,
            new DiffSide.CommitIsh(left),
            new DiffSide.CommitIsh(right),
            DateTimeOffset.UtcNow);
    }

    private sealed class FakeEnumerator : IGitRefEnumerator
    {
        public RefEnumerationResult Result { get; set; } = RefEnumerationResult.Empty;
        public Func<string, string, string?>? MergeBaseLookup { get; set; }
        public int EnumerateCalls { get; private set; }
        public int MergeBaseCalls { get; private set; }

        public RefEnumerationResult Enumerate(string canonicalRepoPath)
        {
            EnumerateCalls++;
            return Result;
        }

        public string? TryComputeMergeBase(string canonicalRepoPath, string refA, string refB)
        {
            MergeBaseCalls++;
            return MergeBaseLookup?.Invoke(refA, refB);
        }

        public string? TryGetDefaultRemoteBranch(string canonicalRepoPath) => null;
    }

    private sealed class FakeRecents : IRecentContextsService
    {
        public IReadOnlyList<RecentLaunchContext> Items { get; set; } = Array.Empty<RecentLaunchContext>();
        public IReadOnlyList<RecentLaunchContext> Current => Items;

#pragma warning disable CS0067
        public event EventHandler? Changed;
#pragma warning restore CS0067

        public Task RecordLaunchAsync(ContextIdentity identity, DiffSide leftDisplay, DiffSide rightDisplay, IReviewRef? review = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RemoveAsync(ContextIdentity identity, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
