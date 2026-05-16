using System.Windows;
using System.Windows.Controls;
using DiffViewer.ViewModels;

namespace DiffViewer.Views;

/// <summary>
/// Picks the right <see cref="TreeViewItem"/> <see cref="Style"/> for the
/// unified grouped-by-directory <see cref="TreeView"/> in
/// <see cref="FileListView"/>. Each tier of the tree binds different state
/// (sections drive expand state via the shared header, directories drive
/// their own expand state, files drive selection), so a single uniform
/// style would either pollute the binding-error log or require synthetic
/// passthrough properties on every VM type. Three buckets is cleaner.
/// </summary>
public sealed class FileListItemContainerStyleSelector : StyleSelector
{
    public Style? SectionStyle { get; set; }
    public Style? DirectoryStyle { get; set; }
    public Style? FileStyle { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container) => item switch
    {
        FileListSectionViewModel => SectionStyle,
        DirectoryNodeViewModel => DirectoryStyle,
        FileEntryViewModel => FileStyle,
        _ => base.SelectStyle(item, container),
    };
}
