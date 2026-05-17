namespace DiffViewer.Models;

/// <summary>
/// Discriminated result of <see cref="DiffViewer.Services.ICommandLineParser.ParseLaunch"/>.
/// Exactly one of the three properties is non-null.
/// </summary>
/// <remarks>
/// <para>
/// Local-mode launches carry a fully-resolved <see cref="ParsedCommandLine"/>.
/// PR-mode launches carry an unresolved <see cref="Models.PullRequestRef"/>;
/// the actual repo path and side SHAs are resolved by the PR resolver at
/// launch time. Errors carry a structured <see cref="CommandLineError"/>.
/// </para>
/// <para>
/// PR URLs are kept out of <see cref="ParsedCommandLine"/> deliberately:
/// pushing them through would force that type to carry both
/// "resolved repo+sides" and "unresolved PR ref" states, breaking its
/// single-purpose invariant.
/// </para>
/// </remarks>
public sealed record CommandLineLaunchPlan(
    ParsedCommandLine? Local,
    PullRequestRef? PullRequest,
    CommandLineError? Error)
{
    public bool IsLocal => Local is not null;
    public bool IsPullRequest => PullRequest is not null;
    public bool IsError => Error is not null;

    public static CommandLineLaunchPlan FromLocal(ParsedCommandLine parsed) =>
        new(parsed, null, null);

    public static CommandLineLaunchPlan FromPullRequest(PullRequestRef pr) =>
        new(null, pr, null);

    public static CommandLineLaunchPlan Failure(CommandLineErrorKind kind, string message) =>
        new(null, null, new CommandLineError(kind, message));
}
