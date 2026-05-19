using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Utility;

namespace DiffViewer.ViewModels;

/// <summary>
/// Backs <c>FileListView</c>. Holds the section structure, the active
/// display mode, and the currently-selected row. Population is push-based:
/// callers invoke <see cref="LoadFromChanges"/> whenever the underlying
/// <see cref="IRepositoryService"/> emits a change-list update.
/// </summary>
public sealed partial class FileListViewModel : ObservableObject
{
    private readonly Services.ISettingsService? _settingsService;
    private bool _suppressSettingsWrite;

    /// <summary>
    /// Guards against feedback loops in the
    /// <see cref="SelectedEntry"/>↔<see cref="FileEntryViewModel.IsSelected"/>
    /// bidirectional sync. Set while one side is mutating the other so the
    /// resulting PropertyChanged event doesn't bounce back into a setter.
    /// </summary>
    private bool _suppressIsSelectedSync;

    /// <summary>
    /// True while <see cref="LoadFromChanges"/> is rebuilding the list.
    /// Downstream consumers of <see cref="SelectedEntry"/> PropertyChanged
    /// (currently <c>MainViewModel.OnFileListPropertyChanged</c>) gate on
    /// this so they ignore transient intermediates -- the binding-driven
    /// null writeback when <see cref="FlatEntries"/> is cleared, and the
    /// explicit re-assignment that restores the selection at the end. One
    /// consolidated PropertyChanged is fired after the rebuild completes
    /// so the consumer observes the final (post-reload) state exactly
    /// once. Skipping the intermediates is what keeps
    /// <c>DiffPaneViewModel.CurrentHunkIndex</c> from being reset on a
    /// same-file refresh -- otherwise the transient null-load would empty
    /// <c>_currentHunks</c> and defeat the same-shape preserve logic in
    /// <c>DiffPaneViewModel.ApplyResult</c>.
    /// </summary>
    public bool IsReloading { get; private set; }

    /// <summary>
    /// Per-process directory expansion memory. Keeps track of which
    /// directory nodes the user has explicitly collapsed so that adding
    /// or removing a file (which triggers a full <see cref="LoadFromChanges"/>)
    /// doesn't reset the tree to all-expanded.
    /// </summary>
    private readonly DirectoryExpansionStore _expansionStore = new();

    /// <summary>
    /// Per-layer section header cache. Headers outlive any single
    /// <see cref="FileListSectionViewModel"/> instance so the user's
    /// collapse state survives <see cref="LoadFromChanges"/> rebuilds.
    /// </summary>
    private readonly Dictionary<WorkingTreeLayer, FileListSectionHeader> _sectionHeaders = new();

    /// <summary>
    /// Per-launch-context "viewed" memory. Survives
    /// <see cref="LoadFromChanges"/> rebuilds — same pattern as
    /// <see cref="_sectionHeaders"/> and <see cref="_expansionStore"/> —
    /// keyed by <see cref="FileChange.Path"/> (case-insensitive on
    /// Windows). Stored value is the entry's
    /// <see cref="FileEntryViewModel.Fingerprint"/> at the moment the
    /// flag was set; on the next rebuild we only re-apply the flag when
    /// the new entry's fingerprint matches. Mismatch ⇒ content has moved
    /// since the user marked it viewed, so the prior assertion is stale
    /// and the dictionary entry is dropped (GitHub-PR-review behaviour).
    ///
    /// <para>Entries for files that fall out of the list are intentionally
    /// kept: a file may temporarily disappear (revert, branch switch,
    /// staging) and come back with the same content, in which case the
    /// prior viewed flag should re-apply. Dictionary footprint is bounded
    /// by the user's per-context interaction count.</para>
    /// </summary>
    private readonly Dictionary<string, ViewedRecord> _viewedByPath =
        new(StringComparer.OrdinalIgnoreCase);

    public FileListViewModel(Services.ISettingsService? settingsService = null)
    {
        _settingsService = settingsService;
        if (_settingsService is not null)
        {
            _suppressSettingsWrite = true;
            try { DisplayMode = _settingsService.Current.DisplayMode; }
            finally { _suppressSettingsWrite = false; }
        }
    }

    public ObservableCollection<FileListSectionViewModel> Sections { get; } = new();

    /// <summary>
    /// Flat list shortcut used by the file-stepping keyboard shortcuts
    /// (Shift+F7/F8) so navigation works regardless of display mode.
    /// </summary>
    public ObservableCollection<FileEntryViewModel> FlatEntries { get; } = new();

    [ObservableProperty]
    private FileListDisplayMode _displayMode = FileListDisplayMode.RepoRelative;

    [ObservableProperty]
    private FileEntryViewModel? _selectedEntry;

    [ObservableProperty]
    private bool _isFlatLayout;

    /// <summary>
    /// Substring filter applied to every row's repo-relative path.
    /// Case-insensitive and slash-insensitive (see
    /// <see cref="FileListFilter"/>). Empty string disables the filter.
    /// </summary>
    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>
    /// Toolbar toggle that suppresses rows the user has marked viewed.
    /// Composes with <see cref="FilterText"/> (AND, not OR): a row is
    /// visible only when it both matches the filter and is either
    /// un-viewed or Hide-viewed is off.
    /// </summary>
    [ObservableProperty]
    private bool _hideViewed;

    /// <summary>
    /// True when at least one of <see cref="FilterText"/> /
    /// <see cref="HideViewed"/> is currently restricting the visible set.
    /// Used by the section header chip template to decide between the
    /// plain <c>(N)</c> and the <c>(visible / total)</c> rendering.
    /// </summary>
    public bool IsFilterOrHideActive => HideViewed || !string.IsNullOrEmpty(FilterText);

    /// <summary>
    /// True when at least one file in the current context is marked
    /// viewed. The toolbar's <em>Hide viewed</em> toggle binds its
    /// visibility to this so the toggle vanishes when it would be a
    /// no-op (nothing to hide). Computed from <see cref="_viewedByPath"/>
    /// rather than scanning <see cref="FlatEntries"/> because the
    /// dictionary outlives any single rebuild — a file may currently
    /// be filtered out of the entry list while its viewed flag still
    /// counts toward "have we marked anything?".
    /// </summary>
    public bool HasAnyViewed
    {
        get
        {
            foreach (var r in _viewedByPath.Values)
            {
                if (r.IsViewed) return true;
            }
            return false;
        }
    }

    /// <summary>True when <see cref="DisplayMode"/> is the grouped tree view.</summary>
    public bool IsGroupedMode => DisplayMode == FileListDisplayMode.GroupedByDirectory;

    /// <summary>True when <see cref="DisplayMode"/> is one of the flat list modes.</summary>
    public bool IsFlatMode => !IsGroupedMode;

    public bool IsFullPathMode
    {
        get => DisplayMode == FileListDisplayMode.FullPath;
        set { if (value) DisplayMode = FileListDisplayMode.FullPath; }
    }

    public bool IsRepoRelativeMode
    {
        get => DisplayMode == FileListDisplayMode.RepoRelative;
        set { if (value) DisplayMode = FileListDisplayMode.RepoRelative; }
    }

    public bool IsGroupedByDirectoryMode
    {
        get => DisplayMode == FileListDisplayMode.GroupedByDirectory;
        set { if (value) DisplayMode = FileListDisplayMode.GroupedByDirectory; }
    }

    partial void OnDisplayModeChanged(FileListDisplayMode value)
    {
        foreach (var entry in FlatEntries) entry.ApplyDisplayMode(value);
        foreach (var section in Sections) section.ApplyDisplayMode(value);
        OnPropertyChanged(nameof(IsGroupedMode));
        OnPropertyChanged(nameof(IsFlatMode));
        OnPropertyChanged(nameof(IsFullPathMode));
        OnPropertyChanged(nameof(IsRepoRelativeMode));
        OnPropertyChanged(nameof(IsGroupedByDirectoryMode));
        if (_settingsService is not null && !_suppressSettingsWrite)
        {
            _settingsService.Update(s => s with { DisplayMode = value });
        }
    }

    partial void OnFilterTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsFilterOrHideActive));
        RecomputeVisibility();
    }

    partial void OnHideViewedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFilterOrHideActive));
        RecomputeVisibility();
    }

    /// <summary>
    /// Mirror <see cref="SelectedEntry"/> into the per-entry
    /// <see cref="FileEntryViewModel.IsSelected"/> flag so the grouped-mode
    /// <c>TreeView</c> visually highlights and scrolls to the new selection
    /// (TreeView reacts to its containers' <c>IsSelected</c> turning true).
    /// Also expands the ancestor section and directories so the row is
    /// actually visible — otherwise auto-scroll would land inside a
    /// collapsed branch and the user wouldn't see the highlight.
    ///
    /// <para>Always runs. The suppression guard is set <em>here</em> to
    /// keep our own <c>IsSelected</c> writes from bouncing back through
    /// <see cref="OnEntryPropertyChanged"/> and re-entering this setter,
    /// but the work itself (clearing the prior IsSelected, setting the new
    /// one, expanding ancestors) must happen whether the change came from
    /// a TreeView click or from a direct <c>SelectedEntry</c> assignment.</para>
    /// </summary>
    partial void OnSelectedEntryChanged(FileEntryViewModel? oldValue, FileEntryViewModel? newValue)
    {
        _suppressIsSelectedSync = true;
        try
        {
            if (oldValue is not null) oldValue.IsSelected = false;
            if (newValue is not null)
            {
                ExpandAncestorsForSelection(newValue);
                newValue.IsSelected = true;
            }
        }
        finally { _suppressIsSelectedSync = false; }
    }

    /// <summary>
    /// Listens for <see cref="FileEntryViewModel.IsSelected"/> flipping to
    /// <c>true</c> (which happens when the user clicks a file in the grouped
    /// TreeView and the TwoWay binding pushes the flag back into the VM).
    /// Promotes that entry to <see cref="SelectedEntry"/>; the
    /// <c>OnSelectedEntryChanged</c> hook then clears any stale
    /// <c>IsSelected</c> on the prior entry, keeping the per-VM state and
    /// the per-tree TreeViewItem state synchronized.
    ///
    /// <para>Bails out when <see cref="_suppressIsSelectedSync"/> is set,
    /// which is the signal that <see cref="OnSelectedEntryChanged"/> is
    /// already running and the IsSelected change is its own bookkeeping
    /// (not a fresh user click).</para>
    ///
    /// <para>Also listens for <see cref="FileEntryViewModel.IsViewed"/>
    /// changes so the per-entry checkbox writes back into
    /// <see cref="_viewedByPath"/>, with the entry's current fingerprint,
    /// so the flag can be re-applied across rebuilds when the content
    /// hasn't changed. Skipped during <see cref="IsReloading"/> because
    /// the reload path itself sets <c>IsViewed</c> from the dictionary
    /// (writing back during reload would be a redundant write of the
    /// same value).</para>
    /// </summary>
    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not FileEntryViewModel entry) return;

        if (e.PropertyName == nameof(FileEntryViewModel.IsViewed))
        {
            if (IsReloading) return;
            _viewedByPath[entry.Change.Path] = new ViewedRecord(entry.Fingerprint, entry.IsViewed);
            // Only the Hide-viewed compose path cares about a viewed
            // flip — the filter is independent of viewed state. Skipping
            // the recompute when HideViewed is off is a meaningful win
            // for large lists, since toggling viewed is a per-row action
            // that doesn't otherwise touch the tree structure.
            if (HideViewed) RecomputeVisibility();
            // Notify HasAnyViewed so the toolbar's Hide-viewed button
            // appears/disappears as needed. If un-marking just cleared
            // the last viewed flag, also auto-reset HideViewed — otherwise
            // the toggle would be stuck "on" with no UI to flip it off
            // (and the next file the user marks viewed would vanish
            // immediately, which is the wrong default).
            OnPropertyChanged(nameof(HasAnyViewed));
            if (HideViewed && !HasAnyViewed) HideViewed = false;
            return;
        }

        if (_suppressIsSelectedSync) return;
        if (e.PropertyName != nameof(FileEntryViewModel.IsSelected)) return;
        if (!entry.IsSelected) return;

        SelectedEntry = entry;
    }

    /// <summary>
    /// Walk the section &amp; directory tree to expand every ancestor of
    /// <paramref name="entry"/> so a programmatic
    /// <see cref="SelectedEntry"/> change (F7/F8 navigation, refresh-time
    /// restoration, etc.) results in a visible, scrolled-into-view row.
    /// No-op if the entry isn't actually present in <see cref="Sections"/>
    /// (defensive guard against stale references during rebuilds).
    /// </summary>
    private void ExpandAncestorsForSelection(FileEntryViewModel entry)
    {
        foreach (var section in Sections)
        {
            if (section.Layer != entry.Change.Layer) continue;
            foreach (var item in section.RootItems)
            {
                // Root files surface as bare FileEntryViewModel siblings
                // of the root directories, so a ref-equal hit here means
                // the entry lives at the repo root and only the section
                // header needs to be expanded.
                if (item is FileEntryViewModel file && ReferenceEquals(file, entry))
                {
                    section.SharedHeader.IsExpanded = true;
                    return;
                }
                if (item is DirectoryNodeViewModel root && ExpandIfContains(root, entry))
                {
                    section.SharedHeader.IsExpanded = true;
                    return;
                }
            }
        }
    }

    private static bool ExpandIfContains(DirectoryNodeViewModel node, FileEntryViewModel target)
    {
        foreach (var f in node.Files)
        {
            if (ReferenceEquals(f, target))
            {
                node.IsExpanded = true;
                return true;
            }
        }
        foreach (var child in node.Children)
        {
            if (ExpandIfContains(child, target))
            {
                node.IsExpanded = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Replace all sections / entries with the supplied change list. Called
    /// from the UI thread.
    ///
    /// <para>Preserves <see cref="SelectedEntry"/> across the rebuild when
    /// the previously-selected file is still present in the new list at
    /// the same <see cref="WorkingTreeLayer"/>. If the file fell out of
    /// the list entirely (deleted, staged elsewhere, branch switched,
    /// etc.), the selection is cleared -- the diff pane will swap to its
    /// placeholder via the normal <c>SelectedEntry</c>-change pipeline.</para>
    ///
    /// <para>When <paramref name="preferredInitialPath"/> is supplied AND
    /// there is no prior selection (cold launch), the matching entry is
    /// selected. Used by the CLI <c>--file</c> flag (issue #5). Matching is
    /// case-insensitive against <see cref="FileEntryViewModel.RepoRelativePath"/>;
    /// the caller is responsible for normalizing separators to
    /// <see cref="System.IO.Path.DirectorySeparatorChar"/>. An unmatched
    /// path is a silent no-op.</para>
    /// </summary>
    public void LoadFromChanges(
        IReadOnlyList<FileChange> changes,
        string repoRoot,
        bool isCommitVsCommit,
        string? preferredInitialPath = null)
    {
        ArgumentNullException.ThrowIfNull(changes);

        // Snapshot the prior selection's identity BEFORE we clear the
        // collections. Clearing FlatEntries can cascade to the bound
        // grouped ListBox; capturing path/layer up front lets us re-resolve
        // to the matching new entry once the rebuild completes.
        var priorSelectedEntry = SelectedEntry;
        string? priorPath = priorSelectedEntry?.Change.Path;
        WorkingTreeLayer? priorLayer = priorSelectedEntry?.Change.Layer;

        // Gate consumers of SelectedEntry PropertyChanged so they don't
        // react to intermediate states. Two writes happen during the
        // rebuild: (1) the WPF ListBox writes null back to SelectedEntry
        // when FlatEntries.Clear() empties its ItemsSource (TwoWay binding
        // can't keep a SelectedItem that's no longer in the items list);
        // (2) we explicitly re-assign SelectedEntry to the restored match
        // below. Without this gate, (1) would null out DiffPane.CurrentEntry
        // and clear _currentHunks, causing the (2) restore to be treated
        // as a fresh-file load and resetting CurrentHunkIndex.
        IsReloading = true;
        try
        {
            // Detach the per-entry IsSelected listener BEFORE the entries go
            // away, otherwise we leak a handler per refresh.
            foreach (var e in FlatEntries) e.PropertyChanged -= OnEntryPropertyChanged;

            Sections.Clear();
            FlatEntries.Clear();

            var entries = changes.Select(c =>
            {
                var e = new FileEntryViewModel(c, repoRoot);
                e.ApplyDisplayMode(DisplayMode);
                e.PropertyChanged += OnEntryPropertyChanged;
                return e;
            }).ToList();
            foreach (var e in entries) FlatEntries.Add(e);

            // Re-apply viewed flags from prior rebuilds. Only entries
            // whose content fingerprint still matches the moment-of-mark
            // get their flag back; on mismatch the dictionary entry is
            // dropped so the user isn't lulled by a stale "viewed" badge
            // on changed content. Files that simply fell out of the list
            // are left in the dictionary so the flag can re-apply if they
            // come back (revert / branch switch) with matching content.
            foreach (var e in entries)
            {
                if (!_viewedByPath.TryGetValue(e.Change.Path, out var record)) continue;
                if (record.Fingerprint.Equals(e.Fingerprint))
                {
                    e.IsViewed = record.IsViewed;
                }
                else
                {
                    _viewedByPath.Remove(e.Change.Path);
                }
            }

            // Dictionary may have been pruned (fingerprint mismatches) or
            // had no viewed entries to reapply at all; either way the
            // toolbar binding needs to reflect the post-reload truth.
            OnPropertyChanged(nameof(HasAnyViewed));
            if (HideViewed && !HasAnyViewed) HideViewed = false;

            if (isCommitVsCommit)
            {
                // No section grouping for commit-vs-commit - flat list under one synthetic section.
                IsFlatLayout = true;
                var section = new FileListSectionViewModel(
                    WorkingTreeLayer.None, "Changes", entries,
                    _expansionStore, GetSectionHeader(WorkingTreeLayer.None));
                section.ApplyDisplayMode(DisplayMode);
                Sections.Add(section);
            }
            else
            {
                IsFlatLayout = false;

                // Order: Conflicted, CommittedSinceCommit, Staged, Unstaged, Untracked.
                AddIfNonEmpty(WorkingTreeLayer.Conflicted, "Conflicted", entries);
                AddIfNonEmpty(WorkingTreeLayer.CommittedSinceCommit, "Committed since baseline", entries);
                AddIfNonEmpty(WorkingTreeLayer.Staged, "Staged", entries);
                AddIfNonEmpty(WorkingTreeLayer.Unstaged, "Unstaged", entries);
                AddIfNonEmpty(WorkingTreeLayer.Untracked, "Untracked", entries);
            }

            // Restore selection (or explicitly clear it if the prior file
            // fell out of the list -- otherwise the diff pane stays stale
            // showing a file that no longer appears anywhere on the left).
            // On a cold launch there is no prior selection; in that case,
            // if the caller supplied a preferredInitialPath (--file), select
            // the matching entry. Unmatched paths fall through silently.
            if (priorPath is not null)
            {
                FileEntryViewModel? match = null;
                foreach (var e in FlatEntries)
                {
                    if (string.Equals(e.Change.Path, priorPath, StringComparison.OrdinalIgnoreCase)
                        && e.Change.Layer == priorLayer)
                    {
                        match = e;
                        break;
                    }
                }
                SelectedEntry = match;
            }
            else if (!string.IsNullOrEmpty(preferredInitialPath))
            {
                foreach (var e in FlatEntries)
                {
                    if (string.Equals(e.RepoRelativePath, preferredInitialPath, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedEntry = e;
                        break;
                    }
                }
            }

            // Initial visibility pass — seeds each section's
            // VisibleEntryCount and applies any FilterText / HideViewed
            // values that were set before the rebuild.
            RecomputeVisibility();
        }
        finally
        {
            IsReloading = false;
        }

        // Fire one consolidated PropertyChanged so the gated consumer
        // (MainViewModel.OnFileListPropertyChanged) observes the final
        // post-reload selection. The reference comparison handles the
        // common no-op case where there was no selection before and none
        // after, avoiding a pointless placeholder reload. When the
        // refresh produced a new VM instance for the same path, the
        // consumer treats it as a same-file refresh because
        // DiffPane.CurrentEntry still points at the prior instance
        // (which we never disturbed during the rebuild), preserving
        // CurrentHunkIndex via HunksHaveSameShape in ApplyResult.
        if (!ReferenceEquals(priorSelectedEntry, SelectedEntry))
        {
            OnPropertyChanged(nameof(SelectedEntry));
        }
    }

    private void AddIfNonEmpty(WorkingTreeLayer layer, string header, IEnumerable<FileEntryViewModel> all)
    {
        var subset = all.Where(e => e.Change.Layer == layer)
                        .OrderBy(e => e.RepoRelativePath, StringComparer.OrdinalIgnoreCase)
                        .ToList();
        if (subset.Count == 0) return;
        var section = new FileListSectionViewModel(
            layer, header, subset, _expansionStore, GetSectionHeader(layer));
        section.ApplyDisplayMode(DisplayMode);
        Sections.Add(section);
    }

    /// <summary>
    /// Return the cached section header for <paramref name="layer"/>,
    /// creating it on first use. The cache outlives section VMs so the
    /// user's collapse state survives <see cref="LoadFromChanges"/>
    /// rebuilds — the new section instance gets handed the same
    /// <see cref="FileListSectionHeader"/> via its constructor.
    /// </summary>
    internal FileListSectionHeader GetSectionHeader(WorkingTreeLayer layer)
    {
        if (!_sectionHeaders.TryGetValue(layer, out var header))
        {
            header = new FileListSectionHeader(layer, HeaderLabelFor(layer));
            _sectionHeaders[layer] = header;
        }
        return header;
    }

    private static string HeaderLabelFor(WorkingTreeLayer layer) => layer switch
    {
        WorkingTreeLayer.Conflicted => "Conflicted",
        WorkingTreeLayer.CommittedSinceCommit => "Committed since baseline",
        WorkingTreeLayer.Staged => "Staged",
        WorkingTreeLayer.Unstaged => "Unstaged",
        WorkingTreeLayer.Untracked => "Untracked",
        WorkingTreeLayer.None => "Changes",
        _ => layer.ToString(),
    };

    /// <summary>
    /// Recompute the <c>IsVisible</c> / <c>VisibleEntryCount</c> flags on
    /// every entry, section, and directory node in the tree so the bound
    /// triggers in the view collapse hidden rows. Called whenever
    /// <see cref="FilterText"/> or <see cref="HideViewed"/> changes,
    /// after <see cref="LoadFromChanges"/> rebuilds the list, and when an
    /// individual entry's <see cref="FileEntryViewModel.IsViewed"/>
    /// flips while <see cref="HideViewed"/> is on.
    ///
    /// <para>Each <see cref="FileEntryViewModel"/> instance is shared
    /// between <see cref="FlatEntries"/>, the owning section's
    /// <c>Entries</c>, and the directory tree's <c>Files</c>, so
    /// updating <c>IsVisible</c> once via the section loop is enough.
    /// Section and directory visibility derive from descendant visibility
    /// (any visible descendant ⇒ the container itself is visible).</para>
    /// </summary>
    internal void RecomputeVisibility()
    {
        string? normalizedQuery = string.IsNullOrEmpty(FilterText)
            ? null
            : FileListFilter.Normalize(FilterText);

        foreach (var section in Sections)
        {
            int visibleCount = 0;
            foreach (var entry in section.Entries)
            {
                bool matchesFilter = normalizedQuery is null
                    || FileListFilter.MatchesNormalized(entry.NormalizedPathForFilter, normalizedQuery);
                bool passesHide = !HideViewed || !entry.IsViewed;
                bool visible = matchesFilter && passesHide;
                entry.IsVisible = visible;
                if (visible) visibleCount++;
            }
            section.VisibleEntryCount = visibleCount;
            // A section with no visible entries collapses itself so the
            // header chrome doesn't dangle. The display-mode bar and the
            // header collapse-arrow remain user-visible because they're
            // not inside the section's TreeViewItem.
            section.IsVisible = visibleCount > 0;

            // Cascade through the directory tree for grouped-by-directory
            // mode. The flat / repo-relative modes don't surface
            // DirectoryNodeViewModels in the bound RootItems collection,
            // so the inner walk is a no-op there.
            foreach (var item in section.RootItems)
            {
                if (item is DirectoryNodeViewModel dir) RecomputeDirVisibility(dir);
            }
        }
    }

    /// <summary>
    /// Recursive helper for <see cref="RecomputeVisibility"/>. A directory
    /// is visible when any of its descendant files (transitively, through
    /// nested directories) is visible.
    /// </summary>
    private static bool RecomputeDirVisibility(DirectoryNodeViewModel dir)
    {
        bool anyVisible = false;
        foreach (var child in dir.Children)
        {
            if (RecomputeDirVisibility(child)) anyVisible = true;
        }
        foreach (var file in dir.Files)
        {
            if (file.IsVisible) anyVisible = true;
        }
        dir.IsVisible = anyVisible;
        return anyVisible;
    }
}

/// <summary>
/// Per-path snapshot of a viewed flag along with the content fingerprint
/// at the moment it was set. Stored in
/// <see cref="FileListViewModel._viewedByPath"/>; on the next rebuild we
/// only re-apply the flag if the new entry's fingerprint still matches —
/// otherwise the marking is stale (content moved on us) and is dropped.
/// </summary>
internal readonly record struct ViewedRecord(ContentFingerprint Fingerprint, bool IsViewed);
