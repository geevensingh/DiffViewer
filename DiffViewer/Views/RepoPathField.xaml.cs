using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DiffViewer.Views;

/// <summary>
/// The repository-path input shared by every local "New diff" form: a
/// text box, a folder browser, and the worktree switcher.
///
/// <para>Its <see cref="FrameworkElement.DataContext"/> is inherited
/// from the host form, which must be a
/// <see cref="DiffViewer.ViewModels.LocalRepoFormViewModelBase"/> — the
/// control binds to that base's <c>RepoPath</c> and
/// <c>WorktreePicker</c>.</para>
///
/// <para><b>Why a UserControl</b>: this row previously existed as five
/// copy-pasted <c>Grid</c>s, one per form template, with the Browse
/// handler locating its text box by walking the visual tree for a
/// <c>Tag="RepoPath"</c> marker. Adding the worktree switcher would
/// have made that six elements copied five ways. One control means one
/// place to change, and the text box is now simply named.</para>
/// </summary>
public partial class RepoPathField : UserControl
{
    public RepoPathField()
    {
        InitializeComponent();
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Pick a repository folder",
            Multiselect = false,
        };

        var initial = PathBox.Text;
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
        {
            picker.InitialDirectory = initial;
        }

        if (picker.ShowDialog(Window.GetWindow(this)) == true)
        {
            PathBox.Text = picker.FolderName;
        }
    }
}
