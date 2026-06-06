using System.Windows.Controls;

namespace DiffViewer.Views;

/// <summary>
/// Code-behind for <see cref="MarkdownDiffView"/>. The view is pure
/// XAML wiring (a <see cref="FlowDocumentScrollViewer"/> bound to
/// <see cref="DiffViewer.ViewModels.MarkdownDiffViewModel.Document"/>),
/// so there's no imperative work here — this file exists only because
/// WPF's compiled-XAML pipeline requires the <c>partial</c> class.
/// </summary>
public partial class MarkdownDiffView : UserControl
{
    public MarkdownDiffView()
    {
        InitializeComponent();
    }
}
