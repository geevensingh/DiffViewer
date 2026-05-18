using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.RecentContexts;

public class RecentContextsViewModelTests
{
    [Fact]
    public void Items_ProjectsServiceCurrent()
    {
        var a = MakeContext(@"C:\repos\a", "main");
        var b = MakeContext(@"C:\repos\b", "feature/x");
        var svc = new FakeService(a, b);

        using var vm = new RecentContextsViewModel(svc, switcher: null, currentIdentity: null);

        vm.Items.Should().HaveCount(2);
        vm.ItemViews.Should().HaveCount(2);
        vm.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void IsEmpty_TrueWhenServiceCurrentIsEmpty()
    {
        var svc = new FakeService();
        using var vm = new RecentContextsViewModel(svc, null, null);
        vm.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void SelectedItem_GetterReturnsNullWhenCurrentIdentityIsNull()
    {
        var svc = new FakeService(MakeContext(@"C:\a", "main"));
        using var vm = new RecentContextsViewModel(svc, null, currentIdentity: null);
        vm.SelectedItem.Should().BeNull();
    }

    [Fact]
    public void SelectedItem_GetterReturnsItemMatchingCurrentIdentity()
    {
        var a = MakeContext(@"C:\repos\a", "main");
        var b = MakeContext(@"C:\repos\b", "feature/x");
        var svc = new FakeService(a, b);

        using var vm = new RecentContextsViewModel(svc, null, currentIdentity: b.Identity);

        vm.SelectedItem.Should().NotBeNull();
        vm.SelectedItem!.Source.Identity.Should().Be(b.Identity);
    }

    [Fact]
    public void SelectedItem_SetterToCurrentIdentity_DoesNotCallSwitcher()
    {
        var a = MakeContext(@"C:\repos\a", "main");
        var b = MakeContext(@"C:\repos\b", "feature/x");
        var svc = new FakeService(a, b);
        var switcher = new FakeSwitcher();

        using var vm = new RecentContextsViewModel(svc, switcher, currentIdentity: a.Identity);

        // Picking the row that matches the current identity must be a no-op.
        vm.SelectedItem = new RecentContextItem(a);

        switcher.SwitchCalls.Should().Be(0);
    }

    [Fact]
    public async Task SelectedItem_SetterToDifferentIdentity_TriggersSwitch()
    {
        var a = MakeContext(@"C:\repos\a", "main");
        var b = MakeContext(@"C:\repos\b", "feature/x");
        var svc = new FakeService(a, b);
        var switcher = new FakeSwitcher();

        using var vm = new RecentContextsViewModel(svc, switcher, currentIdentity: a.Identity);

        vm.SelectedItem = new RecentContextItem(b);

        // The setter is fire-and-forget; give the dispatcher a tick.
        await Task.Delay(50);

        switcher.SwitchCalls.Should().Be(1);
        switcher.LastSwitched!.Identity.Should().Be(b.Identity);
    }

    [Fact]
    public void SelectedItem_SetterWithNullSwitcher_NoOps()
    {
        var a = MakeContext(@"C:\repos\a", "main");
        var b = MakeContext(@"C:\repos\b", "feature/x");
        var svc = new FakeService(a, b);

        using var vm = new RecentContextsViewModel(svc, switcher: null, currentIdentity: a.Identity);

        // Should not throw, should not loop.
        vm.SelectedItem = new RecentContextItem(b);
    }

    [Fact]
    public void IsEnabled_TracksSwitcherIsSwitching()
    {
        var svc = new FakeService(MakeContext(@"C:\a", "main"));
        var switcher = new FakeSwitcher();

        using var vm = new RecentContextsViewModel(svc, switcher, currentIdentity: null);
        vm.IsEnabled.Should().BeTrue();

        switcher.RaiseIsSwitching(true);
        vm.IsEnabled.Should().BeFalse();

        switcher.RaiseIsSwitching(false);
        vm.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void OnRecentsChanged_RaisesItemsAndSelectedItemPropertyChanges()
    {
        var svc = new FakeService(MakeContext(@"C:\a", "main"));
        using var vm = new RecentContextsViewModel(svc, null, null);

        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        svc.RaiseChanged();

        changes.Should().Contain(nameof(RecentContextsViewModel.Items));
        changes.Should().Contain(nameof(RecentContextsViewModel.ItemViews));
        changes.Should().Contain(nameof(RecentContextsViewModel.SelectedItem));
        changes.Should().Contain(nameof(RecentContextsViewModel.IsEmpty));
    }

    [Fact]
    public void Dispose_UnsubscribesFromService_AndSwitcher()
    {
        var svc = new FakeService(MakeContext(@"C:\a", "main"));
        var switcher = new FakeSwitcher();
        var vm = new RecentContextsViewModel(svc, switcher, null);

        var changes = 0;
        vm.PropertyChanged += (_, _) => changes++;

        vm.Dispose();
        svc.RaiseChanged();
        switcher.RaiseIsSwitching(true);

        // After Dispose, neither service.Changed nor switcher.PropertyChanged
        // should propagate into the VM.
        changes.Should().Be(0);
    }

    [Fact]
    public void IsNewDiffEnabled_FalseWhenHostOrSwitcherMissing()
    {
        var svc = new FakeService();

        // No host, no switcher.
        using (var vm = new RecentContextsViewModel(svc, switcher: null, currentIdentity: null))
        {
            vm.IsNewDiffEnabled.Should().BeFalse();
            vm.NewDiffCommand.CanExecute(null).Should().BeFalse();
        }

        // Switcher but no host.
        using (var vm = new RecentContextsViewModel(svc, new FakeSwitcher(), currentIdentity: null))
        {
            vm.IsNewDiffEnabled.Should().BeFalse();
        }

        // Host but no switcher — clicking the button without anywhere
        // to dispatch is still pointless, so the command stays gated.
        using (var vm = new RecentContextsViewModel(svc, switcher: null, currentIdentity: null, new FakeNewDiffDialogHost()))
        {
            vm.IsNewDiffEnabled.Should().BeFalse();
        }
    }

    [Fact]
    public void IsNewDiffEnabled_TrueWhenHostAndSwitcherProvided()
    {
        var svc = new FakeService();
        using var vm = new RecentContextsViewModel(svc, new FakeSwitcher(), currentIdentity: null, new FakeNewDiffDialogHost());

        vm.IsNewDiffEnabled.Should().BeTrue();
        vm.NewDiffCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task NewDiffCommand_HostReturnsNull_DoesNotCallSwitcher()
    {
        var svc = new FakeService(MakeContext(@"C:\a", "main"));
        var switcher = new FakeSwitcher();
        var host = new FakeNewDiffDialogHost { NextResult = null };

        using var vm = new RecentContextsViewModel(svc, switcher, currentIdentity: null, host);

        await vm.NewDiffCommand.ExecuteAsync(null);

        host.Calls.Should().Be(1);
        switcher.SwitchToCalls.Should().Be(0);
    }

    [Fact]
    public async Task NewDiffCommand_HostReturnsSource_DispatchesToSwitcher()
    {
        var svc = new FakeService();
        var switcher = new FakeSwitcher();
        var pickedSource = new DiffViewer.Models.DiffLaunchSource.Local(
            new DiffViewer.Models.ParsedCommandLine(
                @"C:\repos\foo",
                new DiffSide.CommitIsh("HEAD"),
                new DiffSide.WorkingTree()));
        var host = new FakeNewDiffDialogHost { NextResult = pickedSource };

        using var vm = new RecentContextsViewModel(svc, switcher, currentIdentity: null, host);

        await vm.NewDiffCommand.ExecuteAsync(null);

        host.Calls.Should().Be(1);
        switcher.SwitchToCalls.Should().Be(1);
        switcher.LastLaunched.Should().BeSameAs(pickedSource);
    }

    [Fact]
    public async Task NewDiffCommand_PassesCurrentRepoPathAsPrefill()
    {
        var c = MakeContext(@"C:\repos\foo", "main");
        var svc = new FakeService(c);
        var switcher = new FakeSwitcher();
        var host = new FakeNewDiffDialogHost { NextResult = null };

        using var vm = new RecentContextsViewModel(svc, switcher, currentIdentity: c.Identity, host);

        await vm.NewDiffCommand.ExecuteAsync(null);

        host.LastPrefilledRepoPath.Should().Be(c.Identity.CanonicalRepoPath);
    }

    [Fact]
    public async Task NewDiffCommand_NoCurrentIdentity_PassesNullPrefill()
    {
        var svc = new FakeService();
        var switcher = new FakeSwitcher();
        var host = new FakeNewDiffDialogHost { NextResult = null };

        using var vm = new RecentContextsViewModel(svc, switcher, currentIdentity: null, host);

        await vm.NewDiffCommand.ExecuteAsync(null);

        host.LastPrefilledRepoPath.Should().BeNull();
    }

    private static RecentLaunchContext MakeContext(string repoPath, string commitRef)
    {
        var canonical = ContextIdentityFactory.CanonicalizeRepoPath(repoPath);
        var left = new DiffSide.WorkingTree();
        var right = new DiffSide.CommitIsh(commitRef);
        var identity = new ContextIdentity(canonical, left, right);
        return new RecentLaunchContext(identity, left, right, DateTimeOffset.UtcNow);
    }

    private sealed class FakeService : IRecentContextsService
    {
        private readonly List<RecentLaunchContext> _items;
        public FakeService(params RecentLaunchContext[] items) { _items = new List<RecentLaunchContext>(items); }
        public IReadOnlyList<RecentLaunchContext> Current => _items;
        public event EventHandler? Changed;
        public Task RecordLaunchAsync(ContextIdentity identity, DiffSide leftDisplay, DiffSide rightDisplay, DiffViewer.Models.IReviewRef? review = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(ContextIdentity identity, CancellationToken ct = default) => Task.CompletedTask;
        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeSwitcher : IContextSwitcher
    {
        public int SwitchCalls { get; private set; }
        public RecentLaunchContext? LastSwitched { get; private set; }
        public int SwitchToCalls { get; private set; }
        public DiffViewer.Models.DiffLaunchSource? LastLaunched { get; private set; }
        public bool IsSwitching { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public Task<bool> SwitchToRecentAsync(RecentLaunchContext recent, CancellationToken ct = default)
        {
            SwitchCalls++;
            LastSwitched = recent;
            return Task.FromResult(true);
        }
        public Task<bool> SwitchToAsync(DiffViewer.Models.DiffLaunchSource source, CancellationToken ct = default)
        {
            SwitchToCalls++;
            LastLaunched = source;
            return Task.FromResult(true);
        }
        public void RaiseIsSwitching(bool value)
        {
            IsSwitching = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSwitching)));
        }
    }

    private sealed class FakeNewDiffDialogHost : INewDiffDialogHost
    {
        public DiffViewer.Models.DiffLaunchSource? NextResult { get; set; }
        public int Calls { get; private set; }
        public string? LastPrefilledRepoPath { get; private set; }
        public Task<DiffViewer.Models.DiffLaunchSource?> ShowAsync(
            string? prefilledRepoPath, CancellationToken ct = default)
        {
            Calls++;
            LastPrefilledRepoPath = prefilledRepoPath;
            return Task.FromResult(NextResult);
        }
    }
}
