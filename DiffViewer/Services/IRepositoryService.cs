using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Read-only access to a git repository: enumerate the change list between
/// two <see cref="DiffSide"/>s, fetch blob bytes through the clean/smudge
/// filter chain, watch for repo loss / index changes. Write operations
/// live in <see cref="IGitWriteService"/>.
/// </summary>
public interface IRepositoryService : IDisposable
{
    /// <summary>Static facts about the repo captured at open time.</summary>
    RepositoryShape Shape { get; }

    /// <summary>The most recently enumerated change list. Empty until <see cref="EnumerateChanges"/> succeeds.</summary>
    IReadOnlyList<FileChange> CurrentChanges { get; }

    /// <summary>Raised whenever the change list is recomputed.</summary>
    event EventHandler<ChangeListUpdatedEventArgs>? ChangeListUpdated;

    /// <summary>Raised when the repo on disk becomes unreadable or vanishes.</summary>
    event EventHandler<RepositoryLostEventArgs>? RepositoryLost;

    /// <summary>Resolve a commit-ish reference; returns <c>null</c> if it doesn't resolve.</summary>
    string? ResolveCommitIsh(string reference);

    /// <summary>
    /// Resolve a commit-ish reference to its full <see cref="CommitMetadata"/>
    /// (sha, short sha, author, date, subject, body). Returns <c>null</c> if
    /// the reference doesn't resolve or doesn't point at a commit object.
    /// </summary>
    CommitMetadata? GetCommitMetadata(string commitIsh);

    /// <summary>Both refs resolve to commits and are reachable.</summary>
    bool ValidateRevisions(string leftRef, string rightRef);

    /// <summary>
    /// Enumerate the full change list between <paramref name="left"/> and
    /// <paramref name="right"/>. Updates <see cref="CurrentChanges"/> and
    /// raises <see cref="ChangeListUpdated"/> on success.
    /// </summary>
    IReadOnlyList<FileChange> EnumerateChanges(DiffSide left, DiffSide right);

    /// <summary>
    /// Read the contents of one side of a single file change. Applies the
    /// clean/smudge filter chain and detects encoding / binary / LFS-pointer
    /// state.
    /// </summary>
    BlobContent ReadSide(FileChange change, ChangeSide side);

    /// <summary>
    /// Compute a cheap identity tag for one side of a change without
    /// reading the blob bytes. Used by the diff pane to detect "this
    /// side's content is identical to what we last loaded" so a refresh
    /// that produced a new <see cref="FileChange"/> instance pointing at
    /// the same bytes can skip the reload entirely (no IsLoading overlay
    /// flash, no diff recompute, no highlight-map refire).
    ///
    /// <para>For blob-backed sides this is the blob SHA. For working-tree
    /// sides this is the file's (mtime, size) at probe time -- the same
    /// pair git itself caches in the index for fast change detection.
    /// Returns <c>null</c> when no deterministic identity is available;
    /// callers must treat <c>null</c> as "always changed" and not skip.</para>
    /// </summary>
    BlobIdentity? ProbeSideIdentity(FileChange change, ChangeSide side);

    /// <summary>
    /// Drop LibGit2Sharp's in-memory index cache and re-read <c>.git\index</c>
    /// from disk. Required after every external <c>git.exe</c> mutation.
    /// </summary>
    void RefreshIndex();

    /// <summary>
    /// Re-resolve the current state of <paramref name="path"/> as a
    /// <see cref="FileChange"/> in the supplied layer. Returns <c>null</c>
    /// if the path no longer differs in that layer (used by write-op
    /// preflight to close the menu-open ⟶ click race).
    /// </summary>
    FileChange? TryResolveCurrent(string path, WorkingTreeLayer layer);

    /// <summary>Reopen the repo after <see cref="RepositoryLost"/>; returns true on success.</summary>
    bool TryReopen();

    /// <summary>Atomic snapshot of the current change list under the same lock that wires up the subscription.</summary>
    (IReadOnlyList<FileChange> Snapshot, IDisposable Subscription) SnapshotAndSubscribe(
        EventHandler<ChangeListUpdatedEventArgs> handler);

    /// <summary>
    /// True if the supplied repo-relative forward-slash path is ignored
    /// per the repo's <c>.gitignore</c> rules (including
    /// <c>core.excludesFile</c> and any nested <c>.gitignore</c> files).
    /// Used by the watcher to drop noise from <c>bin\</c>, <c>obj\</c>,
    /// <c>node_modules\</c>, etc. Returns <c>false</c> if the repo is
    /// inaccessible.
    /// </summary>
    bool IsPathIgnored(string repoRelativeForwardSlashPath);
}

/// <summary>Which side of a <see cref="FileChange"/> to read.</summary>
public enum ChangeSide
{
    Left,
    Right,
}

/// <summary>
/// Cheap identity tag for one side of a <see cref="FileChange"/>. Compared
/// by value: two equal <see cref="BlobIdentity"/>s mean the underlying
/// bytes are (almost certainly) unchanged between two probes, so the
/// caller can skip a redundant <see cref="IRepositoryService.ReadSide"/>
/// + diff recompute.
///
/// <para>Construct via the static factory members rather than the
/// primary constructor so the field roles stay clear:
/// <see cref="FromBlob"/> for SHA-addressed content,
/// <see cref="FromWorkingTree"/> for a file on disk (mtime + size, the
/// same identity proxy git uses in the index), <see cref="Empty"/> for
/// an empty side (e.g. <c>Untracked</c> left, deleted-on-this-side),
/// and <see cref="MissingWorkingTreeFile"/> for a workdir path that
/// doesn't exist. The <see cref="Empty"/> and
/// <see cref="MissingWorkingTreeFile"/> singletons compare equal to
/// themselves, which is intentional: "still empty" and "still missing"
/// are stable identities worth skipping.</para>
/// </summary>
public readonly record struct BlobIdentity(
    string? BlobSha,
    DateTime WorkingTreeMtimeUtc,
    long WorkingTreeSizeBytes)
{
    /// <summary>SHA-addressed blob (any commit-side or staged-blob side).</summary>
    public static BlobIdentity FromBlob(string sha) => new(sha, default, 0);

    /// <summary>Genuinely empty side (e.g. <c>Untracked</c> left, file deleted on this side).</summary>
    public static BlobIdentity Empty { get; } = new(null, default, 0);

    /// <summary>Working-tree file that exists on disk; identity is (mtime, size).</summary>
    public static BlobIdentity FromWorkingTree(DateTime mtimeUtc, long sizeBytes)
        => new(null, mtimeUtc, sizeBytes);

    /// <summary>Working-tree side whose file does not exist on disk.</summary>
    public static BlobIdentity MissingWorkingTreeFile { get; } = new(null, default, -1);
}
