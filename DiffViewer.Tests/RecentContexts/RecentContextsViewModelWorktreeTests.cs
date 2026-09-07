using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.RecentContexts;

/// <summary>
/// The context bar's worktree switcher: re-opens the current comparison
/// rooted at a different worktree of the same repository, carrying both
/// sides over unchanged.
/// </summary>
public class RecentContextsViewModelWorktreeTests
{
    private const string CurrentRepo = @"C:\repos\diffviewer";
    private const string OtherWorktree = @"C:\worktrees\feature-x";

    [Fact]
    public void WorktreeSwitch_WithNoEnumeratorWired_IsDisabled()
    {
        var vm = MakeViewModel(new FakeSwitcher(), worktreeEnumerator: null);

        vm.IsWorktreeSwitchEnabled.Should().BeFalse();
        vm.WorktreePicker.Should().BeNull();
    }

    [Fact]
    public void WorktreeSwitch_WithNoSwitcherWired_IsDisabled()
    {
        var vm = MakeViewModel(switcher: null, worktreeEnumerator: new StubWorktreeEnumerator());

        vm.IsWorktreeSwitchEnabled.Should().BeFalse();
        vm.WorktreePicker.Should().BeNull();
    }

    [Fact]
    public void WorktreeSwitch_WhenFullyWired_IsEnabledAndPointedAtTheCurrentRepo()
    {
        var vm = MakeViewModel(new FakeSwitcher(), new StubWorktreeEnumerator());

        vm.IsWorktreeSwitchEnabled.Should().BeTrue();
        vm.WorktreePicker!.CanonicalRepoPath.Should().Be(CurrentRepo);
    }

    [Fact]
    public async Task PickingAnotherWorktree_RelaunchesTheSameSidesAtThatPath()
    {
        var switcher = new FakeSwitcher();
        var vm = MakeViewModel(switcher, new StubWorktreeEnumerator());

        vm.WorktreePicker!.PickWorktreeCommand.Execute(Linked(OtherWorktree));
        await switcher.WaitForSwitchAsync();

        switcher.SwitchToCalls.Should().Be(1);
        var local = switcher.LastLaunched.Should().BeOfType<DiffLaunchSource.Local>().Subject;
        local.Parsed.RepoPath.Should().Be(OtherWorktree);
        // Both sides carry over verbatim - the point is to ask the same
        // question of a different checkout.
        local.Parsed.Left.Should().Be(new DiffSide.CommitIsh("HEAD"));
        local.Parsed.Right.Should().Be(new DiffSide.WorkingTree());
    }

    [Fact]
    public void PickingTheWorktreeAlreadyOpen_DoesNotSwitch()
    {
        var switcher = new FakeSwitcher();
        var vm = MakeViewModel(switcher, new StubWorktreeEnumerator());

        vm.WorktreePicker!.PickWorktreeCommand.Execute(Linked(CurrentRepo));

        switcher.SwitchToCalls.Should().Be(0);
    }

    [Fact]
    public void PickingTheCurrentWorktree_IsPathCaseInsensitive()
    {
        var switcher = new FakeSwitcher();
        var vm = MakeViewModel(switcher, new StubWorktreeEnumerator());

        vm.WorktreePicker!.PickWorktreeCommand.Execute(Linked(CurrentRepo.ToUpperInvariant()));

        switcher.SwitchToCalls.Should().Be(0);
    }

    [Fact]
    public void PickingAMissingWorktree_DoesNotSwitch()
    {
        var switcher = new FakeSwitcher();
        var vm = MakeViewModel(switcher, new StubWorktreeEnumerator());

        vm.WorktreePicker!.PickWorktreeCommand.Execute(Linked(OtherWorktree) with { IsMissing = true });

        switcher.SwitchToCalls.Should().Be(0);
    }

    private static RecentContextsViewModel MakeViewModel(
        IContextSwitcher? switcher,
        IGitWorktreeEnumerator? worktreeEnumerator)
    {
        var left = new DiffSide.CommitIsh("HEAD");
        var right = new DiffSide.WorkingTree();
        var identity = ContextIdentityFactory.Create(CurrentRepo, left, right);
        return new RecentContextsViewModel(
            new NullRecentContextsService(),
            switcher,
            identity,
            newDiffDialogHost: null,
            worktreeEnumerator: worktreeEnumerator);
    }

    private static WorktreeEntry Linked(string workingDirectory) => new(
        Name: "feature-x",
        WorkingDirectory: workingDirectory,
        HeadFriendlyName: "feature-x",
        IsMain: false,
        IsCurrent: false,
        IsLocked: false,
        IsMissing: false);

    private sealed class FakeSwitcher : IContextSwitcher
    {
        private readonly TaskCompletionSource _switched =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SwitchToCalls { get; private set; }
        public DiffLaunchSource? LastLaunched { get; private set; }
        public bool IsSwitching => false;

#pragma warning disable CS0067 // Interface-required; not raised by this fake.
        public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067

        public Task<bool> SwitchToRecentAsync(RecentLaunchContext recent, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> SwitchToAsync(DiffLaunchSource source, CancellationToken ct = default)
        {
            SwitchToCalls++;
            LastLaunched = source;
            _switched.TrySetResult();
            return Task.FromResult(true);
        }

        /// <summary>The picker's write-back is fire-and-forget, so tests
        /// that assert on a switch must wait for it to actually land.</summary>
        public Task WaitForSwitchAsync() => _switched.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
