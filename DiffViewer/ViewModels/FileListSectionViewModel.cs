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
    }
}
