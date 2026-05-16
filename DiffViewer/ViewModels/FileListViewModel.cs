using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;

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
    /// Also serves as the group key for <see cref="FlatGroupedView"/>: the
    /// same header instance is returned for every entry in a given layer,
    /// which is what causes them to land in the same grouped ListBox group.
    /// </summary>
    private readonly Dictionary<WorkingTreeLayer, FileListSectionHeader> _sectionHeaders = new();

    private readonly LayerToHeaderConverter _layerConverter;
    private ListCollectionView? _flatGroupedView;

    public FileListViewModel(Services.ISettingsService? settingsService = null)
    {
        _settingsService = settingsService;
        _layerConverter = new LayerToHeaderConverter(this);
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

    /// <summary>
    /// Grouped view over <see cref="FlatEntries"/> used by the flat-mode
    /// single ListBox. Grouping by <see cref="FileListSectionHeader"/>
    /// keyed off <see cref="FileChange.Layer"/> means selection is owned
    /// by exactly one Selector — preventing the multi-ListBox stale-state
    /// bug where re-clicking a previously-selected file in a different
    /// section would silently no-op.
    /// </summary>
    public ICollectionView FlatGroupedView
    {
        get
        {
            if (_flatGroupedView is null)
            {
                _flatGroupedView = new ListCollectionView(FlatEntries)
                {
                    CustomSort = SectionAndPathComparer.Instance,
                };
                _flatGroupedView.GroupDescriptions.Add(
                    new PropertyGroupDescription("Change.Layer", _layerConverter));
            }
            return _flatGroupedView;
        }
    }

    [ObservableProperty]
    private FileListDisplayMode _displayMode = FileListDisplayMode.RepoRelative;

    [ObservableProperty]
    private FileEntryViewModel? _selectedEntry;

    [ObservableProperty]
    private bool _isFlatLayout;

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
    /// </summary>
    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressIsSelectedSync) return;
        if (e.PropertyName != nameof(FileEntryViewModel.IsSelected)) return;
        if (sender is not FileEntryViewModel entry) return;
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
            foreach (var root in section.RootDirectories)
            {
                if (ExpandIfContains(root, entry))
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
    /// </summary>
    public void LoadFromChanges(IReadOnlyList<FileChange> changes, string repoRoot, bool isCommitVsCommit)
    {
        ArgumentNullException.ThrowIfNull(changes);

        // Snapshot the prior selection's identity BEFORE we clear the
        // collections. Clearing FlatEntries can cascade to the bound
        // grouped ListBox; capturing path/layer up front lets us re-resolve
        // to the matching new entry once the rebuild completes.
        string? priorPath = SelectedEntry?.Change.Path;
        WorkingTreeLayer? priorLayer = SelectedEntry?.Change.Layer;

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

        if (isCommitVsCommit)
        {
            // No section grouping for commit-vs-commit - flat list under one synthetic section.
            IsFlatLayout = true;
            Sections.Add(new FileListSectionViewModel(
                WorkingTreeLayer.None, "Changes", entries,
                _expansionStore, GetSectionHeader(WorkingTreeLayer.None)));
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
    }

    private void AddIfNonEmpty(WorkingTreeLayer layer, string header, IEnumerable<FileEntryViewModel> all)
    {
        var subset = all.Where(e => e.Change.Layer == layer)
                        .OrderBy(e => e.RepoRelativePath, StringComparer.OrdinalIgnoreCase)
                        .ToList();
        if (subset.Count == 0) return;
        Sections.Add(new FileListSectionViewModel(
            layer, header, subset, _expansionStore, GetSectionHeader(layer)));
    }

    /// <summary>
    /// Return the cached section header for <paramref name="layer"/>,
    /// creating it on first use. Both the grouped flat-mode ListBox and
    /// the per-section <see cref="FileListSectionViewModel"/> share the
    /// same instance so collapsing in one presentation is visible in the
    /// other, and so collapse state survives rebuilds.
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

    private static int LayerSortOrder(WorkingTreeLayer layer) => layer switch
    {
        WorkingTreeLayer.Conflicted => 0,
        WorkingTreeLayer.CommittedSinceCommit => 1,
        WorkingTreeLayer.Staged => 2,
        WorkingTreeLayer.Unstaged => 3,
        WorkingTreeLayer.Untracked => 4,
        WorkingTreeLayer.None => 5,
        _ => 99,
    };

    /// <summary>
    /// Maps the <see cref="FileChange.Layer"/> property of each entry to
    /// the cached <see cref="FileListSectionHeader"/> used as the group
    /// key. Same-layer entries get the same instance, which is how
    /// <see cref="ICollectionView"/> grouping coalesces them.
    /// </summary>
    private sealed class LayerToHeaderConverter : IValueConverter
    {
        private readonly FileListViewModel _owner;

        public LayerToHeaderConverter(FileListViewModel owner) => _owner = owner;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            _owner.GetSectionHeader((WorkingTreeLayer)value);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>
    /// Orders entries by canonical layer order first, then by repo-relative
    /// path within a layer. Matches the per-section ordering produced by
    /// <see cref="AddIfNonEmpty"/> so the grouped flat-mode ListBox shows
    /// rows in the same order as the per-section tree-mode presentation.
    /// </summary>
    private sealed class SectionAndPathComparer : IComparer
    {
        public static readonly SectionAndPathComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            if (x is not FileEntryViewModel a || y is not FileEntryViewModel b) return 0;
            int la = LayerSortOrder(a.Change.Layer);
            int lb = LayerSortOrder(b.Change.Layer);
            if (la != lb) return la - lb;
            return StringComparer.OrdinalIgnoreCase.Compare(a.RepoRelativePath, b.RepoRelativePath);
        }
    }
}
