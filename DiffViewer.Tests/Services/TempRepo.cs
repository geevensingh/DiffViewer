using System.IO;
using System.Text;
using LibGit2Sharp;

namespace DiffViewer.Tests.Services;

/// <summary>
/// Helper that creates a real on-disk LibGit2Sharp repository in a temp folder
/// for the duration of a single test. Disposed via xUnit's IDisposable hook
/// in the test class.
/// </summary>
internal sealed class TempRepo : IDisposable
{
    private readonly string _tempPath;
    private readonly Signature _author = new("Test", "test@example.com", DateTimeOffset.UtcNow);

    public string Path => _tempPath;
    public Signature Author => _author;

    public TempRepo()
    {
        _tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "diffviewer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempPath);
        Repository.Init(_tempPath);

        // Pin core.autocrlf=false so tests get byte-exact line endings
        // regardless of the user's global git config (Git for Windows
        // installer default is autocrlf=true, which would confuse round-
        // trip assertions in GitWriteServiceTests).
        using var repo = new Repository(_tempPath);
        repo.Config.Set("core.autocrlf", "false");
    }

    public void WriteFile(string relativePath, string content, Encoding? encoding = null)
    {
        var full = System.IO.Path.Combine(_tempPath, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public void WriteBytes(string relativePath, byte[] bytes)
    {
        var full = System.IO.Path.Combine(_tempPath, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    public void DeleteWorkingFile(string relativePath)
    {
        var full = System.IO.Path.Combine(_tempPath, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (File.Exists(full)) File.Delete(full);
    }

    public void Stage(params string[] paths)
    {
        using var repo = new Repository(_tempPath);
        foreach (var p in paths)
        {
            repo.Index.Add(p);
        }
        repo.Index.Write();
    }

    public void Unstage(params string[] paths)
    {
        using var repo = new Repository(_tempPath);
        foreach (var p in paths)
        {
            repo.Index.Remove(p);
        }
        repo.Index.Write();
    }

    public Commit Commit(string message)
    {
        using var repo = new Repository(_tempPath);
        Commands.Stage(repo, "*");
        return repo.Commit(message, _author, _author, new CommitOptions { AllowEmptyCommit = true });
    }

    public Commit InitialCommit(string message = "init")
    {
        using var repo = new Repository(_tempPath);
        Commands.Stage(repo, "*");
        return repo.Commit(message, _author, _author);
    }

    /// <summary>Create a local branch at a specific commit and leave HEAD
    /// where it is. Use <see cref="Checkout"/> if you want to move HEAD
    /// onto the new branch.</summary>
    public void CreateBranch(string name, Commit commit)
    {
        using var repo = new Repository(_tempPath);
        repo.CreateBranch(name, commit);
    }

    /// <summary>Move HEAD onto the named branch.</summary>
    public void Checkout(string branchName)
    {
        using var repo = new Repository(_tempPath);
        var branch = repo.Branches[branchName]
            ?? throw new InvalidOperationException($"Branch '{branchName}' not found.");
        Commands.Checkout(repo, branch);
    }

    /// <summary>Create a lightweight tag pointing at <paramref name="commit"/>.</summary>
    public void CreateLightweightTag(string name, Commit commit)
    {
        using var repo = new Repository(_tempPath);
        repo.ApplyTag(name, commit.Sha);
    }

    /// <summary>Create an annotated tag pointing at <paramref name="commit"/>
    /// (the kind <c>git tag -a</c> creates — its object is a wrapper that
    /// then peels to the commit).</summary>
    public void CreateAnnotatedTag(string name, Commit commit, string message)
    {
        using var repo = new Repository(_tempPath);
        repo.ApplyTag(name, commit.Sha, _author, message);
    }

    /// <summary>Force-create a remote-tracking branch ref
    /// (<c>refs/remotes/{remote}/{branch}</c>) pointing at the given commit.
    /// This sidesteps the need for an actual remote with a working network
    /// connection — just installs the ref directly.</summary>
    public void CreateRemoteTrackingBranch(string remote, string branch, Commit commit)
    {
        using var repo = new Repository(_tempPath);
        repo.Refs.Add($"refs/remotes/{remote}/{branch}", commit.Sha);
    }

    /// <summary>Install the symbolic ref <c>refs/remotes/{remote}/HEAD</c>
    /// pointing at the given target branch (e.g. <c>refs/remotes/origin/main</c>).
    /// Mirrors what <c>git clone</c> does at the end of a clone when the
    /// remote advertises a HEAD branch; tests that exercise
    /// "default-branch detection" use this to set up the precondition
    /// without spinning up a real remote.</summary>
    public void SetRemoteHead(string remote, string targetBranch)
    {
        using var repo = new Repository(_tempPath);
        var name = $"refs/remotes/{remote}/HEAD";
        var target = $"refs/remotes/{remote}/{targetBranch}";
        // allowOverwrite=true so a test can rewire HEAD after first set.
        repo.Refs.Add(name, target, allowOverwrite: true);
    }

    /// <summary>Create a stash from the current working-tree state.
    /// Requires at least one commit on HEAD and at least one dirty
    /// tracked file. Returns the stash's working-tree <see cref="Commit"/>
    /// (the commit <c>stash@{0}</c> resolves to).</summary>
    public Commit Stash(string? message = null)
    {
        using var repo = new Repository(_tempPath);
        var stash = repo.Stashes.Add(
            _author,
            message ?? $"WIP stash at {DateTimeOffset.UtcNow:O}");
        return stash.WorkTree;
    }

    public void Dispose()
    {
        try
        {
            // LibGit2Sharp marks .git internals read-only on Windows; clear before delete.
            if (Directory.Exists(_tempPath))
            {
                foreach (var f in Directory.EnumerateFiles(_tempPath, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(_tempPath, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup - don't fail the test on temp-dir leak.
        }
    }
}
