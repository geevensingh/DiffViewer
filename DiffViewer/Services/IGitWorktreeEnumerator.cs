using System.Collections.Generic;

namespace DiffViewer.Services;

/// <summary>
/// Stateless, repo-path-keyed enumeration of every worktree attached to
/// a Git repository — the main worktree plus every linked worktree
/// created by <c>git worktree add</c>.
///
/// <para><b>Why a sibling to <see cref="IGitRefEnumerator"/> rather than
/// a member of it</b>: refs and worktrees answer different questions and
/// have different failure modes. Ref enumeration reads the object
/// database; worktree enumeration reads administrative files under
/// <c>.git/worktrees</c> and stats directories that may no longer
/// exist. Keeping them apart lets the worktree side own its
/// "prunable entry" semantics without complicating the ref result
/// shape.</para>
///
/// <para><b>Error policy</b> matches <see cref="IGitRefEnumerator"/>:
/// invalid paths, non-repos, and libgit2 errors return an empty list,
/// never a throw. A single unreadable worktree degrades to an entry
/// with <see cref="WorktreeEntry.IsMissing"/> set rather than losing
/// the whole enumeration.</para>
/// </summary>
public interface IGitWorktreeEnumerator
{
    /// <summary>
    /// Enumerate every worktree attached to the repository containing
    /// <paramref name="canonicalRepoPath"/>. The main worktree sorts
    /// first; linked worktrees follow, ordered by
    /// <see cref="WorktreeEntry.Name"/>. Returns an empty list (never
    /// <c>null</c>) when the path doesn't resolve or libgit2 throws.
    /// </summary>
    IReadOnlyList<WorktreeEntry> Enumerate(string canonicalRepoPath);
}

/// <summary>
/// One worktree returned by <see cref="IGitWorktreeEnumerator.Enumerate"/>.
/// </summary>
/// <param name="Name">Display name. For linked worktrees this is git's
/// own worktree name (the directory name under <c>.git/worktrees</c>,
/// which is normally — but not necessarily — the leaf of
/// <paramref name="WorkingDirectory"/>). Git does not name the main
/// worktree, so the main entry uses the leaf of its working
/// directory.</param>
/// <param name="WorkingDirectory">Absolute path to the worktree's
/// working directory, canonicalized with any trailing separator
/// trimmed so it compares equal to a
/// <see cref="DiffViewer.Models.ContextIdentity"/> repo path. For a
/// missing worktree this is the last known path — the directory is
/// gone, but the path is still what the administrative files
/// record.</param>
/// <param name="HeadFriendlyName">Friendly name of the branch checked
/// out in this worktree (e.g. <c>master</c>), or <c>null</c> when HEAD
/// is detached, unborn, or unreadable. Git forbids checking the same
/// branch out in two worktrees, so this doubles as a natural
/// disambiguator in pickers.</param>
/// <param name="IsMain">True for the repository's main worktree — the
/// one whose <c>.git</c> is a real directory rather than a pointer
/// file. False for every linked worktree.</param>
/// <param name="IsCurrent">True when this entry is the worktree that
/// the queried path itself resolved to.</param>
/// <param name="IsLocked">True when the worktree is locked
/// (<c>git worktree lock</c>), meaning git will refuse to prune it.</param>
/// <param name="IsMissing">True when the working directory no longer
/// exists on disk — git calls this "prunable". Surfaced rather than
/// filtered so a picker can show it as unavailable instead of
/// silently dropping a worktree the user believes exists.</param>
public sealed record WorktreeEntry(
    string Name,
    string WorkingDirectory,
    string? HeadFriendlyName,
    bool IsMain,
    bool IsCurrent,
    bool IsLocked,
    bool IsMissing);
