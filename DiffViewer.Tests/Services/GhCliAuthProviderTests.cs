using System.ComponentModel;
using System.IO;
using System.Threading;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public sealed class GhCliAuthProviderTests
{
    [Fact]
    public async Task TryGetTokenAsync_Success_ReturnsTrimmedToken()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, "ghp_secret\n", string.Empty));
        var provider = new GhCliAuthProvider(runner);

        var token = await provider.TryGetTokenAsync("github.com", CancellationToken.None);

        token.Should().Be("ghp_secret");
        runner.Calls.Should().HaveCount(1);
        runner.Calls[0].Arguments.Should().Equal("auth", "token", "--hostname", "github.com");
        runner.Calls[0].FileName.Should().Be("gh");
    }

    [Fact]
    public async Task TryGetTokenAsync_CacheHit_DoesNotReinvokeGh()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, "ghp_secret", string.Empty));
        var provider = new GhCliAuthProvider(runner);

        var first = await provider.TryGetTokenAsync("github.com", CancellationToken.None);
        var second = await provider.TryGetTokenAsync("github.com", CancellationToken.None);

        first.Should().Be("ghp_secret");
        second.Should().Be("ghp_secret");
        runner.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task TryGetTokenAsync_GhNotInstalled_ReturnsNullAndDoesNotCache()
    {
        var runner = new FakeProcessRunner(() => throw new Win32Exception("file not found"));
        var provider = new GhCliAuthProvider(runner);

        var first = await provider.TryGetTokenAsync("github.com", CancellationToken.None);
        first.Should().BeNull();

        // Failures must NOT be cached: a user installing gh mid-session
        // should be able to retry without restarting DiffViewer.
        runner.NextResult = new ProcessRunResult(0, "ghp_secret", string.Empty);
        var second = await provider.TryGetTokenAsync("github.com", CancellationToken.None);
        second.Should().Be("ghp_secret");
        runner.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task TryGetTokenAsync_FileNotFoundException_ReturnsNull()
    {
        var runner = new FakeProcessRunner(() => throw new FileNotFoundException());
        var provider = new GhCliAuthProvider(runner);

        var token = await provider.TryGetTokenAsync("github.com", CancellationToken.None);
        token.Should().BeNull();
    }

    [Fact]
    public async Task TryGetTokenAsync_NonZeroExit_ReturnsNullAndDoesNotCache()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(1, string.Empty, "not logged in"));
        var provider = new GhCliAuthProvider(runner);

        var first = await provider.TryGetTokenAsync("github.com", CancellationToken.None);
        first.Should().BeNull();

        runner.NextResult = new ProcessRunResult(0, "ghp_secret", string.Empty);
        var second = await provider.TryGetTokenAsync("github.com", CancellationToken.None);
        second.Should().Be("ghp_secret");
    }

    [Fact]
    public async Task TryGetTokenAsync_EmptyStdout_ReturnsNull()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, "   \n", string.Empty));
        var provider = new GhCliAuthProvider(runner);

        var token = await provider.TryGetTokenAsync("github.com", CancellationToken.None);
        token.Should().BeNull();
    }

    [Fact]
    public async Task InvalidateCache_DropsCachedToken_NextCallReinvokesGh()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, "ghp_first", string.Empty));
        var provider = new GhCliAuthProvider(runner);

        var first = await provider.TryGetTokenAsync("github.com", CancellationToken.None);
        first.Should().Be("ghp_first");

        provider.InvalidateCache("github.com");
        runner.NextResult = new ProcessRunResult(0, "ghp_second", string.Empty);

        var second = await provider.TryGetTokenAsync("github.com", CancellationToken.None);
        second.Should().Be("ghp_second");
        runner.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task TryGetTokenAsync_DifferentHosts_CachedIndependently()
    {
        var runner = new FakeProcessRunner();
        runner.QueueResult(new ProcessRunResult(0, "ghp_dotcom", string.Empty));
        runner.QueueResult(new ProcessRunResult(0, "ghp_ghes", string.Empty));
        var provider = new GhCliAuthProvider(runner);

        var dotcom = await provider.TryGetTokenAsync("github.com", CancellationToken.None);
        var ghes = await provider.TryGetTokenAsync("ghe.example.com", CancellationToken.None);

        dotcom.Should().Be("ghp_dotcom");
        ghes.Should().Be("ghp_ghes");
        runner.Calls.Should().HaveCount(2);

        // Cached now: re-querying neither host should spawn gh.
        await provider.TryGetTokenAsync("github.com", CancellationToken.None);
        await provider.TryGetTokenAsync("ghe.example.com", CancellationToken.None);
        runner.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task TryGetTokenAsync_HostCaseInsensitive()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, "ghp_secret", string.Empty));
        var provider = new GhCliAuthProvider(runner);

        var lower = await provider.TryGetTokenAsync("github.com", CancellationToken.None);
        var mixed = await provider.TryGetTokenAsync("GitHub.com", CancellationToken.None);

        lower.Should().Be("ghp_secret");
        mixed.Should().Be("ghp_secret");
        runner.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task TryGetTokenAsync_NullOrEmptyHost_Throws()
    {
        var runner = new FakeProcessRunner();
        var provider = new GhCliAuthProvider(runner);

        await FluentActions.Invoking(() =>
                provider.TryGetTokenAsync(string.Empty, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Queue<Func<ProcessRunResult>> _queue = new();
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = new();
        public ProcessRunResult? NextResult { get; set; }

        public FakeProcessRunner() { }

        public FakeProcessRunner(ProcessRunResult result)
        {
            QueueResult(result);
        }

        public FakeProcessRunner(Func<ProcessRunResult> producer)
        {
            _queue.Enqueue(producer);
        }

        public void QueueResult(ProcessRunResult result)
        {
            _queue.Enqueue(() => result);
        }

        public Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken ct)
        {
            Calls.Add((fileName, arguments.ToList()));
            if (_queue.Count > 0)
            {
                return Task.FromResult(_queue.Dequeue()());
            }

            if (NextResult is not null)
            {
                var result = NextResult;
                NextResult = null;
                return Task.FromResult(result);
            }

            throw new InvalidOperationException("FakeProcessRunner has no queued result.");
        }
    }
}
