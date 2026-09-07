using System;
using System.Windows;
using DiffViewer.ViewModels;

namespace DiffViewer.Views;

/// <summary>
/// Modal dialog hosting <see cref="NewDiffDialogViewModel"/>. View
/// concerns only: auto-closes itself when the VM resolves
/// (<see cref="NewDiffDialogViewModel.Completion"/>) so callers can
/// <c>ShowDialog()</c> and then <c>await vm.Completion</c>, and routes
/// the per-form "Browse…" buttons through the standard folder picker.
/// </summary>
public partial class NewDiffDialog : Window
{
    private readonly NewDiffDialogViewModel _vm;

    public NewDiffDialog(NewDiffDialogViewModel vm)
    {
        InitializeComponent();
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        DataContext = vm;

        // Auto-dismiss when the VM resolves (OK or Cancel). Marshal
        // back to the UI thread because Completion may be set from a
        // background continuation in the test path.
        vm.Completion.ContinueWith(
            _ => Dispatcher.BeginInvoke((Action)Close),
            System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());

        // If the user closes the dialog via the [X] window button
        // (bypassing OK/Cancel commands), synthesise a Cancel so the
        // host's `await Completion` never hangs.
        Closed += (_, _) => _vm.ForceCancel();
    }
}
