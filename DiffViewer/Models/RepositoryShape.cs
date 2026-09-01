namespace DiffViewer.Models;

/// <summary>
/// Static facts about a repository captured at open time. Drives the
/// command-line dispatch (e.g. reject working-tree input modes against a
/// bare repo) and gates expensive features like the eager pre-diff pass
/// on partial-clone repos.
/// </summary>
/// <param name="RepoRoot">Absolute path to the repo root (or the <c>.git</c> dir for bare repos).</param>
/// <param name="WorkingDirectory">Absolute path to the working tree, or <c>null</c> for bare repos.</param>
/// <param name="GitDir">Absolute path to the actual <c>.git</c> dir (resolves linked-worktree pointers).</param>
/// <param name="IsBare">True if the repo is bare (no working tree).</param>
/// <param name="IsHeadUnborn">True if there are no commits yet.</param>
/// <param name="IsSparseCheckout">True if <c>core.sparseCheckout=true</c>.</param>
/// <param name="IsPartialClone">True if any remote has <c>promisor=true</c>.</param>
/// <param name="HasInProgressOperation">True if a merge / rebase / cherry-pick / revert / stash-pop is in progress.</param>
/// <param name="CommonGitDirectory">Absolute path to the repository's
/// <em>common</em> git directory — the one holding the object database
/// and shared refs. Equal to <paramref name="GitDir"/> for a main
/// worktree; for a linked worktree it is the shared directory the
/// worktree points back at, and so is the same value for every worktree
/// of a repository. This is what identifies "the same repo" across
/// worktrees.</param>
/// <param name="WorktreeName">Git's name for this linked worktree, or
/// <c>null</c> when this is the repository's main worktree.</param>
public sealed record RepositoryShape(
    string RepoRoot,
    string? WorkingDirectory,
    string GitDir,
    bool IsBare,
    bool IsHeadUnborn,
    bool IsSparseCheckout,
    bool IsPartialClone,
    bool HasInProgressOperation,
    string CommonGitDirectory,
    string? WorktreeName = null)
{
    /// <summary>
    /// True when this repository was opened through a linked worktree
    /// (<c>git worktree add</c>) rather than its main checkout.
    /// </summary>
    public bool IsLinkedWorktree => WorktreeName is not null;
}
