using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Polls GitHub's PR endpoint on a periodic timer, raises a single
/// debounced <see cref="Changed"/> event whenever head or base SHAs
/// shift or the PR's state transitions. Sibling abstraction to
/// <see cref="IRepositoryWatcher"/>: same event-driven shape so a
/// single <c>OnRepositoryChanged</c>-style handler can react to both
/// working-tree and remote changes uniformly.
///
/// <para>The watcher pauses its periodic timer when the main window
/// is hidden (via <see cref="IWindowVisibilityProbe"/>), backs off
/// exponentially when GitHub's <c>X-RateLimit-Remaining</c> drops
/// below 100, and stops polling permanently after a 401/403 — terminal
/// errors are surfaced once via <see cref="Changed"/> with
/// <see cref="PullRequestChangeKind.PollFailed"/>.</para>
///
/// <para><see cref="Suspend"/> nests; multiple concurrent suspends
/// compose. The watcher continues to record poll-discovered changes
/// while suspended and fires one <see cref="Changed"/> on resume if
/// anything accumulated.</para>
/// </summary>
public interface IPullRequestWatcher : IDisposable
{
    /// <summary>Raised after a poll discovers a change. May fire on any thread.</summary>
    event EventHandler<PullRequestChangedEventArgs>? Changed;

    /// <summary>Begin polling. Idempotent. Honors the current visibility state.</summary>
    void Start();

    /// <summary>
    /// Suspend the periodic poll until the returned token is disposed.
    /// Multiple concurrent suspends compose. If a poll-discovered change
    /// arrives during suspension (via an in-flight tick that completes
    /// after <see cref="Suspend"/> returns), one <see cref="Changed"/>
    /// fires after the outermost token is disposed.
    /// </summary>
    IDisposable Suspend();

    /// <summary>
    /// Force an immediate off-timer poll. The user-facing F5 command
    /// goes through this method so manual and auto-refresh share one
    /// code path. Safe to call before <see cref="Start"/>; a no-op if
    /// the watcher has already entered the terminal
    /// <see cref="PullRequestChangeKind.PollFailed"/> state.
    /// </summary>
    void RequestImmediatePoll();
}
