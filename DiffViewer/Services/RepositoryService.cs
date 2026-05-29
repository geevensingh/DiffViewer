using System.IO;
using DiffViewer.Models;
using DiffViewer.Utility;
using LibGit2Sharp;
using LibGit2SharpFileStatus = LibGit2Sharp.FileStatus;
using DiffViewerFileStatus = DiffViewer.Models.FileStatus;

namespace DiffViewer.Services;

/// <summary>
/// LibGit2Sharp-backed <see cref="IRepositoryService"/>. Owns one
/// <see cref="Repository"/> instance for the lifetime of the window.
/// </summary>
public sealed class RepositoryService : IRepositoryService
{
    private readonly object _lock = new();
    private readonly string _repoPath;

    private Repository _repo;
    private RepositoryShape _shape;
    private IReadOnlyList<FileChange> _currentChanges = Array.Empty<FileChange>();

    public event EventHandler<ChangeListUpdatedEventArgs>? ChangeListUpdated;
    public event EventHandler<RepositoryLostEventArgs>? RepositoryLost;

    public RepositoryService(string repoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);

        if (!Repository.IsValid(repoPath))
        {
            throw new ArgumentException($"Not a git repository: {repoPath}", nameof(repoPath));
        }

        _repoPath = repoPath;
        _repo = new Repository(repoPath);
        _shape = BuildShape(_repo);
    }

    public RepositoryShape Shape
    {
        get { lock (_lock) { return _shape; } }
    }

    public IReadOnlyList<FileChange> CurrentChanges
    {
        get { lock (_lock) { return _currentChanges; } }
    }

    public string? ResolveCommitIsh(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        lock (_lock)
        {
            try
            {
                return CommitIshResolver.PeelToCommit(_repo, reference)?.Sha;
            }
            catch (LibGit2SharpException)
            {
                return null;
            }
        }
    }

    public CommitMetadata? GetCommitMetadata(string commitIsh)
    {
        if (string.IsNullOrWhiteSpace(commitIsh)) return null;
        lock (_lock)
        {
            try
            {
                var commit = CommitIshResolver.PeelToCommit(_repo, commitIsh);
                if (commit is null) return null;

                var author = commit.Author;
                return new CommitMetadata(
                    Sha: commit.Sha,
                    ShortSha: ShortenSha(commit.Sha),
                    AuthorName: author?.Name ?? string.Empty,
                    AuthorEmail: author?.Email ?? string.Empty,
                    AuthorDate: author?.When ?? default,
                    MessageSubject: commit.MessageShort ?? string.Empty,
                    MessageBody: ExtractMessageBody(commit.Message ?? string.Empty),
                    FriendlyName: ResolveFriendlyName(commit));
            }
            catch (LibGit2SharpException)
            {
                return null;
            }
        }
    }

    public bool ValidateRevisions(string leftRef, string rightRef) =>
        ResolveCommitIsh(leftRef) is not null && ResolveCommitIsh(rightRef) is not null;

    private static string ShortenSha(string sha) =>
        sha.Length >= 7 ? sha[..7] : sha;

    /// <summary>
    /// Git convention: subject paragraph, blank line, body. Returns everything
    /// after the first <c>\n\n</c>; empty string for subject-only messages.
    /// LibGit2Sharp normalises CRLF to LF on read, so a single <c>\n\n</c>
    /// probe covers both Windows-authored and Unix-authored commits.
    /// </summary>
    private static string ExtractMessageBody(string fullMessage)
    {
        if (string.IsNullOrEmpty(fullMessage)) return string.Empty;
        var idx = fullMessage.IndexOf("\n\n", StringComparison.Ordinal);
        if (idx < 0) return string.Empty;
        return fullMessage[(idx + 2)..].TrimEnd('\n');
    }

    /// <summary>
    /// Find a <c>git log --decorate</c>-style friendly label for the commit
    /// by walking the repo's refs in priority order:
    /// <list type="number">
    ///   <item>HEAD's branch — if HEAD is on a branch (not detached) and its
    ///   tip equals this commit, that branch wins. This is what makes
    ///   <c>HEAD</c> render as <c>master</c> in the common case.</item>
    ///   <item>Tags — annotated or lightweight — pointing at this commit.
    ///   Tags are stable user-facing labels; once a release is tagged, the
    ///   tag is more informative than a coincidentally-positioned branch.</item>
    ///   <item>Other local branches whose tip equals this commit.</item>
    ///   <item>Remote-tracking branches (e.g. <c>origin/master</c>) whose tip
    ///   equals this commit. Lowest priority because they're a mirror of
    ///   what was last fetched, not an authoritative local ref.</item>
    /// </list>
    /// Returns <see langword="null"/> when nothing decorates the commit, so
    /// the View falls back to the short SHA alone. Deliberately does not
    /// consider the user's literal CLI input string: a user who typed
    /// <c>feature/foo</c> shouldn't see that label if the branch has since
    /// moved off the commit.
    /// <para>Within a single tier, ties are broken alphabetically for
    /// determinism — needed because LibGit2Sharp's enumeration order isn't
    /// contractually stable.</para>
    /// </summary>
    private string? ResolveFriendlyName(Commit commit)
    {
        try
        {
            // 1. HEAD's branch.
            var head = _repo.Head;
            if (head is not null
                && !head.IsRemote
                && head.FriendlyName != "(no branch)"
                && head.Tip?.Sha == commit.Sha)
            {
                return head.FriendlyName;
            }

            // 2. Tags. Use Peel<Commit> so annotated and lightweight tags
            //    are handled uniformly; annotated tags' Target is the
            //    annotation object, not the commit.
            var tagHit = _repo.Tags
                .Where(t => t.Target is not null
                            && t.Target.Peel<Commit>()?.Sha == commit.Sha)
                .Select(t => t.FriendlyName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (tagHit is not null) return tagHit;

            // 3. Other local branches.
            var localBranchHit = _repo.Branches
                .Where(b => !b.IsRemote && b.Tip?.Sha == commit.Sha)
                .Select(b => b.FriendlyName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (localBranchHit is not null) return localBranchHit;

            // 4. Remote-tracking branches.
            var remoteBranchHit = _repo.Branches
                .Where(b => b.IsRemote && b.Tip?.Sha == commit.Sha)
                .Select(b => b.FriendlyName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .FirstOrDefault();
            return remoteBranchHit;
        }
        catch (LibGit2SharpException)
        {
            // Defensive: ref enumeration shouldn't throw, but a corrupt
            // ref store shouldn't take down the whole header row either.
            return null;
        }
    }

    public void RefreshIndex()
    {
        lock (_lock)
        {
            // LibGit2Sharp 0.31 doesn't expose an explicit Index.Read; the cheapest
            // way to drop the in-memory index cache and re-read .git\index from disk
            // is to recreate the Repository handle (the .git dir open is microseconds).
            // Required after every external git.exe mutation.
            var fresh = new Repository(_repoPath);
            _repo.Dispose();
            _repo = fresh;
            _shape = BuildShape(_repo);
        }
    }

    public bool TryReopen()
    {
        lock (_lock)
        {
            if (!Repository.IsValid(_repoPath))
            {
                return false;
            }

            try
            {
                var fresh = new Repository(_repoPath);
                _repo.Dispose();
                _repo = fresh;
                _shape = BuildShape(_repo);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public (IReadOnlyList<FileChange> Snapshot, IDisposable Subscription) SnapshotAndSubscribe(
        EventHandler<ChangeListUpdatedEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock)
        {
            var snapshot = _currentChanges;
            ChangeListUpdated += handler;
            return (snapshot, new Subscription(() => ChangeListUpdated -= handler));
        }
    }

    public IReadOnlyList<FileChange> EnumerateChanges(DiffSide left, DiffSide right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        IReadOnlyList<FileChange> changes;

        try
        {
            lock (_lock)
            {
                changes = (left, right) switch
                {
                    (DiffSide.CommitIsh l, DiffSide.CommitIsh r) =>
                        EnumerateCommitVsCommit(l.Reference, r.Reference),

                    (DiffSide.CommitIsh l, DiffSide.WorkingTree) =>
                        EnumerateCommitVsWorkingTree(l.Reference),

                    (DiffSide.WorkingTree, DiffSide.CommitIsh r) =>
                        EnumerateCommitVsWorkingTree(r.Reference),

                    _ => throw new InvalidOperationException("Working-tree vs working-tree is not a meaningful comparison."),
                };
            }
        }
        catch (Exception ex) when (IsRepoLossException(ex))
        {
            RaiseRepositoryLost(ex);
            return Array.Empty<FileChange>();
        }

        lock (_lock)
        {
            _currentChanges = changes;
        }

        ChangeListUpdated?.Invoke(this, new ChangeListUpdatedEventArgs { Changes = changes });
        return changes;
    }

    public BlobContent ReadSide(FileChange change, ChangeSide side)
    {
        ArgumentNullException.ThrowIfNull(change);

        try
        {
            lock (_lock)
            {
                return ReadSideLocked(change, side);
            }
        }
        catch (Exception ex) when (IsRepoLossException(ex))
        {
            RaiseRepositoryLost(ex);
            return BlobContent.Empty;
        }
    }

    public BlobIdentity? ProbeSideIdentity(FileChange change, ChangeSide side)
    {
        ArgumentNullException.ThrowIfNull(change);

        bool readLeft = side == ChangeSide.Left;

        // Mirror ReadSideLocked's "right side of Unstaged/Untracked reads
        // from the working tree" special case. The workdir path resolves
        // under _lock (it depends on _shape which can mutate during
        // TryReopen), but the stat call itself is plain System.IO and
        // doesn't need to hold the libgit2 lock.
        if (!readLeft && (change.Layer == WorkingTreeLayer.Unstaged
                       || change.Layer == WorkingTreeLayer.Untracked))
        {
            string workPath;
            lock (_lock) { workPath = WorkdirPath(change.Path); }
            try
            {
                var info = new FileInfo(workPath);
                if (!info.Exists) return BlobIdentity.MissingWorkingTreeFile;
                return BlobIdentity.FromWorkingTree(info.LastWriteTimeUtc, info.Length);
            }
            catch
            {
                // I/O error or permission issue - force a reload rather
                // than silently treating "can't stat" as "still the same".
                return null;
            }
        }

        // Blob-backed sides. A null SHA here is either Untracked-left
        // (which ReadSideLocked treats as empty) or "file added/deleted
        // on this side"; both observably produce empty content, so they
        // share the same identity.
        string? sha = readLeft ? change.LeftBlobSha : change.RightBlobSha;
        if (sha is null) return BlobIdentity.Empty;
        return BlobIdentity.FromBlob(sha);
    }

    public FileChange? TryResolveCurrent(string path, WorkingTreeLayer layer)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            lock (_lock)
            {
                return layer switch
                {
                    WorkingTreeLayer.Staged => ResolveStagedSingle(path),
                    WorkingTreeLayer.Unstaged => ResolveUnstagedSingle(path),
                    WorkingTreeLayer.Untracked => ResolveUntrackedSingle(path),
                    _ => null,
                };
            }
        }
        catch (Exception ex) when (IsRepoLossException(ex))
        {
            RaiseRepositoryLost(ex);
            return null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _repo.Dispose();
        }
    }

    public bool IsPathIgnored(string repoRelativeForwardSlashPath)
    {
        if (string.IsNullOrEmpty(repoRelativeForwardSlashPath)) return false;
        try
        {
            lock (_lock)
            {
                // LibGit2Sharp.Ignore.IsPathIgnored handles core.excludesFile,
                // .git/info/exclude, AND every nested .gitignore with the
                // same precedence as `git check-ignore`. Hand-rolling this
                // would miss the global file at minimum.
                return _repo.Ignore.IsPathIgnored(repoRelativeForwardSlashPath);
            }
        }
        catch
        {
            // Repo lost while resolving - safest to treat as not-ignored
            // so the watcher still fires; the next refresh will surface
            // the loss.
            return false;
        }
    }

    // --------- enumeration internals ---------

    private List<FileChange> EnumerateCommitVsCommit(string leftRef, string rightRef)
    {
        var leftCommit = CommitIshResolver.PeelToCommit(_repo, leftRef)
            ?? throw new InvalidOperationException($"Cannot resolve `{leftRef}` in repo {_repoPath}");
        var rightCommit = CommitIshResolver.PeelToCommit(_repo, rightRef)
            ?? throw new InvalidOperationException($"Cannot resolve `{rightRef}` in repo {_repoPath}");

        var compareOptions = BuildCompareOptions();
        var changes = new List<FileChange>();

        using (var treeChanges = _repo.Diff.Compare<TreeChanges>(leftCommit.Tree, rightCommit.Tree, compareOptions))
        {
            foreach (var entry in treeChanges)
            {
                changes.Add(BuildFileChange(entry, WorkingTreeLayer.None));
            }
        }

        return changes;
    }

    private List<FileChange> EnumerateCommitVsWorkingTree(string leftRef)
    {
        // Special-case: unborn HEAD has no commits, so a "HEAD" left side resolves to the
        // empty tree. Other refs against an unborn HEAD will return null below and throw.
        bool unbornHead = _repo.Info.IsHeadUnborn;
        bool leftIsUnbornHead = unbornHead && IsHeadAlias(leftRef);

        Commit? leftCommit = leftIsUnbornHead ? null : CommitIshResolver.PeelToCommit(_repo, leftRef);
        if (!leftIsUnbornHead && leftCommit is null)
        {
            throw new InvalidOperationException($"Cannot resolve `{leftRef}` in repo {_repoPath}");
        }

        var headCommit = unbornHead ? null : _repo.Head.Tip;
        Tree? leftTree = leftCommit?.Tree;
        Tree? headTree = headCommit?.Tree;

        var compareOptions = BuildCompareOptions();
        var changes = new List<FileChange>();

        // Conflicted entries always come first.
        if (!_repo.Index.IsFullyMerged)
        {
            foreach (var conflict in _repo.Index.Conflicts)
            {
                changes.Add(BuildConflictedChange(conflict));
            }
        }

        // Committed-since layer: only if the user supplied a non-HEAD ref
        // AND HEAD is born AND the left ref differs from HEAD.
        if (leftCommit is not null && headCommit is not null && !ShaEquals(leftCommit, headCommit))
        {
            using var committedSince = _repo.Diff.Compare<TreeChanges>(leftTree, headTree, compareOptions);
            foreach (var entry in committedSince)
            {
                changes.Add(BuildFileChange(entry, WorkingTreeLayer.CommittedSinceCommit));
            }
        }

        // Staged layer: HEAD tree -> index. Tree=null means "compare against empty tree"
        // for unborn HEAD; LibGit2Sharp accepts this and reports every staged path as added.
        var stagedDiff = _repo.Diff.Compare<TreeChanges>(headTree, DiffTargets.Index, paths: null, explicitPathsOptions: null, compareOptions: compareOptions);

        using (stagedDiff)
        {
            foreach (var entry in stagedDiff)
            {
                changes.Add(BuildFileChange(entry, WorkingTreeLayer.Staged));
            }
        }

        // Unstaged layer: index -> working dir.
        using (var unstagedDiff = _repo.Diff.Compare<TreeChanges>(paths: null, includeUntracked: false, explicitPathsOptions: null, compareOptions: compareOptions))
        {
            foreach (var entry in unstagedDiff)
            {
                changes.Add(BuildFileChange(entry, WorkingTreeLayer.Unstaged));
            }
        }

        // Untracked layer.
        var statusOptions = new StatusOptions
        {
            Show = StatusShowOption.WorkDirOnly,
            IncludeUntracked = true,
            IncludeIgnored = false,
            DetectRenamesInIndex = false,
            DetectRenamesInWorkDir = false,
        };

        foreach (var entry in _repo.RetrieveStatus(statusOptions))
        {
            if ((entry.State & LibGit2SharpFileStatus.NewInWorkdir) != 0)
            {
                changes.Add(BuildUntrackedChange(entry.FilePath));
            }
        }

        return changes;
    }

    private static bool IsHeadAlias(string reference) =>
        string.Equals(reference, "HEAD", StringComparison.Ordinal) ||
        string.Equals(reference, "head", StringComparison.Ordinal);

    private FileChange BuildFileChange(TreeEntryChanges entry, WorkingTreeLayer layer)
    {
        var status = MapStatus(entry.Status);
        bool isRename = status == DiffViewerFileStatus.Renamed || status == DiffViewerFileStatus.Copied;
        string? oldPath = isRename ? entry.OldPath : null;

        // For working-tree (Unstaged) reads, the right side is on disk - blob SHA may be ObjectId.Zero.
        string? leftSha = entry.OldOid != ObjectId.Zero ? entry.OldOid.Sha : null;
        string? rightSha = entry.Oid != ObjectId.Zero ? entry.Oid.Sha : null;

        // Probe for binary / LFS / size on the right side first; fall back to left.
        var (isBinary, isLfsPointer, leftSize, rightSize) = ProbeMetadata(entry, layer);

        bool isSparseNotCheckedOut = layer == WorkingTreeLayer.Unstaged
            && _shape.IsSparseCheckout
            && rightSha is null
            && !File.Exists(WorkdirPath(entry.Path));

        return new FileChange(
            Path: entry.Path,
            OldPath: oldPath,
            Status: status,
            ConflictCode: null,
            Layer: layer,
            LeftBlobSha: leftSha,
            RightBlobSha: rightSha,
            IsBinary: isBinary,
            LeftFileSizeBytes: leftSize,
            RightFileSizeBytes: rightSize,
            IsLfsPointer: isLfsPointer,
            IsSparseNotCheckedOut: isSparseNotCheckedOut,
            OldMode: (int)entry.OldMode,
            NewMode: (int)entry.Mode);
    }

    private FileChange BuildConflictedChange(Conflict conflict)
    {
        // Conflict entries don't have a single "Path" property; use the index entry's path
        // (Ours/Theirs/Ancestor — whichever exists first).
        string path = conflict.Ours?.Path
            ?? conflict.Theirs?.Path
            ?? conflict.Ancestor?.Path
            ?? "<unknown>";

        string code = ClassifyConflict(conflict);

        return new FileChange(
            Path: path,
            OldPath: null,
            Status: DiffViewerFileStatus.Conflicted,
            ConflictCode: code,
            Layer: WorkingTreeLayer.Conflicted,
            LeftBlobSha: conflict.Ancestor?.Id?.Sha,
            RightBlobSha: null,
            IsBinary: false,
            LeftFileSizeBytes: null,
            RightFileSizeBytes: null,
            IsLfsPointer: false,
            IsSparseNotCheckedOut: false,
            OldMode: 0,
            NewMode: 0);
    }

    private FileChange BuildUntrackedChange(string path)
    {
        long? size = null;
        bool isBinary = false;
        bool isLfsPointer = false;

        var workPath = WorkdirPath(path);
        if (File.Exists(workPath))
        {
            var info = new FileInfo(workPath);
            size = info.Length;

            try
            {
                using var fs = File.OpenRead(workPath);
                int probeLen = (int)Math.Min(8 * 1024, fs.Length);
                var buf = new byte[probeLen];
                int read = fs.Read(buf, 0, probeLen);
                isBinary = BinaryDetector.IsBinary(buf.AsSpan(0, read));
                isLfsPointer = LfsPointerDetector.IsLfsPointer(buf.AsSpan(0, read));
            }
            catch (IOException)
            {
                // Best-effort - leave the metadata blank.
            }
        }

        return new FileChange(
            Path: path,
            OldPath: null,
            Status: DiffViewerFileStatus.Untracked,
            ConflictCode: null,
            Layer: WorkingTreeLayer.Untracked,
            LeftBlobSha: null,
            RightBlobSha: null,
            IsBinary: isBinary,
            LeftFileSizeBytes: null,
            RightFileSizeBytes: size,
            IsLfsPointer: isLfsPointer,
            IsSparseNotCheckedOut: false,
            OldMode: 0,
            NewMode: 0);
    }

    private (bool IsBinary, bool IsLfsPointer, long? LeftSize, long? RightSize) ProbeMetadata(
        TreeEntryChanges entry, WorkingTreeLayer layer)
    {
        long? leftSize = null;
        long? rightSize = null;
        bool isBinary = false;
        bool isLfsPointer = false;

        // Right side first - the most "live" version of the file.
        if (entry.Oid != ObjectId.Zero && _repo.Lookup<Blob>(entry.Oid) is Blob rightBlob)
        {
            rightSize = rightBlob.Size;
            (isBinary, isLfsPointer) = ProbeBlob(rightBlob, entry.Path);
        }
        else if (layer == WorkingTreeLayer.Unstaged)
        {
            var workPath = WorkdirPath(entry.Path);
            if (File.Exists(workPath))
            {
                rightSize = new FileInfo(workPath).Length;
                try
                {
                    using var fs = File.OpenRead(workPath);
                    int probeLen = (int)Math.Min(8 * 1024, fs.Length);
                    var buf = new byte[probeLen];
                    int read = fs.Read(buf, 0, probeLen);
                    isBinary |= BinaryDetector.IsBinary(buf.AsSpan(0, read));
                    isLfsPointer |= LfsPointerDetector.IsLfsPointer(buf.AsSpan(0, read));
                }
                catch (IOException) { }
            }
        }

        if (entry.OldOid != ObjectId.Zero && _repo.Lookup<Blob>(entry.OldOid) is Blob leftBlob)
        {
            leftSize = leftBlob.Size;
            if (!isBinary || !isLfsPointer)
            {
                var (lb, ll) = ProbeBlob(leftBlob, entry.OldPath ?? entry.Path);
                isBinary |= lb;
                isLfsPointer |= ll;
            }
        }

        return (isBinary, isLfsPointer, leftSize, rightSize);
    }

    private static (bool IsBinary, bool IsLfsPointer) ProbeBlob(Blob blob, string repoRelativePath)
    {
        // Read the raw ODB stream first to detect LFS pointers (which are tiny text files
        // even when the underlying object would be a multi-GB binary).
        try
        {
            using var raw = blob.GetContentStream();
            int probeLen = (int)Math.Min(8 * 1024, blob.Size);
            var buf = new byte[probeLen];
            int read = ReadFully(raw, buf);
            var span = buf.AsSpan(0, read);

            bool isLfs = LfsPointerDetector.IsLfsPointer(span);
            // A recognised text BOM (UTF-8 / UTF-16 / UTF-32) is a strong
            // text signal — strong enough to override LibGit2Sharp's own
            // NUL-byte heuristic, which otherwise mis-flags UTF-16 source
            // files as binary. EncodingDetector handles all five BOM
            // forms downstream. Honour blob.IsBinary (which also reads
            // .gitattributes 'binary') only when no BOM is present.
            bool isBinary = !BinaryDetector.HasTextBom(span)
                && (blob.IsBinary || BinaryDetector.IsBinary(span));
            return (isBinary, isLfs);
        }
        catch (Exception) when (!IsCriticalException())
        {
            return (false, false);
        }

        static bool IsCriticalException() => false;
    }

    private BlobContent ReadSideLocked(FileChange change, ChangeSide side)
    {
        // Determine the source: blob (committed) or working tree.
        // Layer-aware:
        //   - None (commit-vs-commit): both sides are blobs.
        //   - Staged: left = HEAD blob, right = index blob.
        //   - Unstaged: left = index blob, right = working tree file.
        //   - Untracked: left = empty, right = working tree file.
        //   - CommittedSinceCommit: left = leftCommit.Tree blob, right = HEAD blob.

        bool readLeft = side == ChangeSide.Left;

        // For the right side of Unstaged/Untracked, the working tree IS the
        // source of truth — read directly from disk regardless of
        // RightBlobSha. LibGit2Sharp's Compare<TreeChanges> against
        // DiffTargets.WorkingDirectory sometimes pre-computes the
        // clean-filter hash of the workdir file and stamps it on
        // TreeEntryChanges.Oid, but the blob it would refer to was never
        // persisted to the object database. Honouring that SHA here would
        // route us into Lookup<Blob>, get back null, and return Empty —
        // showing the file as entirely deleted in the diff pane even
        // though it exists on disk. ProbeMetadata already has this
        // workdir fallback (see lines around `else if (layer ==
        // WorkingTreeLayer.Unstaged)`); reading the workdir here keeps
        // the read path consistent with its documented contract above.
        if (!readLeft && (change.Layer == WorkingTreeLayer.Unstaged || change.Layer == WorkingTreeLayer.Untracked))
        {
            var workPath = WorkdirPath(change.Path);
            if (!File.Exists(workPath)) return BlobContent.Empty;

            var bytes = File.ReadAllBytes(workPath);
            return BuildBlobContent(bytes);
        }

        string? sha = readLeft ? change.LeftBlobSha : change.RightBlobSha;
        if (sha is null) return BlobContent.Empty;

        var blob = _repo.Lookup<Blob>(sha);
        if (blob is null) return BlobContent.Empty;

        // Read post-filter bytes (clean/smudge applied) so encoding detection
        // and diff happen on the "view" of the file the user expects.
        var pathForFilters = readLeft && change.OldPath is not null ? change.OldPath : change.Path;
        byte[] filteredBytes;
        try
        {
            using var stream = blob.GetContentStream(new FilteringOptions(pathForFilters));
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            filteredBytes = ms.ToArray();
        }
        catch (LibGit2SharpException)
        {
            // Filter chain failed (e.g. partial-clone object missing) - return empty.
            return BlobContent.Empty;
        }

        return BuildBlobContent(filteredBytes);
    }

    private static BlobContent BuildBlobContent(byte[] bytes)
    {
        bool isLfs = LfsPointerDetector.IsLfsPointer(bytes);
        bool isBinary = BinaryDetector.IsBinary(bytes);
        if (isBinary || isLfs)
        {
            // Don't try to decode binary or pointer text - the caller switches to a placeholder pane.
            return new BlobContent(bytes, System.Text.Encoding.UTF8, string.Empty, IsBinary: isBinary, IsLfsPointer: isLfs);
        }

        var text = EncodingDetector.Decode(bytes, out var enc);
        return new BlobContent(bytes, enc, text, IsBinary: false, IsLfsPointer: false);
    }

    private FileChange? ResolveStagedSingle(string path)
    {
        var head = _repo.Info.IsHeadUnborn ? null : _repo.Head.Tip;
        var compareOptions = BuildCompareOptions();
        using var diff = _repo.Diff.Compare<TreeChanges>(head?.Tree, DiffTargets.Index, paths: new[] { path }, explicitPathsOptions: null, compareOptions: compareOptions);

        var entry = diff.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.Ordinal));
        return entry is null ? null : BuildFileChange(entry, WorkingTreeLayer.Staged);
    }

    private FileChange? ResolveUnstagedSingle(string path)
    {
        var compareOptions = BuildCompareOptions();
        using var diff = _repo.Diff.Compare<TreeChanges>(paths: new[] { path }, includeUntracked: false, explicitPathsOptions: null, compareOptions: compareOptions);
        var entry = diff.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.Ordinal));
        return entry is null ? null : BuildFileChange(entry, WorkingTreeLayer.Unstaged);
    }

    private FileChange? ResolveUntrackedSingle(string path)
    {
        var workPath = WorkdirPath(path);
        if (!File.Exists(workPath)) return null;

        var status = _repo.RetrieveStatus(path);
        if ((status & LibGit2SharpFileStatus.NewInWorkdir) == 0) return null;

        return BuildUntrackedChange(path);
    }

    // --------- helpers ---------

    private static CompareOptions BuildCompareOptions() => new()
    {
        // Honour the repo's diff.renames / diff.renameLimit config (Resolved Decision #1).
        Similarity = SimilarityOptions.Default,
        IncludeUnmodified = false,
        ContextLines = 0, // we recompute context ourselves in DiffService
    };

    private string WorkdirPath(string repoRelative)
    {
        if (_shape.WorkingDirectory is null)
        {
            return repoRelative;
        }

        return Path.Combine(_shape.WorkingDirectory, repoRelative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool ShaEquals(Commit a, Commit b) =>
        string.Equals(a.Sha, b.Sha, StringComparison.OrdinalIgnoreCase);

    private static DiffViewerFileStatus MapStatus(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => DiffViewerFileStatus.Added,
        ChangeKind.Deleted => DiffViewerFileStatus.Deleted,
        ChangeKind.Modified => DiffViewerFileStatus.Modified,
        ChangeKind.Renamed => DiffViewerFileStatus.Renamed,
        ChangeKind.Copied => DiffViewerFileStatus.Copied,
        ChangeKind.TypeChanged => DiffViewerFileStatus.TypeChanged,
        ChangeKind.Ignored => DiffViewerFileStatus.Untracked,
        ChangeKind.Untracked => DiffViewerFileStatus.Untracked,
        ChangeKind.Conflicted => DiffViewerFileStatus.Conflicted,
        _ => DiffViewerFileStatus.Modified,
    };

    private static string ClassifyConflict(Conflict c)
    {
        bool ancestor = c.Ancestor is not null;
        bool ours = c.Ours is not null;
        bool theirs = c.Theirs is not null;

        // The standard porcelain conflict letters: U = unmerged.
        return (ancestor, ours, theirs) switch
        {
            (true, true, true) => "UU",     // both modified
            (false, true, true) => "AA",    // both added
            (true, true, false) => "UD",    // modified-vs-deleted (theirs deleted)
            (true, false, true) => "DU",    // deleted-vs-modified (ours deleted)
            (true, false, false) => "DD",   // both deleted
            (false, true, false) => "AU",   // added by us, unmerged
            (false, false, true) => "UA",   // added by them, unmerged
            _ => "??",
        };
    }

    private static int ReadFully(Stream s, byte[] buf)
    {
        int total = 0;
        while (total < buf.Length)
        {
            int read = s.Read(buf, total, buf.Length - total);
            if (read <= 0) break;
            total += read;
        }
        return total;
    }

    private static RepositoryShape BuildShape(Repository repo)
    {
        bool sparse = false;
        try
        {
            sparse = repo.Config.Get<bool>("core.sparseCheckout")?.Value ?? false;
        }
        catch (LibGit2SharpException) { }

        bool partialClone = false;
        try
        {
            foreach (var remote in repo.Network.Remotes)
            {
                var promisor = repo.Config.Get<bool>($"remote.{remote.Name}.promisor");
                if (promisor?.Value == true)
                {
                    partialClone = true;
                    break;
                }
            }
        }
        catch (LibGit2SharpException) { }

        return new RepositoryShape(
            RepoRoot: repo.Info.WorkingDirectory ?? repo.Info.Path,
            WorkingDirectory: repo.Info.WorkingDirectory,
            GitDir: repo.Info.Path,
            IsBare: repo.Info.IsBare,
            IsHeadUnborn: repo.Info.IsHeadUnborn,
            IsSparseCheckout: sparse,
            IsPartialClone: partialClone,
            HasInProgressOperation: repo.Info.CurrentOperation != CurrentOperation.None);
    }

    private static bool IsRepoLossException(Exception ex) => ex is
        DirectoryNotFoundException or
        FileNotFoundException or
        UnauthorizedAccessException or
        RepositoryNotFoundException;

    private void RaiseRepositoryLost(Exception ex)
    {
        var reason = ex switch
        {
            DirectoryNotFoundException => RepositoryLossReason.DotGitMissing,
            FileNotFoundException => RepositoryLossReason.DotGitMissing,
            UnauthorizedAccessException => RepositoryLossReason.AccessDenied,
            RepositoryNotFoundException => RepositoryLossReason.RepoRootMissing,
            _ => RepositoryLossReason.Other,
        };

        RepositoryLost?.Invoke(this, new RepositoryLostEventArgs
        {
            RepoRoot = _repoPath,
            Reason = reason,
            ExceptionMessage = reason == RepositoryLossReason.Other ? ex.Message : null,
        });
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _unsubscribe;
        public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;
        public void Dispose() { _unsubscribe?.Invoke(); _unsubscribe = null; }
    }
}
