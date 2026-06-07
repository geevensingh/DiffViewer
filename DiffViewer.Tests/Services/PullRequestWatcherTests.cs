using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// Behavioral coverage for <see cref="PullRequestWatcher"/>. Uses the
/// existing <see cref="ManualTimeProvider"/> from
/// <see cref="RepositoryEventDebouncerTests"/> so timer ticks are
/// deterministic; the fake <see cref="IGitHubClient"/> /
/// <see cref="IPullRequestLocalFetcher"/> return synchronous results so
/// each <c>Advance</c> call completes the poll before returning.
/// </summary>
public sealed class PullRequestWatcherTests
{
    private static readonly PullRequestRef Pr = new("github.com", "owner", "repo", 7);
    private const string InitialHead = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string InitialMergeBase = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    [Fact]
    public void Start_DoesNotPollBeforeFirstInterval()
    {
        var (watcher, client, _, time, _) = Build();
        watcher.Start();

        time.Advance(Interval - TimeSpan.FromSeconds(1));

        client.CallCount.Should().Be(0);
    }

    [Fact]
    public void Start_FiresOnePollPerInterval()
    {
        var (watcher, client, _, time, _) = Build();
        watcher.Start();

        time.Advance(Interval);
        time.Advance(Interval);
        time.Advance(Interval);

        client.CallCount.Should().Be(3);
    }

    [Fact]
    public void UnchangedResponse_DoesNotRaiseChanged()
    {
        var (watcher, _, _, time, observed) = Build();
        watcher.Start();

        time.Advance(Interval);

        observed.Should().BeEmpty();
    }

    [Fact]
    public void NotModified_DoesNotRaiseChanged_AndPreservesETag()
    {
        var (watcher, client, _, time, observed) = Build();
        // First poll: server returns 200 with ETag header. Snapshot
        // unchanged so no event fires.
        client.Response = (BuildInfo(), etag: "\"v1\"", rateLimit: null);
        watcher.Start();
        time.Advance(Interval);
        observed.Should().BeEmpty();

        // Second poll: server returns 304. Watcher must send the cached
        // ETag as If-None-Match and continue to suppress events.
        client.Response = (info: null, etag: null, rateLimit: null);
        time.Advance(Interval);

        observed.Should().BeEmpty();
        client.LastIfNoneMatch.Should().Be("\"v1\"");
    }

    [Fact]
    public void HeadMoved_RaisesChangedWithHeadMovedFlagAndNewSnapshot()
    {
        var (watcher, client, fetcher, time, observed) = Build();
        const string newHead = "cccccccccccccccccccccccccccccccccccccccc";
        client.Response = (BuildInfo(headSha: newHead), null, null);
        fetcher.Result = new PullRequestFetchResult(InitialMergeBase, newHead);

        watcher.Start();
        time.Advance(Interval);

        observed.Should().HaveCount(1);
        var e = observed[0];
        e.Kind.Should().Be(PullRequestChangeKind.HeadMoved);
        e.NewSnapshot!.HeadSha.Should().Be(newHead);
        e.NewSnapshot.MergeBaseSha.Should().Be(InitialMergeBase);
    }

    [Fact]
    public void BaseMoved_RaisesChangedWithBaseMovedFlag()
    {
        var (watcher, client, fetcher, time, observed) = Build();
        const string newBase = "dddddddddddddddddddddddddddddddddddddddd";
        // API reports new base SHA but unchanged head SHA. After local
        // refetch the merge-base also moves (the fetcher returns the
        // newly-computed base).
        client.Response = (BuildInfo(baseSha: newBase), null, null);
        fetcher.Result = new PullRequestFetchResult(newBase, InitialHead);

        watcher.Start();
        time.Advance(Interval);

        observed.Should().HaveCount(1);
        observed[0].Kind.Should().Be(PullRequestChangeKind.BaseMoved);
        observed[0].NewSnapshot!.MergeBaseSha.Should().Be(newBase);
    }

    [Fact]
    public void StateChanged_WithoutShaMovement_DoesNotRefetch()
    {
        var (watcher, client, fetcher, time, observed) = Build();
        client.Response = (BuildInfo(state: "closed", merged: true), null, null);

        watcher.Start();
        time.Advance(Interval);

        observed.Should().HaveCount(1);
        observed[0].Kind.Should().Be(PullRequestChangeKind.StateChanged);
        fetcher.CallCount.Should().Be(0);
    }

    [Fact]
    public void HeadMovedPlusStateChanged_FiresCombinedKind()
    {
        var (watcher, client, fetcher, time, observed) = Build();
        const string newHead = "cccccccccccccccccccccccccccccccccccccccc";
        client.Response = (BuildInfo(headSha: newHead, state: "closed", merged: true), null, null);
        fetcher.Result = new PullRequestFetchResult(InitialMergeBase, newHead);

        watcher.Start();
        time.Advance(Interval);

        observed.Should().HaveCount(1);
        observed[0].Kind.Should().Be(
            PullRequestChangeKind.HeadMoved | PullRequestChangeKind.StateChanged);
    }

    [Fact]
    public void Unauthorized_FiresTerminalPollFailed_AndStopsPolling()
    {
        var (watcher, client, _, time, observed) = Build();
        client.Throw = new GitHubException("GitHub rejected the auth token. Run `gh auth login`.");

        watcher.Start();
        time.Advance(Interval);
        time.Advance(Interval);
        time.Advance(Interval);

        observed.Should().HaveCount(1);
        observed[0].Kind.Should().Be(PullRequestChangeKind.PollFailed);
        observed[0].FailureMessage.Should().Contain("rejected the auth token");
        client.CallCount.Should().Be(1, "terminal stop prevents subsequent ticks from polling");
    }

    [Fact]
    public void TransientHttpError_IsSwallowed_AndNextTickRetries()
    {
        var (watcher, client, _, time, observed) = Build();
        client.Throw = new GitHubException("Network glitch. Please retry.");

        watcher.Start();
        time.Advance(Interval);

        observed.Should().BeEmpty();
        client.CallCount.Should().Be(1);

        // Next tick: clear the error, expect a real poll.
        client.Throw = null;
        time.Advance(Interval);
        client.CallCount.Should().Be(2);
    }

    [Fact]
    public void Suspend_BuffersChangeUntilResume()
    {
        var (watcher, client, fetcher, time, observed) = Build();
        const string newHead = "cccccccccccccccccccccccccccccccccccccccc";
        client.Response = (BuildInfo(headSha: newHead), null, null);
        fetcher.Result = new PullRequestFetchResult(InitialMergeBase, newHead);

        watcher.Start();
        using (var token = watcher.Suspend())
        {
            time.Advance(Interval);
            observed.Should().BeEmpty("Changed must not fire while suspended");
        }

        observed.Should().HaveCount(1);
        observed[0].Kind.Should().Be(PullRequestChangeKind.HeadMoved);
    }

    [Fact]
    public void RequestImmediatePoll_FiresOffCycle()
    {
        var (watcher, client, _, time, _) = Build();
        watcher.Start();

        time.Advance(Interval / 2);
        watcher.RequestImmediatePoll();
        // Advance just enough for the zero-due-time timer to fire.
        time.Advance(TimeSpan.FromMilliseconds(1));

        client.CallCount.Should().Be(1);
    }

    [Fact]
    public void HiddenWindow_PausesPolling()
    {
        var visibility = new FakeVisibility { IsVisible = false };
        var (watcher, client, _, time, _) = Build(visibility: visibility);
        watcher.Start();

        time.Advance(Interval * 3);

        client.CallCount.Should().Be(0, "hidden window must not consume API quota");
    }

    [Fact]
    public void RestoringVisibility_TriggersImmediatePoll()
    {
        var visibility = new FakeVisibility { IsVisible = false };
        var (watcher, client, _, time, _) = Build(visibility: visibility);
        watcher.Start();

        time.Advance(Interval * 2);
        client.CallCount.Should().Be(0);

        visibility.SetVisible(true);
        time.Advance(TimeSpan.FromMilliseconds(1));

        client.CallCount.Should().Be(1, "restoring visibility kicks one immediate poll");
    }

    [Fact]
    public void LowRateLimit_TriggersBackoff()
    {
        var (watcher, client, _, time, _) = Build();
        // First poll returns low rate-limit.
        client.Response = (BuildInfo(), null, rateLimit: 50);
        watcher.Start();

        time.Advance(Interval);
        client.CallCount.Should().Be(1);

        // Next tick at base interval should NOT fire — backoff multiplies it.
        time.Advance(Interval);
        client.CallCount.Should().Be(1, "first poll observed low quota; next tick deferred");

        // After (multiplier - 1) more intervals it should fire.
        time.Advance(Interval * (PullRequestWatcher.LowRateLimitBackoffMultiplier - 1));
        client.CallCount.Should().Be(2);
    }

    [Fact]
    public void RateLimitRecovery_ReturnsToNormalCadence()
    {
        var (watcher, client, _, time, _) = Build();
        client.Response = (BuildInfo(), null, rateLimit: 50);
        watcher.Start();

        time.Advance(Interval);  // fires; sees low quota
        client.Response = (BuildInfo(), null, rateLimit: 500);  // recovered
        time.Advance(Interval * PullRequestWatcher.LowRateLimitBackoffMultiplier);  // backoff tick
        client.CallCount.Should().Be(2);

        // Subsequent tick should fire at base interval again.
        time.Advance(Interval);
        client.CallCount.Should().Be(3);
    }

    [Fact]
    public void Dispose_StopsAllFutureWork()
    {
        var (watcher, client, _, time, _) = Build();
        watcher.Start();
        watcher.Dispose();

        time.Advance(Interval * 5);

        client.CallCount.Should().Be(0);
    }

    [Fact]
    public void Start_IsIdempotent()
    {
        var (watcher, client, _, time, _) = Build();
        watcher.Start();
        watcher.Start();
        watcher.Start();

        time.Advance(Interval);

        client.CallCount.Should().Be(1, "extra Start calls must not multiply polling");
    }

    // ---------- helpers ----------

    private static (
        PullRequestWatcher watcher,
        FakeGitHubClient client,
        FakeLocalFetcher fetcher,
        ManualTimeProvider time,
        List<PullRequestChangedEventArgs> observed)
        Build(IWindowVisibilityProbe? visibility = null)
    {
        var client = new FakeGitHubClient();
        var fetcher = new FakeLocalFetcher();
        var time = new ManualTimeProvider();
        var observed = new List<PullRequestChangedEventArgs>();
        var snapshot = new RemoteRefSnapshot(InitialHead, InitialMergeBase);

        var watcher = new PullRequestWatcher(
            pr: Pr,
            localClonePath: @"C:\fake\repo",
            initialSnapshot: snapshot,
            gitHubClient: client,
            localFetcher: fetcher,
            intervalProvider: () => Interval,
            visibility: visibility,
            timeProvider: time,
            initialState: "open",
            initialMerged: false);

        watcher.Changed += (_, e) => observed.Add(e);
        return (watcher, client, fetcher, time, observed);
    }

    private static PullRequestInfo BuildInfo(
        string? headSha = null,
        string? baseSha = null,
        string state = "open",
        bool merged = false)
    {
        return new PullRequestInfo(
            Number: Pr.Number,
            Title: "t",
            State: state,
            Merged: merged,
            BaseRef: "main",
            BaseSha: baseSha ?? InitialMergeBase,
            HeadRef: "feat",
            HeadSha: headSha ?? InitialHead,
            HeadRepoCloneUrl: "https://github.com/owner/repo.git",
            BaseRepoCloneUrl: "https://github.com/owner/repo.git");
    }

    private sealed class FakeGitHubClient : IGitHubClient
    {
        public (PullRequestInfo? info, string? etag, int? rateLimit) Response { get; set; }
            = (BuildInfo(), null, null);
        public GitHubException? Throw { get; set; }
        public int CallCount { get; private set; }
        public string? LastIfNoneMatch { get; private set; }

        public Task<PullRequestInfo> GetPullRequestAsync(PullRequestRef pr, CancellationToken ct)
            => throw new NotSupportedException("Watcher uses the polled path.");

        public Task<PullRequestPolledResult> GetPullRequestPolledAsync(
            PullRequestRef pr, string? ifNoneMatch, CancellationToken ct)
        {
            CallCount++;
            LastIfNoneMatch = ifNoneMatch;
            if (Throw is not null) throw Throw;
            var (info, etag, rateLimit) = Response;
            return Task.FromResult(new PullRequestPolledResult(info, etag, rateLimit));
        }
    }

    private sealed class FakeLocalFetcher : IPullRequestLocalFetcher
    {
        public PullRequestFetchResult Result { get; set; }
            = new(InitialMergeBase, InitialHead);
        public int CallCount { get; private set; }

        public Task<PullRequestFetchResult> FetchAsync(
            string repoPath, PullRequestInfo info, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeVisibility : IWindowVisibilityProbe
    {
        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set => SetVisible(value);
        }
        public event EventHandler? VisibilityChanged;

        public void SetVisible(bool value)
        {
            if (_isVisible == value) return;
            _isVisible = value;
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
