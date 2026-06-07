namespace DiffViewer.Models;

using DiffViewer.Services;

/// <summary>
/// Payload for <see cref="DiffViewer.Services.IPullRequestWatcher.Changed"/>.
/// Carries everything the coordinator needs to decide how to react:
/// the change kind bitmask, the freshly-polled PR metadata (or
/// <c>null</c> on failure), the new ref snapshot (or <c>null</c> when
/// nothing was fetched), an optional failure message for the terminal
/// <see cref="PullRequestChangeKind.PollFailed"/> path, and a UTC
/// timestamp suitable for toast wording.
/// </summary>
/// <param name="Kind">What changed.</param>
/// <param name="NewInfo">The freshly-polled PR metadata; <c>null</c>
/// on <see cref="PullRequestChangeKind.PollFailed"/>.</param>
/// <param name="NewSnapshot">The freshly-resolved snapshot (head SHA
/// + merge-base SHA); non-null only when SHAs actually moved. Lets the
/// coordinator hand a pre-resolved <c>ParsedCommandLine</c> to the
/// rebuild path without re-running the resolver.</param>
/// <param name="FailureMessage">User-actionable error message; set
/// only on <see cref="PullRequestChangeKind.PollFailed"/>.</param>
/// <param name="UtcTimestamp">When the change was observed.</param>
public sealed record PullRequestChangedEventArgs(
    PullRequestChangeKind Kind,
    PullRequestInfo? NewInfo,
    RemoteRefSnapshot? NewSnapshot,
    string? FailureMessage,
    System.DateTime UtcTimestamp);
