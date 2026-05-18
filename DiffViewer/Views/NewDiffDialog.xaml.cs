using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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

    private void OnBrowseRepoPathClick(object sender, RoutedEventArgs e)
    {
        // The repo-path TextBox sits next to the Browse button in the
        // same Grid; find it by its `Tag="RepoPath"`. Going through
        // Tag rather than a named hard-coded lookup keeps the
        // code-behind agnostic to which form template raised the click.
        if (sender is not Button button) return;

        var container = FindAncestor<Grid>(button);
        if (container is null) return;

        TextBox? textBox = null;
        foreach (var child in container.Children)
        {
            if (child is TextBox tb && tb.Tag is "RepoPath")
            {
                textBox = tb;
                break;
            }
        }
        if (textBox is null) return;

        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Pick a repository folder",
            Multiselect = false,
        };
        var initial = textBox.Text;
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
        {
            picker.InitialDirectory = initial;
        }

        if (picker.ShowDialog(this) == true)
        {
            textBox.Text = picker.FolderName;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match) return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
