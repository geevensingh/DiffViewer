using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace DiffViewer.Views;

/// <summary>
/// Code-behind for <see cref="FileListView"/>. Layout-only plus a small
/// amount of view-chrome glue that re-wires section / directory header
/// clicks to expand-toggle instead of WPF's default select-the-row
/// behavior — no UI logic, no VM state.
/// </summary>
public partial class FileListView : UserControl
{
    public FileListView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Section and directory rows toggle their expand state on a left-click
    /// of the header, mimicking the affordance the old flat-mode
    /// <c>Expander</c> had (click anywhere on the header to open/close). The
    /// default <see cref="TreeViewItem"/> click behavior would instead just
    /// select the row, which produces a misleading "selected" highlight on
    /// a row that doesn't drive any selection-aware state in the VM.
    ///
    /// <para>Clicks that originate inside the chevron's
    /// <see cref="ToggleButton"/> are left alone — that button has its own
    /// click handler that toggles <see cref="TreeViewItem.IsExpanded"/>, so
    /// handling the click here too would double-toggle. The chevron-detect
    /// walk relies on the default WPF <c>TreeViewItem</c> template (only
    /// <see cref="ToggleButton"/> in the visual tree is the chevron); if a
    /// future custom template introduces another <see cref="ToggleButton"/>
    /// in the header it will need to opt out of this detection.</para>
    ///
    /// <para>The <see cref="NearestTreeViewItemAncestor"/> guard is what
    /// stops a click on a descendant row (e.g. a file row inside an
    /// unstaged section) from bubbling up and toggling the parent
    /// section: the parent's handler fires too because the event bubbles,
    /// but it bails immediately because the click originated inside the
    /// child's <see cref="TreeViewItem"/>, not the parent's.</para>
    /// </summary>
    private void OnHeaderLeftMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        if (sender is not TreeViewItem item) return;
        if (NearestTreeViewItemAncestor(e.OriginalSource as DependencyObject) != item) return;
        if (IsInsideToggleButton(e.OriginalSource as DependencyObject)) return;

        item.IsExpanded = !item.IsExpanded;
        item.IsSelected = false;
        e.Handled = true;
    }

    /// <summary>
    /// Suppress the right-click selection highlight on section and
    /// directory rows. No <c>ContextMenu</c> is wired to these tiers, so
    /// the only visible effect of WPF's default right-click-selects-the-row
    /// behavior would be a misleading "selected" highlight on a row that
    /// then does nothing. File rows keep their normal right-click behavior
    /// (the entry context menu fires from the row's <c>Grid</c> in
    /// <c>EntryTemplate</c>, below this handler in the visual tree).
    ///
    /// <para>This is a tunneling handler, so it would otherwise fire
    /// before the file row's right-click handlers and swallow the file's
    /// context menu entirely. The <see cref="NearestTreeViewItemAncestor"/>
    /// guard ensures we only suppress right-clicks that originated in
    /// this row's own header chrome, not in a descendant file or nested
    /// directory row.</para>
    /// </summary>
    private void OnHeaderRightMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem item) return;
        if (NearestTreeViewItemAncestor(e.OriginalSource as DependencyObject) != item) return;
        e.Handled = true;
    }

    private static TreeViewItem? NearestTreeViewItemAncestor(DependencyObject? source)
    {
        // Same Visual / FrameworkContentElement branching as
        // IsInsideToggleButton: a click on header label text starts at a
        // Run (content element), so we have to fall back to
        // LogicalTreeHelper for those nodes or VisualTreeHelper throws.
        while (source is not null)
        {
            if (source is TreeViewItem tvi) return tvi;
            source = source is Visual or Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    private static bool IsInsideToggleButton(DependencyObject? source)
    {
        // The walk needs to handle both visual ancestors (the chevron's
        // ToggleButton sits in the visual tree) and content-element
        // ancestors. A click on header label text reports
        // e.OriginalSource as a System.Windows.Documents.Run, which is a
        // FrameworkContentElement, not a Visual; VisualTreeHelper.GetParent
        // throws InvalidOperationException for non-Visual / non-Visual3D
        // inputs, so we have to branch and use LogicalTreeHelper there.
        // The loop terminates on null or a ToggleButton, or when we fall
        // off both trees.
        while (source is not null)
        {
            if (source is ToggleButton) return true;
            source = source is Visual or Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    /// <summary>
    /// Window-level Ctrl+/ wiring (issue #3). Called from
    /// <see cref="MainWindow"/> via the
    /// <see cref="ViewModels.MainViewModel.FocusFilterHandler"/> hook.
    /// Focus + select-all so the user can immediately type a fresh query
    /// or replace the prior one without an explicit clear.
    /// </summary>
    public void FocusFilter()
    {
        FilterBox.Focus();
        FilterBox.SelectAll();
    }

    /// <summary>
    /// Esc in the filter box clears the text and returns focus to the
    /// file list so the user can resume keyboard-driven navigation
    /// without a mouse trip. Other Esc behaviour at the Window level
    /// (clear selection / close dialogs) is unaffected because we only
    /// handle the keystroke while it's still inside the TextBox.
    /// </summary>
    private void OnFilterKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        FilterBox.Clear();
        FilesTree.Focus();
        e.Handled = true;
    }

    /// <summary>
    /// Click handler for the filter clear-X button. Clears the filter
    /// and puts focus back in the TextBox (not the file list) so the
    /// user can immediately type a new query — mouse-driven users tend
    /// to be working in the filter at that moment, not navigating with
    /// the keyboard.
    /// </summary>
    private void OnFilterClearClick(object sender, RoutedEventArgs e)
    {
        FilterBox.Clear();
        FilterBox.Focus();
    }

    /// <summary>
    /// Space-toggles-viewed on the selected row (issue #3). Scoped to
    /// the TreeView's KeyDown handler so it doesn't intercept Space in
    /// the filter TextBox or in the diff editor — both of which
    /// legitimately consume Space (text input / fold navigation).
    /// Marks the event handled so the keystroke doesn't bubble to the
    /// Window's broader input bindings.
    /// </summary>
    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        if (DataContext is not ViewModels.FileListViewModel vm) return;
        if (vm.SelectedEntry is null) return;

        vm.SelectedEntry.IsViewed = !vm.SelectedEntry.IsViewed;
        e.Handled = true;
    }

    /// <summary>
    /// Stop the per-row viewed CheckBox from doubling as a row click.
    /// Without this guard the press / release would bubble up to the
    /// containing TreeViewItem and trigger selection + scroll-into-view,
    /// which is jarring when the user's intent was just to toggle the
    /// checkbox. The CheckBox's own ToggleButton logic still fires
    /// because we run as a Preview handler — the Click event is
    /// raised separately on the routed-event cycle. We do not handle
    /// the routed Click itself, so the IsChecked binding fires
    /// normally.
    /// </summary>
    private void OnViewedCheckBoxPreviewMouse(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        // We swallowed the mouse event so the CheckBox's own toggle
        // wouldn't fire either; perform the toggle ourselves on the
        // press half of the event (PreviewMouseLeftButtonDown) and
        // ignore the release half.
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ButtonState != MouseButtonState.Pressed) return;
        if (sender is not CheckBox cb) return;
        cb.IsChecked = !(cb.IsChecked ?? false);
    }
}
