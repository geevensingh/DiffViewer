using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;

namespace DiffViewer.ViewModels;

/// <summary>
/// Per-<see cref="WorkingTreeLayer"/> section header used as the group key
/// in the flat-mode file list's grouped <see cref="System.Windows.Controls.ListBox"/>,
/// and shared with the corresponding <see cref="FileListSectionViewModel"/>
/// so the grouped-by-directory presentation reflects the same expand state.
///
/// <para>Instances are cached on <see cref="FileListViewModel"/> for the
/// lifetime of the VM. That cache is what makes <see cref="IsExpanded"/>
/// survive a <see cref="FileListViewModel.LoadFromChanges"/> rebuild — the
/// old <see cref="FileListSectionViewModel"/> instances are thrown away and
/// recreated, but the header (and thus the user's collapse state) is not.</para>
/// </summary>
public sealed partial class FileListSectionHeader : ObservableObject
{
    public WorkingTreeLayer Layer { get; }
    public string Header { get; }

    [ObservableProperty]
    private bool _isExpanded = true;

    public FileListSectionHeader(WorkingTreeLayer layer, string header)
    {
        Layer = layer;
        Header = header;
    }
}
