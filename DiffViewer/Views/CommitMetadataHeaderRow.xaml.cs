using System.Windows.Controls;

namespace DiffViewer.Views;

/// <summary>
/// Compact clickable header row that renders for each commit-side of a
/// comparison. DataContext is a
/// <see cref="DiffViewer.ViewModels.CommitMetadataPanelViewModel"/>.
/// Clicking anywhere on the row invokes its <c>ShowDetailsCommand</c>,
/// which the host wires to a modal-launching handler on
/// <see cref="DiffViewer.ViewModels.MainViewModel"/>.
/// </summary>
public partial class CommitMetadataHeaderRow : UserControl
{
    public CommitMetadataHeaderRow()
    {
        InitializeComponent();
    }
}
