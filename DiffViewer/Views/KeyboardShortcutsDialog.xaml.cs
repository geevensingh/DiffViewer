using System.Windows;
using System.Windows.Input;
using DiffViewer.Models;

namespace DiffViewer.Views;

/// <summary>
/// Modal cheat sheet listing every keyboard shortcut and mouse action.
/// Opened from the main window with <c>F1</c>; closed with
/// <c>F1</c>, <c>Esc</c>, or the Close button. The DataContext is
/// <see cref="KeyboardShortcutCatalog.Groups"/> — there is no
/// view-model layer because the dialog is purely read-only.
/// </summary>
public partial class KeyboardShortcutsDialog : Window
{
    public KeyboardShortcutsDialog()
    {
        InitializeComponent();
        DataContext = KeyboardShortcutCatalog.Groups;
    }

    /// <summary>
    /// Esc is handled by the Close button's <c>IsCancel="True"</c>.
    /// F1 needs an explicit handler so pressing the same key that
    /// opened the dialog also closes it (the issue's spec).
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
