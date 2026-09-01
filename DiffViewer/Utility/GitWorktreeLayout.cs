using System;
using System.IO;

namespace DiffViewer.Utility;

/// <summary>
/// Pure path/file reasoning about git's worktree on-disk layout. No
/// libgit2 dependency, so it is cheap to call and trivially testable.
///
/// <para><b>The layout</b>: a repository has one <em>common
/// directory</em> — the <c>.git</c> directory holding the object
/// database and shared refs. The main worktree's git directory
/// <em>is</em> the common directory. Each linked worktree instead gets
/// its own git directory at <c>&lt;common&gt;/worktrees/&lt;name&gt;</c>,
/// containing a <c>commondir</c> file that points back (normally the
/// relative string <c>../..</c>). The presence of that file is the
/// definitive "am I a linked worktree" test.</para>
/// </summary>
public static class GitWorktreeLayout
{
    private const string CommonDirFileName = "commondir";
    private const string GitDirectoryName = ".git";

    /// <summary>
    /// True when <paramref name="gitDirectory"/> is a linked worktree's
    /// git directory rather than a repository's common directory.
    /// </summary>
    public static bool IsLinkedWorktree(string gitDirectory)
    {
        if (string.IsNullOrWhiteSpace(gitDirectory)) return false;
        try
        {
            return File.Exists(Path.Combine(gitDirectory, CommonDirFileName));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Resolve the repository's common directory. Returns
    /// <paramref name="gitDirectory"/> unchanged when it is already the
    /// common directory, or when the <c>commondir</c> pointer is
    /// unreadable or empty.
    /// </summary>
    public static string ResolveCommonDirectory(string gitDirectory)
    {
        if (string.IsNullOrWhiteSpace(gitDirectory)) return gitDirectory;

        try
        {
            var commonDirFile = Path.Combine(gitDirectory, CommonDirFileName);
            if (!File.Exists(commonDirFile)) return gitDirectory;

            var contents = File.ReadAllText(commonDirFile).Trim();
            if (contents.Length == 0) return gitDirectory;

            // The pointer is normally relative to the worktree's git
            // directory; Path.Combine leaves an absolute pointer alone.
            return Path.GetFullPath(Path.Combine(gitDirectory, contents));
        }
        catch (Exception)
        {
            return gitDirectory;
        }
    }

    /// <summary>
    /// Git's name for the worktree owning <paramref name="gitDirectory"/>,
    /// or <c>null</c> when it is not a linked worktree. The name is the
    /// directory leaf under <c>&lt;common&gt;/worktrees</c> — normally,
    /// but not necessarily, the leaf of the worktree's working
    /// directory.
    /// </summary>
    public static string? TryGetWorktreeName(string gitDirectory)
    {
        if (!IsLinkedWorktree(gitDirectory)) return null;

        try
        {
            var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gitDirectory));
            var leaf = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(leaf) ? null : leaf;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Describe the repository at <paramref name="workingDirectory"/> in
    /// the terms a user recognizes: the repository's own name, and the
    /// name of the worktree they are looking at (or <c>null</c> when it
    /// is the main one). Both are <c>null</c> when the path is not a
    /// git working directory.
    ///
    /// <para>Deliberately libgit2-free — this runs on the recents
    /// write path for every launch, and a handful of file probes is
    /// much cheaper than opening a repository handle.</para>
    /// </summary>
    public static WorktreeLabels Describe(string workingDirectory)
    {
        var gitDirectory = TryResolveGitDirectory(workingDirectory);
        if (gitDirectory is null) return WorktreeLabels.None;

        return new WorktreeLabels(
            RepositoryName: TryGetRepositoryName(ResolveCommonDirectory(gitDirectory)),
            WorktreeName: TryGetWorktreeName(gitDirectory));
    }

    /// <summary>
    /// Resolve a working directory's git directory. For an ordinary
    /// checkout <c>.git</c> is a directory. For a linked worktree it is
    /// instead a file containing <c>gitdir: &lt;path&gt;</c>, pointing at
    /// the worktree's administrative directory. Returns <c>null</c> when
    /// neither shape is found.
    /// </summary>
    public static string? TryResolveGitDirectory(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory)) return null;

        try
        {
            var candidate = Path.Combine(workingDirectory, GitDirectoryName);
            if (Directory.Exists(candidate)) return candidate;
            if (!File.Exists(candidate)) return null;

            var contents = File.ReadAllText(candidate).Trim();
            const string pointerPrefix = "gitdir:";
            if (!contents.StartsWith(pointerPrefix, StringComparison.OrdinalIgnoreCase)) return null;

            var target = contents[pointerPrefix.Length..].Trim();
            if (target.Length == 0) return null;

            return Path.GetFullPath(Path.Combine(workingDirectory, target));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The repository's own name, derived from its common directory —
    /// the name a user would call the project, shared by every worktree.
    ///
    /// <para>Two layouts are handled. In the usual one the common
    /// directory is <c>&lt;repo&gt;/.git</c>, so the name is the parent
    /// leaf. In the "bare hub plus N worktrees" layout the common
    /// directory is the bare repository itself (conventionally
    /// <c>&lt;name&gt;.git</c>), so the name is that leaf with the
    /// suffix trimmed.</para>
    /// </summary>
    public static string? TryGetRepositoryName(string commonGitDirectory)
    {
        if (string.IsNullOrWhiteSpace(commonGitDirectory)) return null;

        try
        {
            var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(commonGitDirectory));
            var leaf = Path.GetFileName(trimmed);

            if (string.Equals(leaf, GitDirectoryName, StringComparison.OrdinalIgnoreCase))
            {
                var parent = Path.GetDirectoryName(trimmed);
                if (string.IsNullOrEmpty(parent)) return null;
                var parentLeaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(parent));
                return string.IsNullOrEmpty(parentLeaf) ? parent : parentLeaf;
            }

            if (string.IsNullOrEmpty(leaf)) return null;

            return leaf.EndsWith(GitDirectoryName, StringComparison.OrdinalIgnoreCase)
                   && leaf.Length > GitDirectoryName.Length
                ? leaf[..^GitDirectoryName.Length]
                : leaf;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// Human-facing names for a checkout: the repository it belongs to, and
/// which worktree of that repository it is.
/// </summary>
/// <param name="RepositoryName">The repository's name, shared by every
/// worktree (e.g. <c>DiffViewer</c>), or <c>null</c> when it could not
/// be determined.</param>
/// <param name="WorktreeName">Git's name for this linked worktree, or
/// <c>null</c> when this is the main worktree or the path is not a
/// repository.</param>
public sealed record WorktreeLabels(string? RepositoryName, string? WorktreeName)
{
    /// <summary>Nothing could be determined about the path.</summary>
    public static WorktreeLabels None { get; } = new(null, null);
}
