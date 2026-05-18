using System;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="IDiffLaunchValidator"/>. Wraps a
/// <see cref="ICommandLineEnvironment"/> and re-uses the same
/// path-discovery + commit-ish lookup primitives the CLI parser uses,
/// so input typed into the "New diff" dialog is validated identically
/// to input passed on the command line.
/// </summary>
public sealed class DiffLaunchValidator : IDiffLaunchValidator
{
    private readonly ICommandLineEnvironment _env;

    public DiffLaunchValidator(ICommandLineEnvironment env)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
    }

    public RepoPathValidation ValidateRepoPath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new RepoPathValidation.Invalid("Repository path is empty.");
        }

        if (!_env.PathExists(raw))
        {
            return new RepoPathValidation.Invalid($"Path does not exist: {raw}");
        }

        if (_env.IsGitRepository(raw))
        {
            return new RepoPathValidation.Valid(raw);
        }

        // CLI parser semantics: if the path isn't a repo root, try
        // walking upward. This makes "C:\repos\foo\src\sub" accept and
        // resolve back to the enclosing repo root.
        var discovered = _env.TryDiscoverRepoRoot(raw);
        if (discovered is null)
        {
            return new RepoPathValidation.Invalid(
                $"Not a git repository (and no enclosing repo was found): {raw}");
        }
        return new RepoPathValidation.Valid(discovered);
    }

    public CommitIshValidation ValidateCommitIsh(string canonicalRepoPath, string commitIsh)
    {
        if (string.IsNullOrWhiteSpace(canonicalRepoPath))
        {
            return new CommitIshValidation.Invalid("Repository path is empty.");
        }
        if (string.IsNullOrWhiteSpace(commitIsh))
        {
            return new CommitIshValidation.Invalid("Commit-ish is empty.");
        }
        if (!_env.TryResolveCommitIsh(canonicalRepoPath, commitIsh))
        {
            return new CommitIshValidation.Invalid(
                $"Cannot resolve `{commitIsh}` in repo {canonicalRepoPath}.");
        }
        return new CommitIshValidation.Valid();
    }

    public PullRequestUrlValidation ValidatePullRequestUrl(string url)
    {
        if (PullRequestRef.TryParse(url, out var pr, out var error))
        {
            return new PullRequestUrlValidation.Valid(pr);
        }
        return new PullRequestUrlValidation.Invalid(error);
    }
}
