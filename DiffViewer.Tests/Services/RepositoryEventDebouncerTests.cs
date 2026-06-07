using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// Pure logic tests for <see cref="RepositoryEventDebouncer"/>. The
/// debouncer is driven by an injected <see cref="ManualTimeProvider"/>, so
/// timer ticks fire deterministically from <c>Advance(...)</c> on the test
/// thread — no <c>Thread.Sleep</c>, no threadpool scheduling, no flakiness.
/// </summary>
public class RepositoryEventDebouncerTests
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(50);

    [Fact]
    public void OnRawEvent_AfterDebounce_FiresOnce()
    {
        var time = new ManualTimeProvider();
        int fireCount = 0;
        RepositoryChangeKind capturedKind = RepositoryChangeKind.None;
        using var debouncer = new RepositoryEventDebouncer(DebounceInterval, kind =>
        {
            fireCount++;
            capturedKind = kind;
        }, time);

        debouncer.OnRawEvent(RepositoryChangeKind.WorkingTree);
        time.Advance(DebounceInterval);

        fireCount.Should().Be(1);
        capturedKind.Should().Be(RepositoryChangeKind.WorkingTree);
    }

    [Fact]
    public void OnRawEvent_BurstWithinDebounceWindow_FiresOnce()
    {
        var time = new ManualTimeProvider();
        int fireCount = 0;
        using var debouncer = new RepositoryEventDebouncer(DebounceInterval, _ => fireCount++, time);

        for (int i = 0; i < 10; i++)
        {
            debouncer.OnRawEvent(RepositoryChangeKind.WorkingTree);
            time.Advance(TimeSpan.FromMilliseconds(5)); // shorter than the 50 ms debounce
        }

        // Burst ended; advance past the debounce window from the last event.
        time.Advance(DebounceInterval);

        fireCount.Should().Be(1);
    }

    [Fact]
    public void OnRawEvent_MixedKinds_AccumulatesIntoBitmask()
    {
        var time = new ManualTimeProvider();
        RepositoryChangeKind capturedKind = RepositoryChangeKind.None;
        using var debouncer = new RepositoryEventDebouncer(DebounceInterval, kind => capturedKind = kind, time);

        debouncer.OnRawEvent(RepositoryChangeKind.WorkingTree);
        debouncer.OnRawEvent(RepositoryChangeKind.GitDir);
        time.Advance(DebounceInterval);

        capturedKind.Should().HaveFlag(RepositoryChangeKind.WorkingTree);
        capturedKind.Should().HaveFlag(RepositoryChangeKind.GitDir);
    }

    [Fact]
    public void OnRawEvent_BufferOverflow_FiresImmediatelyWithoutDebounce()
    {
        var time = new ManualTimeProvider();
        int fireCount = 0;
        RepositoryChangeKind capturedKind = RepositoryChangeKind.None;
        // A long debounce that we never advance past — proves BufferOverflow
        // bypasses the timer entirely.
        using var debouncer = new RepositoryEventDebouncer(TimeSpan.FromSeconds(10), kind =>
        {
            fireCount++;
            capturedKind = kind;
        }, time);

        debouncer.OnRawEvent(RepositoryChangeKind.BufferOverflow);

        fireCount.Should().Be(1);
        capturedKind.Should().HaveFlag(RepositoryChangeKind.BufferOverflow);
    }

    [Fact]
    public void Suspend_BlocksFireUntilResumed()
    {
        var time = new ManualTimeProvider();
        int fireCount = 0;
        using var debouncer = new RepositoryEventDebouncer(DebounceInterval, _ => fireCount++, time);

        var token = debouncer.Suspend();
        debouncer.OnRawEvent(RepositoryChangeKind.WorkingTree);
        time.Advance(DebounceInterval);
        fireCount.Should().Be(0);

        token.Dispose();
        // Resume fires synchronously when there's a pending event.
        fireCount.Should().Be(1);
    }

    [Fact]
    public void Suspend_NestedTokens_OnlyResumeOnOutermostDispose()
    {
        var time = new ManualTimeProvider();
        int fireCount = 0;
        using var debouncer = new RepositoryEventDebouncer(DebounceInterval, _ => fireCount++, time);

        var outer = debouncer.Suspend();
        var inner = debouncer.Suspend();

        debouncer.OnRawEvent(RepositoryChangeKind.WorkingTree);
        time.Advance(DebounceInterval);
        fireCount.Should().Be(0);

        inner.Dispose();
        fireCount.Should().Be(0); // still suspended

        outer.Dispose();
        fireCount.Should().Be(1);
    }

    [Fact]
    public void Suspend_NoPendingEvent_DoesNotFireOnResume()
    {
        var time = new ManualTimeProvider();
        int fireCount = 0;
        using var debouncer = new RepositoryEventDebouncer(DebounceInterval, _ => fireCount++, time);

        var token = debouncer.Suspend();
        token.Dispose();

        time.Advance(DebounceInterval);
        fireCount.Should().Be(0);
    }

    [Fact]
    public void Suspend_TokenDoubleDispose_IsSafe()
    {
        var time = new ManualTimeProvider();
        int fireCount = 0;
        using var debouncer = new RepositoryEventDebouncer(DebounceInterval, _ => fireCount++, time);

        var token = debouncer.Suspend();
        token.Dispose();
        token.Dispose(); // no-op

        debouncer.OnRawEvent(RepositoryChangeKind.WorkingTree);
        time.Advance(DebounceInterval);

        fireCount.Should().Be(1);
    }

    [Fact]
    public void Dispose_CancelsPendingFire()
    {
        var time = new ManualTimeProvider();
        int fireCount = 0;
        var debouncer = new RepositoryEventDebouncer(DebounceInterval, _ => fireCount++, time);

        debouncer.OnRawEvent(RepositoryChangeKind.WorkingTree);
        debouncer.Dispose();

        time.Advance(DebounceInterval * 4);
        fireCount.Should().Be(0);
    }

    [Fact]
    public void OnRawEvent_None_IsNoOp()
    {
        var time = new ManualTimeProvider();
        int fireCount = 0;
        using var debouncer = new RepositoryEventDebouncer(DebounceInterval, _ => fireCount++, time);

        debouncer.OnRawEvent(RepositoryChangeKind.None);
        time.Advance(DebounceInterval);

        fireCount.Should().Be(0);
    }
}

/// <summary>
/// Deterministic <see cref="TimeProvider"/> for timer tests. Time only
/// advances when <see cref="Advance"/> is called; any timer whose due time
/// has passed fires synchronously on the calling thread, in the order they
/// were registered.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
    private readonly List<ManualTimer> _timers = new();

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan delta)
    {
        _now += delta;
        // Snapshot so timers can Change / Dispose during their own callback.
        foreach (var timer in _timers.ToArray())
        {
            timer.TryFire(_now);
        }
    }

    internal void Remove(ManualTimer timer) => _timers.Remove(timer);

    internal sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private DateTimeOffset? _nextFire;
        private TimeSpan _period;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _nextFire = dueTime == Timeout.InfiniteTimeSpan
                ? null
                : _owner.GetUtcNow() + dueTime;
            _period = period;
            return true;
        }

        internal void TryFire(DateTimeOffset now)
        {
            while (_nextFire is { } due && due <= now)
            {
                // Snapshot the scheduled value so we can detect whether
                // the callback re-armed the timer (e.g. a self-rearming
                // watcher that calls Change inside its own tick). If the
                // callback set a new _nextFire, the post-callback
                // "one-shot done" branch must NOT overwrite it.
                _callback(_state);

                bool callbackRearmed = _nextFire is { } newNext && newNext != due;
                if (callbackRearmed)
                {
                    // Continue the loop with the new schedule, fire-as-needed.
                    continue;
                }

                if (_period <= TimeSpan.Zero || _period == Timeout.InfiniteTimeSpan)
                {
                    _nextFire = null;
                    break;
                }
                _nextFire = due + _period;
            }
        }

        public void Dispose()
        {
            _nextFire = null;
            _owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
