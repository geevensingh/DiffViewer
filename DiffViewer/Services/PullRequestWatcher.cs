using System.Threading;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="IPullRequestWatcher"/>. Periodic ETag-aware
/// poll of GitHub's PR endpoint; on every successful poll, diff the
/// returned <see cref="PullRequestInfo"/> against the captured
/// <see cref="RemoteRefSnapshot"/> and the previous (state, merged)
/// pair to decide which <see cref="PullRequestChangeKind"/> flags to
/// raise.
///
/// <para><b>Concurrency model:</b> exactly one poll runs at a time
/// (re-entrancy guard); a tick that arrives while a poll is in flight
/// is dropped silently. The periodic timer + immediate poll requests
/// + visibility transitions all funnel through the same single-flight
/// gate. The watcher exposes <see cref="IDisposable"/> and cancels
/// any in-flight poll via an internal <see cref="CancellationTokenSource"/>
/// on disposal — callers do not need to await disposal.</para>
///
/// <para><b>Terminal state:</b> 401/403 from the API (token rejected,
/// SSO required, etc.) → raise one <see cref="PullRequestChangeKind.PollFailed"/>
/// event carrying the human message, then ignore every subsequent
/// tick / immediate-poll request. The user gets one toast and is not
/// hammered with the same failure every interval.</para>
///
/// <para><b>Rate-limit backoff:</b> when GitHub's
/// <c>X-RateLimit-Remaining</c> drops below
/// <see cref="LowRateLimitThreshold"/>, the next interval is multiplied
/// by <see cref="LowRateLimitBackoffMultiplier"/>. Recovery to a value
/// at or above the threshold returns the cadence to the configured
/// interval on the following tick.</para>
///
/// <para><b>Suspend semantics:</b> suspend tokens nest like
/// <see cref="RepositoryEventDebouncer"/>'s. Polls continue while
/// suspended (so the snapshot stays warm), but <see cref="Changed"/>
/// emissions buffer the latest change kind + payload and fire once
/// after the outermost token is disposed.</para>
///
/// <para><b>Visibility:</b> a hidden main window pauses scheduling.
/// Restoring visibility fires one immediate poll and resumes the
/// regular schedule.</para>
/// </summary>
public sealed class PullRequestWatcher : IPullRequestWatcher
{
    /// <summary>
    /// Threshold below which we slow polling to conserve API quota.
    /// 100 chosen so a 5000/hr authenticated user (default) only hits
    /// backoff after a meaningful pattern of other API consumers on
    /// the same token; unauthenticated callers at 60/hr trip backoff
    /// far earlier but they're also at much greater absolute risk.
    /// </summary>
    public const int LowRateLimitThreshold = 100;

    /// <summary>Multiplier applied to the next interval when below the threshold.</summary>
    public const int LowRateLimitBackoffMultiplier = 4;

    private readonly PullRequestRef _pr;
    private readonly string _localClonePath;
    private readonly IGitHubClient _gitHubClient;
    private readonly IPullRequestLocalFetcher _localFetcher;
    private readonly IWindowVisibilityProbe? _visibility;
    private readonly Func<TimeSpan> _intervalProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _timer;

    private readonly object _lock = new();
    private readonly CancellationTokenSource _disposeCts = new();

    private RemoteRefSnapshot _snapshot;
    private string? _previousState;
    private bool _previousMerged;
    private string? _etag;
    private int _suspendCount;
    private PullRequestChangedEventArgs? _bufferedChange;
    private bool _started;
    private bool _disposed;
    private bool _terminal;
    private bool _pollInFlight;
    private bool _backoffInEffect;

    public event EventHandler<PullRequestChangedEventArgs>? Changed;

    public PullRequestWatcher(
        PullRequestRef pr,
        string localClonePath,
        RemoteRefSnapshot initialSnapshot,
        IGitHubClient gitHubClient,
        IPullRequestLocalFetcher localFetcher,
        Func<TimeSpan> intervalProvider,
        IWindowVisibilityProbe? visibility = null,
        TimeProvider? timeProvider = null,
        string? initialState = null,
        bool initialMerged = false)
    {
        _pr = pr ?? throw new ArgumentNullException(nameof(pr));
        _localClonePath = !string.IsNullOrWhiteSpace(localClonePath)
            ? localClonePath
            : throw new ArgumentException("Local clone path is required.", nameof(localClonePath));
        _snapshot = initialSnapshot ?? throw new ArgumentNullException(nameof(initialSnapshot));
        _gitHubClient = gitHubClient ?? throw new ArgumentNullException(nameof(gitHubClient));
        _localFetcher = localFetcher ?? throw new ArgumentNullException(nameof(localFetcher));
        _intervalProvider = intervalProvider ?? throw new ArgumentNullException(nameof(intervalProvider));
        _visibility = visibility;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _previousState = initialState;
        _previousMerged = initialMerged;

        _timer = _timeProvider.CreateTimer(
            OnTimerTick, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PullRequestWatcher));
            if (_started) return;
            _started = true;
        }

        if (_visibility is not null)
        {
            _visibility.VisibilityChanged += OnVisibilityChanged;
        }

        ScheduleNext(immediate: false);
    }

    public IDisposable Suspend()
    {
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PullRequestWatcher));
            _suspendCount++;
        }
        return new SuspensionToken(this);
    }

    public void RequestImmediatePoll()
    {
        lock (_lock)
        {
            if (_disposed || _terminal) return;
        }
        ScheduleNext(immediate: true);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        if (_visibility is not null)
        {
            _visibility.VisibilityChanged -= OnVisibilityChanged;
        }

        try { _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { }

        _timer.Dispose();
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }

    private void OnTimerTick(object? _) => _ = PollAsync();

    private void OnVisibilityChanged(object? sender, EventArgs e)
    {
        if (_visibility is null) return;

        if (_visibility.IsVisible)
        {
            // Restoring visibility: one immediate poll, then resume schedule.
            ScheduleNext(immediate: true);
        }
        else
        {
            // Hiding: cancel any scheduled tick. An in-flight poll
            // is allowed to complete (the snapshot may still update).
            try { _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }
    }

    private void ScheduleNext(bool immediate)
    {
        lock (_lock)
        {
            if (_disposed || _terminal || !_started) return;
            if (_visibility is { IsVisible: false } && !immediate) return;
        }

        var dueTime = immediate
            ? TimeSpan.Zero
            : EffectiveInterval();

        try
        {
            _timer.Change(dueTime, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Raced with Dispose; nothing to do.
        }
    }

    private TimeSpan EffectiveInterval()
    {
        var configured = _intervalProvider();
        if (configured < TimeSpan.FromSeconds(AppSettings.PullRequestPollIntervalSecondsMin))
            configured = TimeSpan.FromSeconds(AppSettings.PullRequestPollIntervalSecondsMin);

        lock (_lock)
        {
            return _backoffInEffect
                ? TimeSpan.FromTicks(configured.Ticks * LowRateLimitBackoffMultiplier)
                : configured;
        }
    }

    private async Task PollAsync()
    {
        // Single-flight gate.
        lock (_lock)
        {
            if (_disposed || _terminal) return;
            if (_pollInFlight) return;
            _pollInFlight = true;
        }

        try
        {
            await PollOnceAsync(_disposeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown; nothing to surface.
        }
        catch
        {
            // Transient HTTP / parse failures are silently swallowed so
            // a flaky network doesn't flood the title bar. The next tick
            // will try again. Terminal auth failures are surfaced via
            // PollOnceAsync's explicit GitHubException handling below.
        }
        finally
        {
            lock (_lock)
            {
                _pollInFlight = false;
            }

            ScheduleNext(immediate: false);
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        PullRequestPolledResult result;
        try
        {
            result = await _gitHubClient
                .GetPullRequestPolledAsync(_pr, _etag, ct)
                .ConfigureAwait(false);
        }
        catch (GitHubException ex) when (IsAuthFailureMessage(ex.Message))
        {
            FireTerminal(ex.Message);
            return;
        }

        // Update ETag + rate-limit state regardless of body shape.
        lock (_lock)
        {
            _etag = result.ETag ?? _etag;
            _backoffInEffect = result.RateLimitRemaining is int n && n < LowRateLimitThreshold;
        }

        if (result.Info is null)
        {
            // 304: nothing changed since the last poll. Snapshot stays.
            return;
        }

        var info = result.Info;

        bool stateChanged;
        lock (_lock)
        {
            stateChanged =
                !string.Equals(_previousState, info.State, StringComparison.Ordinal)
                || _previousMerged != info.Merged;
            _previousState = info.State;
            _previousMerged = info.Merged;
        }

        bool headApiChanged = !string.Equals(info.HeadSha, _snapshot.HeadSha, StringComparison.OrdinalIgnoreCase);
        bool baseApiChanged = !string.Equals(info.BaseSha, _snapshot.MergeBaseSha, StringComparison.OrdinalIgnoreCase);

        // Re-fetch only when the API says SHAs moved. State-only changes
        // (merge / close with unchanged tip) skip the network refetch.
        RemoteRefSnapshot? newSnapshot = null;
        PullRequestChangeKind kind = PullRequestChangeKind.None;

        if (headApiChanged || baseApiChanged)
        {
            PullRequestFetchResult fetched;
            try
            {
                fetched = await _localFetcher
                    .FetchAsync(_localClonePath, info, ct)
                    .ConfigureAwait(false);
            }
            catch (PullRequestFetchException)
            {
                // Fetch failed (e.g. PR head pruned upstream). Leave the
                // snapshot as-is and try again next tick; don't surface
                // a toast for transient fetch failures.
                if (stateChanged) RaiseOrBuffer(PullRequestChangeKind.StateChanged, info, null, null);
                return;
            }

            var candidate = new RemoteRefSnapshot(fetched.HeadSha, fetched.MergeBaseSha);
            bool snapshotChanged = !string.Equals(candidate.HeadSha, _snapshot.HeadSha, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(candidate.MergeBaseSha, _snapshot.MergeBaseSha, StringComparison.OrdinalIgnoreCase);

            if (snapshotChanged)
            {
                if (!string.Equals(candidate.HeadSha, _snapshot.HeadSha, StringComparison.OrdinalIgnoreCase))
                    kind |= PullRequestChangeKind.HeadMoved;
                if (!string.Equals(candidate.MergeBaseSha, _snapshot.MergeBaseSha, StringComparison.OrdinalIgnoreCase))
                    kind |= PullRequestChangeKind.BaseMoved;
                newSnapshot = candidate;
                _snapshot = candidate;
            }
        }

        if (stateChanged) kind |= PullRequestChangeKind.StateChanged;

        if (kind != PullRequestChangeKind.None)
        {
            RaiseOrBuffer(kind, info, newSnapshot, null);
        }
    }

    private static bool IsAuthFailureMessage(string message)
    {
        // The polled path's GitHubException for 401/403 carries the
        // user-facing message verbatim. Pattern-match defensively rather
        // than introducing a typed subclass — the error matrix is
        // documented in GitHubClient and changing the message text is
        // already a UX change the test suite would catch.
        return message.Contains("auth token", StringComparison.OrdinalIgnoreCase)
            || message.Contains("403", StringComparison.OrdinalIgnoreCase)
            || message.Contains("refused the request", StringComparison.OrdinalIgnoreCase);
    }

    private void FireTerminal(string message)
    {
        lock (_lock)
        {
            if (_terminal) return;
            _terminal = true;
        }

        try { _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { }

        RaiseOrBuffer(
            PullRequestChangeKind.PollFailed,
            newInfo: null,
            newSnapshot: null,
            failureMessage: message);
    }

    private void RaiseOrBuffer(
        PullRequestChangeKind kind,
        PullRequestInfo? newInfo,
        RemoteRefSnapshot? newSnapshot,
        string? failureMessage)
    {
        var args = new PullRequestChangedEventArgs(
            kind, newInfo, newSnapshot, failureMessage,
            _timeProvider.GetUtcNow().UtcDateTime);

        bool fireNow;
        lock (_lock)
        {
            if (_suspendCount > 0)
            {
                // Collapse multiple buffered events into the latest
                // payload + cumulative kind mask.
                if (_bufferedChange is null)
                {
                    _bufferedChange = args;
                }
                else
                {
                    _bufferedChange = new PullRequestChangedEventArgs(
                        _bufferedChange.Kind | kind,
                        newInfo ?? _bufferedChange.NewInfo,
                        newSnapshot ?? _bufferedChange.NewSnapshot,
                        failureMessage ?? _bufferedChange.FailureMessage,
                        args.UtcTimestamp);
                }
                return;
            }

            fireNow = true;
        }

        if (fireNow) Changed?.Invoke(this, args);
    }

    private void Resume()
    {
        PullRequestChangedEventArgs? toFire = null;
        lock (_lock)
        {
            if (_suspendCount == 0) return;
            _suspendCount--;
            if (_suspendCount > 0) return;

            toFire = _bufferedChange;
            _bufferedChange = null;
        }

        if (toFire is not null) Changed?.Invoke(this, toFire);
    }

    private sealed class SuspensionToken : IDisposable
    {
        private PullRequestWatcher? _owner;

        public SuspensionToken(PullRequestWatcher owner) => _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Resume();
        }
    }
}
