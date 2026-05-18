using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Seam over the parser-side validation primitives so the "New diff"
/// dialog can validate user input the same way the command-line parser
/// does — without re-implementing path discovery, commit-ish resolution,
/// or PR-URL parsing in the view-model layer.
///
/// <para>Each method returns a small discriminated record so the form
/// view-model can present a precise error message without an out-param
/// dance. Production implementation: <see cref="DiffLaunchValidator"/>;
/// it wraps a <see cref="ICommandLineEnvironment"/> and reuses the same
/// disambiguation rules as <see cref="CommandLineParser"/>.</para>
/// </summary>
public interface IDiffLaunchValidator
{
    /// <summary>
    /// Validate that <paramref name="raw"/> identifies (or sits inside)
    /// a git repository on disk. Returns the working-tree root on
    /// success — that's what should flow into <see cref="ParsedCommandLine"/>.
    /// </summary>
    RepoPathValidation ValidateRepoPath(string raw);

    /// <summary>
    /// Validate that <paramref name="commitIsh"/> resolves in the repo
    /// rooted at <paramref name="canonicalRepoPath"/>. Caller is
    /// expected to have already vetted the repo path via
    /// <see cref="ValidateRepoPath"/>.
    /// </summary>
    CommitIshValidation ValidateCommitIsh(string canonicalRepoPath, string commitIsh);

    /// <summary>
    /// Parse <paramref name="url"/> as a GitHub pull-request URL. Delegates
    /// to <see cref="PullRequestRef.TryParse"/>; lifted into this seam so
    /// the form view-model can stay testable without exercising the
    /// model-layer parser directly.
    /// </summary>
    PullRequestUrlValidation ValidatePullRequestUrl(string url);
}

/// <summary>Result of <see cref="IDiffLaunchValidator.ValidateRepoPath"/>.</summary>
public abstract record RepoPathValidation
{
    /// <summary>The path is a git repo (or sits inside one).
    /// <see cref="CanonicalPath"/> is the resolved working-tree root.</summary>
    public sealed record Valid(string CanonicalPath) : RepoPathValidation;

    /// <summary>The path is empty, doesn't exist, or has no enclosing repo.</summary>
    public sealed record Invalid(string Message) : RepoPathValidation;

    private RepoPathValidation() { }
}

/// <summary>Result of <see cref="IDiffLaunchValidator.ValidateCommitIsh"/>.</summary>
public abstract record CommitIshValidation
{
    public sealed record Valid : CommitIshValidation;
    public sealed record Invalid(string Message) : CommitIshValidation;
    private CommitIshValidation() { }
}

/// <summary>Result of <see cref="IDiffLaunchValidator.ValidatePullRequestUrl"/>.</summary>
public abstract record PullRequestUrlValidation
{
    public sealed record Valid(PullRequestRef Pr) : PullRequestUrlValidation;
    public sealed record Invalid(string Message) : PullRequestUrlValidation;
    private PullRequestUrlValidation() { }
}
