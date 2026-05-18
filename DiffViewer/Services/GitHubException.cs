namespace DiffViewer.Services;

/// <summary>
/// User-actionable error from <see cref="IGitHubClient"/>. The
/// <see cref="Exception.Message"/> is the message we display to the user
/// (after wrapping in <c>MainWindowCoordinator.HandleColdLaunchFailure</c>),
/// so it must be specific and self-contained — no internal jargon, no raw
/// HTTP body dumps, but enough hint for the user to know what to do next
/// (refresh gh, check network, ask for repo access, etc.).
/// </summary>
public sealed class GitHubException : Exception
{
    public GitHubException(string message) : base(message)
    {
    }

    public GitHubException(string message, Exception inner) : base(message, inner)
    {
    }
}
