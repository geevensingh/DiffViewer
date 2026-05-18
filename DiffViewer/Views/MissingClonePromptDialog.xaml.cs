using System.Windows;
using DiffViewer.ViewModels;

namespace DiffViewer.Views;

/// <summary>
/// Modal dialog hosting <see cref="MissingClonePromptViewModel"/>.
/// View concerns only: auto-closes itself when the VM resolves (via
/// <see cref="MissingClonePromptViewModel.Completion"/>) so callers
/// can <c>ShowDialog()</c> and then <c>await vm.Completion</c>.
/// </summary>
public partial class MissingClonePromptDialog : Window
{
    public MissingClonePromptDialog(MissingClonePromptViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // Auto-dismiss the dialog when the VM resolves. Marshal to the
        // UI thread because Completion may be set from a background
        // continuation (the clone callback).
        vm.Completion.ContinueWith(
            _ => Dispatcher.BeginInvoke((Action)Close),
            TaskScheduler.FromCurrentSynchronizationContext());
    }
}
