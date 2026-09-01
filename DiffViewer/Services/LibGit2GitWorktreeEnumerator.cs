using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiffViewer.Models;
using DiffViewer.Utility;
using LibGit2Sharp;

namespace DiffViewer.Services;

/// <summary>
/// LibGit2Sharp-backed <see cref="IGitWorktreeEnumerator"/>. Opens,
/// reads, closes — short-lived <see cref="Repository"/> handles per
/// call. Safe to construct as an app-level singleton (no mutable state).
///
/// <para><b>The main worktree has to be synthesized.</b> libgit2's
/// worktree list — like <c>git worktree list --porcelain</c>'s
/// underlying data — enumerates only <em>linked</em> worktrees. The main
/// worktree is found instead by resolving the repository's common
/// directory: a linked worktree's git dir is
/// <c>&lt;common&gt;/worktrees/&lt;name&gt;</c> and contains a
/// <c>commondir</c> file pointing back (usually the relative string
/// <c>../..</c>). Opening that common directory yields the main
/// worktree's working directory and HEAD.</para>
///
/// <para><b>Bare hubs are supported.</b> The "one bare clone plus N
/// worktrees" layout has no main working directory at all; libgit2
/// reports a null working directory for the common dir and this
/// implementation simply omits the main entry rather than inventing
/// a path.</para>
/// </summary>
public sealed class LibGit2GitWorktreeEnumerator : IGitWorktreeEnumerator
{
    public IReadOnlyList<WorktreeEntry> Enumerate(string canonicalRepoPath)
    {
        if (string.IsNullOrWhiteSpace(canonicalRepoPath)) return Array.Empty<WorktreeEntry>();

        try
        {
            if (!Repository.IsValid(canonicalRepoPath)) return Array.Empty<WorktreeEntry>();

            using var repo = new Repository(canonicalRepoPath);

            var gitDir = repo.Info.Path;
            var commonDir = GitWorktreeLayout.ResolveCommonDirectory(gitDir);
            var currentWorkingDirectory = Canonicalize(repo.Info.WorkingDirectory);

            var entries = new List<WorktreeEntry>();

            var main = BuildMainEntry(repo, gitDir, commonDir, currentWorkingDirectory);
            if (main is not null) entries.Add(main);

            entries.AddRange(
                BuildLinkedEntries(repo, commonDir, currentWorkingDirectory)
                    .OrderBy(e => e.Name, StringComparer.Ordinal));

            return entries;
        }
        catch (Exception)
        {
            return Array.Empty<WorktreeEntry>();
        }
    }

    /// <summary>
    /// Build the main-worktree entry, or <c>null</c> when the repository
    /// has no main working directory (a bare hub). When the queried repo
    /// <em>is</em> the main worktree, its already-open handle is reused
    /// rather than reopening the same directory.
    /// </summary>
    private static WorktreeEntry? BuildMainEntry(
        Repository repo,
        string gitDir,
        string commonDir,
        string? currentWorkingDirectory)
    {
        var queriedRepoIsMain = PathsEqual(Canonicalize(gitDir), Canonicalize(commonDir));
        if (queriedRepoIsMain)
        {
            return DescribeMain(repo, currentWorkingDirectory);
        }

        try
        {
            using var mainRepo = new Repository(commonDir);
            return DescribeMain(mainRepo, currentWorkingDirectory);
        }
        catch (Exception)
        {
            // The common directory is unreadable or no longer a valid
            // repository. Linked worktrees may still enumerate, so
            // degrade to "no main entry" rather than failing the call.
            return null;
        }
    }

    private static WorktreeEntry? DescribeMain(Repository mainRepo, string? currentWorkingDirectory)
    {
        var workingDirectory = Canonicalize(mainRepo.Info.WorkingDirectory);
        if (workingDirectory is null) return null;

        return new WorktreeEntry(
            Name: LeafName(workingDirectory),
            WorkingDirectory: workingDirectory,
            HeadFriendlyName: TryReadHeadName(mainRepo),
            IsMain: true,
            IsCurrent: PathsEqual(workingDirectory, currentWorkingDirectory),
            IsLocked: false,
            IsMissing: !Directory.Exists(workingDirectory));
    }

    /// <summary>
    /// Describe every linked worktree.
    ///
    /// <para><b>Names come from the administrative directory, not from
    /// libgit2.</b> A worktree whose directory has been deleted still
    /// appears in libgit2's worktree list, but every property on it
    /// throws — the underlying lookup fails and LibGit2Sharp dereferences
    /// the null. Enumerating <c>&lt;common&gt;/worktrees/*</c> instead
    /// gives a name for every worktree git knows about, healthy or
    /// prunable, and libgit2 is then consulted only for the details it
    /// can actually supply.</para>
    /// </summary>
    private static IEnumerable<WorktreeEntry> BuildLinkedEntries(
        Repository repo,
        string commonDir,
        string? currentWorkingDirectory)
    {
        string[] adminDirectories;
        try
        {
            var worktreesRoot = Path.Combine(commonDir, "worktrees");
            if (!Directory.Exists(worktreesRoot)) yield break;
            adminDirectories = Directory.GetDirectories(worktreesRoot);
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (var adminDirectory in adminDirectories)
        {
            var name = Path.GetFileName(adminDirectory);
            if (string.IsNullOrEmpty(name)) continue;

            var entry = TryDescribeLinked(repo, adminDirectory, name, currentWorkingDirectory);
            if (entry is not null) yield return entry;
        }
    }

    private static WorktreeEntry? TryDescribeLinked(
        Repository repo,
        string adminDirectory,
        string name,
        string? currentWorkingDirectory)
    {
        string? workingDirectory = null;
        string? headName = null;
        bool? isLocked = null;

        try
        {
            var worktree = repo.Worktrees[name];
            if (worktree is not null)
            {
                isLocked = worktree.IsLocked;
                using var worktreeRepo = worktree.WorktreeRepository;
                workingDirectory = Canonicalize(worktreeRepo.Info.WorkingDirectory);
                headName = TryReadHeadName(worktreeRepo);
            }
        }
        catch (Exception)
        {
            // Pruned worktree: libgit2 can't look it up, so fall through
            // to the administrative files, which survive the deletion.
        }

        workingDirectory ??= TryReadAdministrativeWorkingDirectory(adminDirectory);
        if (workingDirectory is null) return null;

        return new WorktreeEntry(
            Name: name,
            WorkingDirectory: workingDirectory,
            HeadFriendlyName: headName,
            IsMain: false,
            IsCurrent: PathsEqual(workingDirectory, currentWorkingDirectory),
            IsLocked: isLocked ?? AdministrativeLockExists(adminDirectory),
            IsMissing: !Directory.Exists(workingDirectory));
    }

    /// <summary>
    /// Recover a linked worktree's working directory from
    /// <c>&lt;admin&gt;/gitdir</c>, which records the path of the
    /// worktree's <c>.git</c> pointer file. Its parent directory is the
    /// working directory. This is the only source of truth once the
    /// worktree directory itself is gone.
    /// </summary>
    private static string? TryReadAdministrativeWorkingDirectory(string adminDirectory)
    {
        try
        {
            var gitDirFile = Path.Combine(adminDirectory, "gitdir");
            if (!File.Exists(gitDirFile)) return null;

            var pointer = File.ReadAllText(gitDirFile).Trim();
            if (pointer.Length == 0) return null;

            var parent = Path.GetDirectoryName(Path.GetFullPath(pointer));
            return Canonicalize(parent);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Lock state for a worktree libgit2 could not look up. Git records
    /// a lock as the presence of a <c>locked</c> file in the worktree's
    /// administrative directory.
    /// </summary>
    private static bool AdministrativeLockExists(string adminDirectory)
    {
        try
        {
            return File.Exists(Path.Combine(adminDirectory, "locked"));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Friendly branch name of a worktree's HEAD, or <c>null</c> when
    /// HEAD is detached, unborn, or unreadable. A detached HEAD's
    /// friendly name is the useless literal <c>"(no branch)"</c>, so it
    /// is normalized away here rather than in the UI.
    /// </summary>
    private static string? TryReadHeadName(Repository repo)
    {
        try
        {
            if (repo.Info.IsHeadDetached || repo.Info.IsHeadUnborn) return null;
            var name = repo.Head?.FriendlyName;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Canonicalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return ContextIdentityFactory.CanonicalizeRepoPath(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string LeafName(string path)
    {
        var leaf = Path.GetFileName(path);
        return string.IsNullOrEmpty(leaf) ? path : leaf;
    }

    private static bool PathsEqual(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
