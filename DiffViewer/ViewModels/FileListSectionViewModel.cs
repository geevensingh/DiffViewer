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
    public ObservableCollection<DirectoryNodeViewModel> RootDirectories { get; }

    /// <summary>
    /// First-level children for the unified TreeView, projected from
    /// <see cref="Entries"/> in the flat display modes and from
    /// <see cref="RootDirectories"/> in grouped-by-directory mode. Mutated
    /// in place by <see cref="ApplyDisplayMode"/> so the bound
    /// <c>HierarchicalDataTemplate.ItemsSource</c> receives incremental
    /// CollectionChanged notifications rather than a full PropertyChanged
    /// re-bind.
    ///
    /// <para>Typed as <see cref="object"/> because the two source collections
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
        RootDirectories = new ObservableCollection<DirectoryNodeViewModel>(
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
    /// <see cref="RootDirectories"/> (a nested directory tree). Called by
    /// <see cref="FileListViewModel"/> after construction and again whenever
    /// the active display mode changes, so the unified TreeView reflects
    /// the right shape without rebuilding the section.
    /// </summary>
    internal void ApplyDisplayMode(FileListDisplayMode mode)
    {
        Children.Clear();
        if (mode == FileListDisplayMode.GroupedByDirectory)
        {
            foreach (var dir in RootDirectories) Children.Add(dir);
        }
        else
        {
            foreach (var entry in Entries) Children.Add(entry);
        }
    }
}
