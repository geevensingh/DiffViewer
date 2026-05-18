using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;

namespace DiffViewer.ViewModels;

/// <summary>
/// One section of the left-pane file list: Conflicted, CommittedSinceCommit,
/// Staged, Unstaged, or Untracked. Sections are absent for commit-vs-commit
/// comparisons (the list is flat).
/// </summary>
public sealed partial class FileListSectionViewModel : ObservableObject
{
    public WorkingTreeLayer Layer { get; }
    public string Header { get; }
    public ObservableCollection<FileEntryViewModel> Entries { get; }

    /// <summary>
    /// Top-level items for grouped-by-directory mode: a heterogeneous
    /// sequence of <see cref="FileEntryViewModel"/> (files at the repo
    /// root, listed first) and <see cref="DirectoryNodeViewModel"/> (root
    /// directories, sorted by label). Root files sit at the section's
    /// top level rather than under a synthetic empty-labelled directory
    /// node, which would otherwise render as an empty header row.
    /// </summary>
    public ObservableCollection<object> RootItems { get; }

    /// <summary>
    /// First-level children for the unified TreeView, projected from
    /// <see cref="Entries"/> in the flat display modes and from
    /// <see cref="RootItems"/> in grouped-by-directory mode. Mutated
    /// in place by <see cref="ApplyDisplayMode"/> so the bound
    /// <c>HierarchicalDataTemplate.ItemsSource</c> receives incremental
    /// CollectionChanged notifications rather than a full PropertyChanged
    /// re-bind.
    ///
    /// <para>Typed as <see cref="object"/> because the source collections
    /// hold different element types (<see cref="FileEntryViewModel"/> vs
    /// <see cref="DirectoryNodeViewModel"/>); WPF dispatches each item to
    /// its matching <c>DataTemplate</c> by runtime type, the same way the
    /// grouped-view templates already do for
    /// <see cref="DirectoryNodeViewModel.ChildrenAndFiles"/>.</para>
    /// </summary>
    public ObservableCollection<object> Children { get; } = new();

    /// <summary>
    /// Per-layer expand state shared with the flat-mode grouped ListBox so
    /// collapsing a section in one presentation is reflected in the other.
    /// Owned by <see cref="FileListViewModel"/>; section VMs are recreated
    /// on every refresh but the header instance is cached on the VM so the
    /// user's collapse state survives.
    /// </summary>
    public FileListSectionHeader SharedHeader { get; }

    /// <summary>
    /// True when the section row should appear in the file-list tree. Set
    /// by <see cref="FileListViewModel.RecomputeVisibility"/> when the
    /// section has at least one visible descendant entry. Composes with
    /// — but is independent of — the user-driven
    /// <see cref="FileListSectionHeader.IsExpanded"/> collapse flag.
    /// </summary>
    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>
    /// Number of entries with <see cref="FileEntryViewModel.IsVisible"/>
    /// true. Drives the section header chip's "visible / total" suffix
    /// when a filter or Hide-viewed toggle is active; falls back to a
    /// plain count otherwise.
    /// </summary>
    [ObservableProperty]
    private int _visibleEntryCount;

    /// <summary>
    /// Pre-formatted suffix text for the section header chip. The view
    /// renders this verbatim so we don't need a multi-binding converter:
    /// <c>"N"</c> when the visible and total counts match (no filter /
    /// hide narrowing the list), <c>"V / T"</c> otherwise. Updated by
    /// the <see cref="OnVisibleEntryCountChanged"/> partial; the
    /// <see cref="Entries"/> count is fixed for the section's lifetime
    /// so we don't need to listen for changes there.
    /// </summary>
    public string CountChipText =>
        VisibleEntryCount == Entries.Count
            ? Entries.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)
            : string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                "{0} / {1}", VisibleEntryCount, Entries.Count);

    partial void OnVisibleEntryCountChanged(int value) =>
        OnPropertyChanged(nameof(CountChipText));

    public FileListSectionViewModel(WorkingTreeLayer layer, string header, IEnumerable<FileEntryViewModel> entries)
        : this(layer, header, entries, store: null, sharedHeader: null) { }

    public FileListSectionViewModel(
        WorkingTreeLayer layer, string header,
        IEnumerable<FileEntryViewModel> entries,
        DirectoryExpansionStore? store)
        : this(layer, header, entries, store, sharedHeader: null) { }

    public FileListSectionViewModel(
        WorkingTreeLayer layer, string header,
        IEnumerable<FileEntryViewModel> entries,
        DirectoryExpansionStore? store,
        FileListSectionHeader? sharedHeader)
    {
        Layer = layer;
        Header = header;
        SharedHeader = sharedHeader ?? new FileListSectionHeader(layer, header);
        Entries = new ObservableCollection<FileEntryViewModel>(entries);
        // Default to all-visible so the section header chip renders as
        // the plain total count before RecomputeVisibility runs (the
        // first reload calls it at the end of LoadFromChanges, but unit
        // tests that build sections directly via this ctor skip that
        // call). Once a filter / Hide-viewed flips, RecomputeVisibility
        // overwrites the count to reflect the visible subset.
        _visibleEntryCount = Entries.Count;
        RootItems = new ObservableCollection<object>(
            DirectoryNodeViewModel.Build(Entries, sectionKey: layer.ToString(), store: store));

        // Default to the directory projection so a section constructed
        // without an explicit mode call (older test paths via the
        // two-/three-arg constructors) still has a sensible Children
        // collection. FileListViewModel.LoadFromChanges calls
        // ApplyDisplayMode immediately after construction to install the
        // mode the rest of the VM was loaded with.
        ApplyDisplayMode(FileListDisplayMode.GroupedByDirectory);
    }

    /// <summary>
    /// Repopulate <see cref="Children"/> for the supplied display mode:
    /// the flat <see cref="FileListDisplayMode.FullPath"/> and
    /// <see cref="FileListDisplayMode.RepoRelative"/> modes project from
    /// <see cref="Entries"/> (files listed directly under the section);
    /// <see cref="FileListDisplayMode.GroupedByDirectory"/> projects from
    /// <see cref="RootItems"/> (a nested directory tree, with repo-root
    /// files surfaced as siblings of the root directories). Called by
    /// <see cref="FileListViewModel"/> after construction and again whenever
    /// the active display mode changes, so the unified TreeView reflects
    /// the right shape without rebuilding the section.
    /// </summary>
    internal void ApplyDisplayMode(FileListDisplayMode mode)
    {
        Children.Clear();
        if (mode == FileListDisplayMode.GroupedByDirectory)
        {
            foreach (var item in RootItems) Children.Add(item);
        }
        else
        {
            foreach (var entry in Entries) Children.Add(entry);
        }
    }
}
