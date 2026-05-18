using System.Windows;
using System.Windows.Input;
using DiffViewer.ViewModels;

namespace DiffViewer.Views;

/// <summary>
/// Modal that opens when a user clicks a
/// <see cref="CommitMetadataHeaderRow"/>. Shows the full author / date
/// / SHA / message body for one side of the comparison, with a
/// Copy-SHA button that runs the VM's command (which in turn writes
/// the full 40-char SHA via the injected
/// <see cref="DiffViewer.Services.IClipboardService"/>).
///
/// <para>Closes on Esc or the Close button (both via
/// <c>IsCancel="True"</c>). No business logic in code-behind — just
/// the close-button click and a key handler for keyboard parity with
/// the cheat sheet's "press the opening key again to close" idiom
/// (no opening key here, so only Esc closes).</para>
/// </summary>
public partial class CommitMetadataDialog : Window
{
    public CommitMetadataDialog(CommitMetadataDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Esc is already handled by the Close button's IsCancel; this
        // hook is reserved for future shortcuts (e.g. Ctrl+C to copy
        // the SHA without finding the button). Empty for v1.
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
