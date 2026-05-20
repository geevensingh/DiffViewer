using LibGit2Sharp;

namespace DiffViewer.Services;

/// <summary>
/// Resolves a commit-ish (SHA, branch name, revspec like
/// <c>HEAD~3</c>, lightweight tag, annotated tag, etc.) to the
/// underlying <see cref="Commit"/> object.
/// </summary>
/// <remarks>
/// <para>
/// LibGit2Sharp's generic <c>Lookup&lt;Commit&gt;(string)</c> handles
/// most inputs in one cheap call but returns <c>null</c> for
/// annotated tags: <c>git tag -a name -m msg</c>-style tags have
/// their ref target a <see cref="TagAnnotation"/> object that wraps
/// the underlying commit, and the <c>&lt;Commit&gt;</c> type filter
/// excludes the wrapper. The non-generic <c>Lookup(string)</c>
/// returns the wrapper itself, which <see cref="GitObject.Peel{T}"/>
/// then unwraps to the commit. Lightweight tags
/// (<c>git tag name</c> with no <c>-a</c>) point straight at the
/// commit and are caught by the fast path.
/// </para>
/// <para>
/// This helper exists so the same resolution semantics serve every
/// commit-ish input in the app — the CLI parser
/// (<see cref="ProcessCommandLineEnvironment.TryResolveCommitIsh"/>),
/// the New Diff dialog validator, and the diff-engine internals in
/// <see cref="RepositoryService"/>. The picker
/// (<see cref="LibGit2GitRefEnumerator"/>) already peels tags the
/// same way; matching that here means anything the user can pick
/// will also resolve everywhere it's later consumed.
/// </para>
/// </remarks>
internal static class CommitIshResolver
{
    /// <summary>
    /// Returns the <see cref="Commit"/> that
    /// <paramref name="commitIsh"/> names in <paramref name="repo"/>,
    /// or <c>null</c> if it doesn't resolve (empty/whitespace input,
    /// unknown ref, points at a tree/blob, etc.). Never throws.
    /// </summary>
    public static Commit? PeelToCommit(IRepository repo, string commitIsh)
    {
        if (string.IsNullOrWhiteSpace(commitIsh)) return null;
        try
        {
            // Fast path covers SHAs, branch names, revspecs
            // (HEAD~3, HEAD^), and lightweight tags.
            var direct = repo.Lookup<Commit>(commitIsh);
            if (direct is not null) return direct;

            // Annotated-tag fallback: peel the TagAnnotation wrapper
            // through to its target commit. Peel throws on
            // un-peelable objects (trees, blobs) — caught below.
            return repo.Lookup(commitIsh)?.Peel<Commit>();
        }
        catch (LibGit2SharpException)
        {
            return null;
        }
    }
}
