using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public sealed class NullUpdateServiceTests
{
    [Fact]
    public async Task CheckAndQueueUpdateAsync_Completes_WithoutThrowing()
    {
        var sut = new NullUpdateService();

        var act = async () => await sut.CheckAndQueueUpdateAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckAndQueueUpdateAsync_HonorsAlreadyCancelledToken_AsNoOp()
    {
        // The no-op is deliberately tolerant of an already-cancelled
        // token — there's no work to abandon and surfacing an OCE
        // would be noise. Phase 2.3 may revisit this when real
        // implementations start accepting and obeying the token.
        var sut = new NullUpdateService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await sut.CheckAndQueueUpdateAsync(cts.Token);

        await act.Should().NotThrowAsync();
    }
}
