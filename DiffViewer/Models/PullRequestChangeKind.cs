namespace DiffViewer.Models;

/// <summary>
/// Categorises what the pull-request watcher observed on a given poll.
/// Bitmask-friendly so a single tick can flag multiple kinds (e.g. a
/// force-push that also moves the base branch surfaces
/// <c>HeadMoved | BaseMoved</c>).
///
/// <para><see cref="StateChanged"/> covers PR lifecycle transitions
/// (open ↔ closed, merged ↔ not). The coordinator surfaces these via
/// the title-bar toast but does NOT rebuild the diff — the SHAs are
/// still valid even after the PR is merged.</para>
///
/// <para><see cref="PollFailed"/> is a terminal signal: the watcher
/// has stopped polling (typically due to a 401/403) and the consumer
/// should surface <see cref="PullRequestChangedEventArgs.FailureMessage"/>
/// to the user once.</para>
/// </summary>
[System.Flags]
public enum PullRequestChangeKind
{
    None = 0,
    HeadMoved = 1 << 0,
    BaseMoved = 1 << 1,
    StateChanged = 1 << 2,
    PollFailed = 1 << 3,
}
