using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public sealed class NullUpdateServiceTests
{
    [Fact]
    public void CanAutoApply_IsFalse()
    {
        // Portable launches can't apply silently — the VM uses this
        // flag to demote Automatic to NotifyOnly behavior so the
        // browser-notify path doesn't surprise users.
        var sut = new NullUpdateService();

        sut.CanAutoApply.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAsync_ReturnsNoUpdateAvailableSingleton()
    {
        var sut = new NullUpdateService();

        var result = await sut.CheckAsync(CancellationToken.None);

        result.Should().BeSameAs(UpdateCheckResult.NoUpdateAvailable);
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_NoOps_ForNoUpdateAvailable()
    {
        var sut = new NullUpdateService();

        var act = async () => await sut.DownloadAsync(
            UpdateCheckResult.NoUpdateAvailable,
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ApplyOnNextLaunchAsync_NoOps_ForNoUpdateAvailable()
    {
        var sut = new NullUpdateService();

        var act = async () => await sut.ApplyOnNextLaunchAsync(
            UpdateCheckResult.NoUpdateAvailable,
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AllMethods_HonorAlreadyCancelledToken_AsNoOp()
    {
        // The no-op is deliberately tolerant of an already-cancelled
        // token — there's no work to abandon and surfacing an OCE
        // would be noise. Phase 2.4 may revisit this when real
        // implementations start accepting and obeying the token.
        var sut = new NullUpdateService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var check = async () => await sut.CheckAsync(cts.Token);
        var download = async () => await sut.DownloadAsync(UpdateCheckResult.NoUpdateAvailable, cts.Token);
        var apply = async () => await sut.ApplyOnNextLaunchAsync(UpdateCheckResult.NoUpdateAvailable, cts.Token);

        await check.Should().NotThrowAsync();
        await download.Should().NotThrowAsync();
        await apply.Should().NotThrowAsync();
    }
}
