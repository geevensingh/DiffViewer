using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Utility;

namespace DiffViewer.ViewModels;

/// <summary>
/// One row in the left-pane file list. Wraps a <see cref="FileChange"/> and
/// exposes the rendered path under the active <see cref="FileListDisplayMode"/>
/// plus the (possibly-still-unknown) "has visible differences" flag set by
/// the eager pre-diff pass.
/// </summary>
public sealed partial class FileEntryViewModel : ObservableObject
{
    public FileChange Change { get; }
    private readonly string _repoRoot;

    /// <summary>
    /// True / false once the pre-diff pass has scored this entry; <c>null</c>
    /// while still pending. Drives the <c>(whitespace-only)</c> grey-out.
    /// </summary>
    [ObservableProperty]
    private bool? _hasVisibleDifferences;

    /// <summary>Path string under the currently-active display mode; updated by FileListViewModel.</summary>
    [ObservableProperty]
    private string _displayPath = string.Empty;

    /// <summary>
    /// Per-entry selection flag bound TwoWay to <c>TreeViewItem.IsSelected</c>
    /// in grouped-by-directory mode. <see cref="FileListViewModel"/> mirrors
    /// this with <c>SelectedEntry</c> in both directions so the TreeView's
    /// per-tree selection (read-only <c>TreeView.SelectedItem</c>) doesn't
    /// matter -- the VM owns the truth. Defaults to <c>false</c>.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// GitHub-PR-review-style "I've looked at this" flag. TwoWay-bound to
    /// the per-row CheckBox. State is owned by
    /// <see cref="FileListViewModel"/> (keyed by <see cref="FileChange.Path"/>
    /// plus a content fingerprint) so it survives a
    /// <see cref="FileListViewModel.LoadFromChanges"/> rebuild for the
    /// unchanged-content case and auto-clears when the file's content
    /// fingerprint changes between rebuilds.
    /// </summary>
    [ObservableProperty]
    private bool _isViewed;

    /// <summary>
    /// True when the row should appear in the file-list tree. Driven by the
    /// filter box plus the Hide-viewed toolbar toggle. Set by
    /// <see cref="FileListViewModel.RecomputeVisibility"/>; the XAML
    /// collapses the containing <see cref="System.Windows.Controls.TreeViewItem"/>
    /// when false. Visibility-collapse is distinct from user-driven
    /// section / directory collapse, which lives on
    /// <see cref="FileListSectionHeader.IsExpanded"/> /
    /// <see cref="DirectoryNodeViewModel.IsExpanded"/>; the two compose.
    /// </summary>
    [ObservableProperty]
    private bool _isVisible = true;

    public FileEntryViewModel(FileChange change, string repoRoot)
    {
        Change = change ?? throw new ArgumentNullException(nameof(change));
        _repoRoot = repoRoot ?? throw new ArgumentNullException(nameof(repoRoot));
        DisplayPath = RepoRelativePath;
        NormalizedPathForFilter = FileListFilter.Normalize(RepoRelativePath);
    }

    /// <summary>Repo-relative path, always with backslashes on Windows for display.</summary>
    public string RepoRelativePath => Change.Path.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>Absolute path (computed from repo root + the relative path).</summary>
    public string FullPath => Path.Combine(_repoRoot, RepoRelativePath);

    /// <summary>Final filename (basename) only.</summary>
    public string FileName => Path.GetFileName(RepoRelativePath);

    /// <summary>Repo-relative directory of the entry; empty for repo-root files.</summary>
    public string DirectoryPath => Path.GetDirectoryName(RepoRelativePath) ?? string.Empty;

    // ---- Context-menu visibility predicates ----
    // These drive MenuItem.Visibility via BoolToVisibilityConverter so the
    // View doesn't need any code-behind branching.

    /// <summary>True iff the file actually exists on disk right now.</summary>
    public bool ExistsOnDisk => File.Exists(FullPath);

    /// <summary>
    /// True for staged-section rows whose index blob differs from the
    /// working-tree file content. Per the plan, *Open in external editor*
    /// is hidden in that case (the user might think they're opening the
    /// staged version when they'd actually open the working-tree version).
    /// </summary>
    public bool ShouldHideOpenInExternalEditor =>
        Change.Layer == WorkingTreeLayer.Staged && IndexBlobDiffersFromDisk();

    private bool IndexBlobDiffersFromDisk()
    {
        if (!ExistsOnDisk) return true;
        if (Change.RightBlobSha is null) return false;
        try
        {
            // Quick file-size sanity first (cheap; right size matches index
            // blob size most of the time on legit staged-then-edited rows).
            var info = new FileInfo(FullPath);
            return Change.RightFileSizeBytes is { } indexSize && info.Length != indexSize;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True for Untracked-layer rows - <c>git add</c> applies.</summary>
    public bool IsUntracked => Change.Layer == WorkingTreeLayer.Untracked;

    // ---- Whole-file write-action eligibility (mirrors hunk-level Can*
    // predicates on DiffPaneViewModel one level up). Drives the
    // Stage/Unstage/Revert MenuItem visibility in the row's
    // ContextMenu. These are pure functions of Change.Layer, and
    // FileChange is set in the ctor and never mutated on a given
    // FileEntryViewModel instance — layer changes always come via a
    // brand-new instance during refresh — so no INotifyPropertyChanged
    // ceremony is needed. ----

    /// <summary>
    /// True iff "Stage file" should appear. Untracked and Unstaged rows
    /// both stage cleanly via <c>git add -- &lt;path&gt;</c>. Note: this
    /// is wider than the per-hunk Stage eligibility, which excludes
    /// Untracked because untracked files have no diff to show hunks
    /// for; whole-file Stage is the only UI path to add them.
    /// </summary>
    public bool CanStageWholeFile =>
        Change.Layer is WorkingTreeLayer.Untracked or WorkingTreeLayer.Unstaged;

    /// <summary>
    /// True iff "Unstage file" should appear. Only Staged rows have
    /// something to remove via <c>git restore --staged -- &lt;path&gt;</c>.
    /// <para>Known limitation: for staged renames the call only resets
    /// the new path's index entry, leaving the staged delete of the
    /// old path in place. See <see cref="DiffViewer.Services.IGitWriteService.RevertFileAsync"/>
    /// docstring for the future multi-path overload that would fix this.</para>
    /// </summary>
    public bool CanUnstageWholeFile =>
        Change.Layer == WorkingTreeLayer.Staged;

    /// <summary>
    /// True iff "Revert file…" should appear. Only Unstaged rows
    /// have working-tree edits to discard via
    /// <c>git restore -- &lt;path&gt;</c>. Destructive.
    /// </summary>
    public bool CanRevertWholeFile =>
        Change.Layer == WorkingTreeLayer.Unstaged;

    /// <summary>
    /// True iff any of the three whole-file write actions is visible.
    /// Drives the Separator preceding them in the context menu.
    /// </summary>
    public bool HasFileWriteAction =>
        CanStageWholeFile || CanUnstageWholeFile || CanRevertWholeFile;

    /// <summary>True when <em>Copy diff (unified)</em> is meaningful.</summary>
    public bool CanCopyDiffAsUnified => !Change.IsBinary && !Change.IsLfsPointer;

    /// <summary>True when *Copy blob SHA (left)* should appear (left side is a real committed blob).</summary>
    public bool HasLeftBlobSha => !string.IsNullOrEmpty(Change.LeftBlobSha);

    /// <summary>True when *Copy blob SHA (right)* should appear.</summary>
    public bool HasRightBlobSha => !string.IsNullOrEmpty(Change.RightBlobSha);

    /// <summary>Re-render <see cref="DisplayPath"/> for the supplied display mode.</summary>
    public void ApplyDisplayMode(FileListDisplayMode mode)
    {
        DisplayPath = mode switch
        {
            FileListDisplayMode.FullPath => FullPath,
            FileListDisplayMode.RepoRelative => RepoRelativePath,
            FileListDisplayMode.GroupedByDirectory => FileName,
            _ => RepoRelativePath,
        };
    }

    /// <summary>Single-character status badge: A / D / M / R / C / T / U / ?? / SM.</summary>
    public string StatusBadge => Change.Status switch
    {
        Models.FileStatus.Added => "A",
        Models.FileStatus.Deleted => "D",
        Models.FileStatus.Modified => "M",
        Models.FileStatus.Renamed => "R",
        Models.FileStatus.Copied => "C",
        Models.FileStatus.TypeChanged => "T",
        Models.FileStatus.Untracked => "??",
        Models.FileStatus.Conflicted => Change.ConflictCode ?? "UU",
        Models.FileStatus.SubmoduleMoved => "SM",
        _ => "?",
    };

    /// <summary>For renames, the rendered "old → new" suffix; otherwise empty.</summary>
    public string RenameDescriptor =>
        Change.IsRenameOrCopy && Change.OldPath is not null
            ? $" (was {Change.OldPath.Replace('/', Path.DirectorySeparatorChar)})"
            : string.Empty;

    /// <summary>True once the pre-diff pass has flagged this entry as whitespace-only.</summary>
    public bool IsWhitespaceOnly => HasVisibleDifferences == false;

    /// <summary>
    /// Single signal driving the row's dim chrome. Composes the two
    /// independent dim triggers — pre-diff-pass-says-whitespace-only and
    /// user-marked-viewed — so the XAML only needs one
    /// <c>DataTrigger</c> on this property rather than chained / nested
    /// triggers across both inputs.
    /// </summary>
    public bool IsDimmed => IsWhitespaceOnly || IsViewed;

    /// <summary>
    /// <see cref="RepoRelativePath"/> with backslashes replaced by forward
    /// slashes, computed once in the constructor so per-keystroke filter
    /// recompute is allocation-free per row. See
    /// <see cref="FileListFilter"/> for why we normalise.
    /// </summary>
    public string NormalizedPathForFilter { get; }

    /// <summary>
    /// Content identity used by <see cref="FileListViewModel"/> to decide
    /// whether a stored "viewed" flag should carry over across a
    /// <see cref="FileListViewModel.LoadFromChanges"/> rebuild. Fields are
    /// pulled straight off the underlying <see cref="FileChange"/>: blob
    /// SHAs cover both committed sides, file sizes catch working-tree
    /// edits (modulo size-preserving content changes — a known v1
    /// limitation), and <see cref="FileChange.Status"/> catches
    /// modified↔renamed↔deleted transitions where the SHAs alone might
    /// match coincidentally.
    /// </summary>
    public ContentFingerprint Fingerprint => new(
        Change.LeftBlobSha,
        Change.RightBlobSha,
        Change.LeftFileSizeBytes,
        Change.RightFileSizeBytes,
        Change.Status);

    partial void OnHasVisibleDifferencesChanged(bool? value)
    {
        OnPropertyChanged(nameof(IsWhitespaceOnly));
        OnPropertyChanged(nameof(IsDimmed));
    }

    partial void OnIsViewedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDimmed));
    }
}

/// <summary>
/// Content-identity tuple used by <see cref="FileListViewModel"/>'s viewed
/// state machine: when the user marks a file viewed we snapshot this
/// fingerprint, and on the next <see cref="FileListViewModel.LoadFromChanges"/>
/// rebuild the viewed flag is re-applied only if the new entry's
/// fingerprint matches the snapshot. Mismatch ⇒ the content has moved,
/// so the prior "I reviewed this" assertion is stale and we drop it
/// (GitHub-PR-review behaviour).
/// </summary>
public readonly record struct ContentFingerprint(
    string? LeftBlobSha,
    string? RightBlobSha,
    long? LeftFileSizeBytes,
    long? RightFileSizeBytes,
    FileStatus Status);
