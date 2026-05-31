using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public sealed class UpdateNotificationViewModelTests
{
    [Fact]
    public async Task StartAsync_WhenDisabled_DoesNotCheck()
    {
        var fake = new FakeUpdateService();
        var sut = NewVm(fake, () => AutoUpdateMode.Disabled);

        await sut.StartAsync(CancellationToken.None);

        fake.CheckCalls.Should().Be(0);
        sut.IsBannerVisible.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_WhenAutomatic_ChecksDownloadsAndQueuesSilently_ThenShowsBanner()
    {
        var available = NewAvailable("1.5.0");
        var fake = new FakeUpdateService { CheckResult = available };
        var sut = NewVm(fake, () => AutoUpdateMode.Automatic);

        await sut.StartAsync(CancellationToken.None);

        fake.CheckCalls.Should().Be(1);
        fake.DownloadCalls.Should().ContainSingle().Which.Should().BeSameAs(available);
        fake.ApplyOnNextLaunchCalls.Should().ContainSingle().Which.Should().BeSameAs(available);
        sut.IsBannerVisible.Should().BeTrue();
        sut.ShowInstallButton.Should().BeFalse();
        sut.StatusText.Should().Contain("1.5.0").And.Contain("next launch");
    }

    [Fact]
    public async Task StartAsync_WhenNotifyOnly_ChecksOnly_ThenShowsBannerWithInstallButton()
    {
        var available = NewAvailable("2.0.0");
        var fake = new FakeUpdateService { CheckResult = available };
        var sut = NewVm(fake, () => AutoUpdateMode.NotifyOnly);

        await sut.StartAsync(CancellationToken.None);

        fake.CheckCalls.Should().Be(1);
        fake.DownloadCalls.Should().BeEmpty();
        fake.ApplyOnNextLaunchCalls.Should().BeEmpty();
        sut.IsBannerVisible.Should().BeTrue();
        sut.ShowInstallButton.Should().BeTrue();
        sut.StatusText.Should().Contain("2.0.0").And.NotContain("next launch");
    }

    [Fact]
    public async Task StartAsync_WhenNoUpdateAvailable_LeavesBannerHidden()
    {
        var fake = new FakeUpdateService { CheckResult = UpdateCheckResult.NoUpdateAvailable };
        var sut = NewVm(fake, () => AutoUpdateMode.NotifyOnly);

        await sut.StartAsync(CancellationToken.None);

        fake.CheckCalls.Should().Be(1);
        fake.DownloadCalls.Should().BeEmpty();
        sut.IsBannerVisible.Should().BeFalse();
        sut.ShowInstallButton.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_WhenDetectedVersionMatchesSkipped_LeavesBannerHidden()
    {
        // A previously-skipped version should silently consume the
        // detection: no banner, no download, no apply queue. The
        // periodic re-check will still try again next interval (in
        // case the user changes their mind).
        var available = NewAvailable("1.5.0");
        var fake = new FakeUpdateService { CheckResult = available };
        var sut = NewVm(fake, () => AutoUpdateMode.NotifyOnly, getSkipped: () => "1.5.0");

        await sut.StartAsync(CancellationToken.None);

        fake.CheckCalls.Should().Be(1);
        fake.DownloadCalls.Should().BeEmpty();
        sut.IsBannerVisible.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_WhenDetectedVersionDiffersFromSkipped_ShowsBanner()
    {
        // A previously-skipped older version should NOT suppress a
        // newer detection — Skip is per-version, not "skip everything
        // forever".
        var available = NewAvailable("2.0.0");
        var fake = new FakeUpdateService { CheckResult = available };
        var sut = NewVm(fake, () => AutoUpdateMode.NotifyOnly, getSkipped: () => "1.5.0");

        await sut.StartAsync(CancellationToken.None);

        sut.IsBannerVisible.Should().BeTrue();
        sut.StatusText.Should().Contain("2.0.0");
    }

    [Fact]
    public async Task Install_AfterNotifyOnlyCheck_DownloadsAndQueues_HidesInstallButton()
    {
        var available = NewAvailable("3.1.2");
        var fake = new FakeUpdateService { CheckResult = available };
        var sut = NewVm(fake, () => AutoUpdateMode.NotifyOnly);
        await sut.StartAsync(CancellationToken.None);

        await sut.InstallCommand.ExecuteAsync(null);

        fake.DownloadCalls.Should().ContainSingle().Which.Should().BeSameAs(available);
        fake.ApplyOnNextLaunchCalls.Should().ContainSingle().Which.Should().BeSameAs(available);
        sut.ShowInstallButton.Should().BeFalse();
        sut.IsBannerVisible.Should().BeTrue();
        sut.StatusText.Should().Contain("3.1.2").And.Contain("next launch");
    }

    [Fact]
    public async Task Install_WithNoPendingUpdate_NoOps()
    {
        // Reach Install without StartAsync having found anything: e.g.
        // a future code path that wires Install directly to the
        // command. The VM should defend against the missing _pending
        // by doing nothing rather than dereferencing null.
        var fake = new FakeUpdateService();
        var sut = NewVm(fake, () => AutoUpdateMode.NotifyOnly);

        await sut.InstallCommand.ExecuteAsync(null);

        fake.DownloadCalls.Should().BeEmpty();
        fake.ApplyOnNextLaunchCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Dismiss_HidesBanner_DoesNotCancelQueuedApply()
    {
        var available = NewAvailable("1.0.0");
        var fake = new FakeUpdateService { CheckResult = available };
        var sut = NewVm(fake, () => AutoUpdateMode.Automatic);
        await sut.StartAsync(CancellationToken.None);
        sut.IsBannerVisible.Should().BeTrue();

        sut.DismissCommand.Execute(null);

        sut.IsBannerVisible.Should().BeFalse();
        fake.ApplyOnNextLaunchCalls.Should().HaveCount(1); // unchanged
    }

    [Fact]
    public async Task Skip_HidesBanner_AndPersistsSkippedVersion()
    {
        var available = NewAvailable("1.5.0");
        var fake = new FakeUpdateService { CheckResult = available };
        var setCalls = new List<string?>();
        var sut = NewVm(fake, () => AutoUpdateMode.NotifyOnly, setSkipped: v => setCalls.Add(v));
        await sut.StartAsync(CancellationToken.None);
        sut.IsBannerVisible.Should().BeTrue();

        sut.SkipCommand.Execute(null);

        sut.IsBannerVisible.Should().BeFalse();
        setCalls.Should().ContainSingle().Which.Should().Be("1.5.0");
    }

    [Fact]
    public void Skip_WithNoPendingUpdate_NoOps()
    {
        var setCalls = new List<string?>();
        var sut = NewVm(new FakeUpdateService(), () => AutoUpdateMode.NotifyOnly,
            setSkipped: v => setCalls.Add(v));

        sut.SkipCommand.Execute(null);

        setCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMode_ReadAtEachStart_AllowsLiveModeChanges()
    {
        // The constructor takes a Func<AutoUpdateMode> rather than a
        // snapshot so changes to AppSettings.AutoUpdate take effect on
        // the next StartAsync (without recreating the view-model).
        // Demonstrates the contract for callers (and prevents a
        // future refactor from accidentally capturing a snapshot).
        var mode = AutoUpdateMode.Disabled;
        var fake = new FakeUpdateService { CheckResult = NewAvailable("9.9.9") };
        var sut = NewVm(fake, () => mode);

        await sut.StartAsync(CancellationToken.None);
        fake.CheckCalls.Should().Be(0);

        mode = AutoUpdateMode.NotifyOnly;
        await sut.StartAsync(CancellationToken.None);
        fake.CheckCalls.Should().Be(1);
    }

    private static UpdateNotificationViewModel NewVm(
        IUpdateService updates,
        Func<AutoUpdateMode> getMode,
        Func<UpdateCheckCadence>? getCadence = null,
        Func<string?>? getSkipped = null,
        Action<string?>? setSkipped = null)
    {
        return new UpdateNotificationViewModel(
            updates,
            getMode,
            getCadence ?? (() => UpdateCheckCadence.Daily),
            getSkipped ?? (() => null),
            setSkipped ?? (_ => { }),
            useDispatcherTimer: false);
    }

    private static UpdateCheckResult NewAvailable(string version) =>
        new() { IsAvailable = true, Version = version, OpaqueHandle = new object() };

    private sealed class FakeUpdateService : IUpdateService
    {
        public UpdateCheckResult CheckResult { get; set; } = UpdateCheckResult.NoUpdateAvailable;
        public int CheckCalls { get; private set; }
        public List<UpdateCheckResult> DownloadCalls { get; } = new();
        public List<UpdateCheckResult> ApplyOnNextLaunchCalls { get; } = new();

        public Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
        {
            CheckCalls++;
            return Task.FromResult(CheckResult);
        }

        public Task DownloadAsync(UpdateCheckResult update, CancellationToken ct)
        {
            DownloadCalls.Add(update);
            return Task.CompletedTask;
        }

        public Task ApplyOnNextLaunchAsync(UpdateCheckResult update, CancellationToken ct)
        {
            ApplyOnNextLaunchCalls.Add(update);
            return Task.CompletedTask;
        }
    }
}
