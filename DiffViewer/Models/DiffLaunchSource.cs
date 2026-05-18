namespace DiffViewer.Models;

/// <summary>
/// Discriminated union of "ways the user can ask the app to load a diff".
/// Drives <see cref="DiffViewer.IContextSwitcher.SwitchToAsync"/> so the
/// coordinator dispatches on a single tagged input rather than growing
/// one typed method per mode.
///
/// <para>Today's variants: <see cref="Local"/> (a fully-parsed local-repo
/// command line — covers working-tree-vs-HEAD, working-tree-vs-commit,
/// and commit-vs-commit) and <see cref="GitHubPullRequest"/> (an
/// unresolved PR reference; the coordinator runs it through
/// <see cref="DiffViewer.Services.IPullRequestResolver"/> at switch
/// time). Adding a new mode is one new <c>sealed record</c> nested
/// here + one new dispatch case in the coordinator + one provider
/// class — no changes ripple through the recents pipeline or the
/// dialog host.</para>
/// </summary>
public abstract record DiffLaunchSource
{
    /// <summary>
    /// A fully-parsed local-repo launch. Carries a
    /// <see cref="ParsedCommandLine"/> the coordinator can hand
    /// straight to <see cref="DiffViewer.CompositionRoot.BuildContextAsync"/>.
    /// </summary>
    public sealed record Local(ParsedCommandLine Parsed) : DiffLaunchSource;

    /// <summary>
    /// An unresolved GitHub PR reference. The coordinator resolves it
    /// via <see cref="DiffViewer.Services.IPullRequestResolver"/> at
    /// switch time, opening the missing-clone dialog if the local
    /// clone cannot be found.
    /// </summary>
    public sealed record GitHubPullRequest(PullRequestRef Pr) : DiffLaunchSource;

    private DiffLaunchSource() { }
}
