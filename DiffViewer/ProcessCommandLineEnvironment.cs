using System.IO;
using DiffViewer.Services;
using LibGit2Sharp;

namespace DiffViewer;

/// <summary>
/// Production <see cref="ICommandLineEnvironment"/> backed by the real
/// process. Parser tests use <c>StubEnv</c> instead.
/// </summary>
internal sealed class ProcessCommandLineEnvironment : ICommandLineEnvironment
{
    public string CurrentDirectory => Directory.GetCurrentDirectory();

    public bool PathExists(string path) =>
        Directory.Exists(path) || File.Exists(path);

    public bool IsGitRepository(string path)
    {
        try
        {
            return Repository.IsValid(path);
        }
        catch
        {
            return false;
        }
    }

    public bool TryResolveCommitIsh(string repoPath, string commitIsh)
    {
        try
        {
            using var repo = new Repository(repoPath);
            // Fast path: SHAs, branch names, and revspecs like HEAD~3
            // resolve directly to a Commit. This single call already
            // handles the vast majority of inputs the CLI parser and
            // the New Diff dialog hand us.
            if (repo.Lookup<Commit>(commitIsh) is not null) return true;
            // Annotated-tag fallback. An annotated tag (e.g. one made
            // with `git tag -a v1.2.0 -m ...`) points at a
            // TagAnnotation object that wraps the underlying Commit;
            // the generic Lookup<Commit> above returns null for it
            // because the immediate ref target is the wrapper, not the
            // commit. Peel<Commit> follows the wrapper through to the
            // commit. Lightweight tags already point straight at the
            // commit and were caught by the fast path. This call
            // matches how LibGit2GitRefEnumerator surfaces tags into
            // the picker, so any tag the user can pick from the popup
            // will also pass validation here. Peel throws on
            // un-peelable objects (trees, blobs); the outer try/catch
            // swallows that as a non-resolving input.
            var obj = repo.Lookup(commitIsh);
            return obj?.Peel<Commit>() is not null;
        }
        catch
        {
            return false;
        }
    }

    public string? TryDiscoverRepoRoot(string path)
    {
        try
        {
            // Repository.Discover walks upward looking for a .git dir (and
            // handles linked worktrees and submodules). Returns the .git
            // directory path (or the bare repo dir), trailing slash, or
            // null when nothing's found.
            var gitDir = Repository.Discover(path);
            if (string.IsNullOrEmpty(gitDir)) return null;

            using var repo = new Repository(gitDir);
            if (repo.Info.IsBare) return null;

            var workDir = repo.Info.WorkingDirectory;
            if (string.IsNullOrEmpty(workDir)) return null;

            return workDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }
}
