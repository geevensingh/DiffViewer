using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.Utility;
using DiffViewer.ViewModels;
using DiffViewer.Views;

namespace DiffViewer;

public partial class MainWindow : Window
{
    /// <summary>
    /// Default size used when no valid saved geometry is available.
    /// Matches the historical XAML values before window-state
    /// persistence was added.
    /// </summary>
    private const double DefaultWindowWidth = 1200;
    private const double DefaultWindowHeight = 800;

    private readonly ISettingsService? _settingsService;

    /// <summary>
    /// Cached reference to the file-list pane's <see cref="ColumnDefinition"/>
    /// inside the <see cref="MainViewModel"/> DataTemplate. Captured when
    /// the template loads and used by
    /// <see cref="OnFileListSplitterDragCompleted"/> to read the current
    /// pixel width after a drag. <c>null</c> when no MainViewModel is the
    /// active DataContext (e.g. the empty-context template is showing
    /// instead).
    /// </summary>
    private ColumnDefinition? _fileListColumn;

    /// <summary>
    /// Parameterless ctor for XAML designer / preview tooling only. The
    /// runtime always uses <see cref="MainWindow(ISettingsService)"/> so
    /// saved window geometry can be restored and re-persisted.
    /// </summary>
    public MainWindow() : this(settingsService: null) { }

    public MainWindow(ISettingsService? settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        ApplyInitialGeometry();
        DataContextChanged += OnDataContextChanged;
        if (_settingsService is not null)
        {
            // Subscribe after applying initial geometry so the restore
            // itself does not trigger a redundant save.
            StateChanged += OnWindowStateChanged;
            Closing += OnWindowClosing;
        }
    }

    /// <summary>
    /// Apply saved size/position/maximized state from settings, or fall
    /// back to <see cref="DefaultWindowWidth"/>×<see cref="DefaultWindowHeight"/>
    /// centered on the primary screen. Multi-monitor validation lives in
    /// <see cref="WindowGeometryValidator"/>.
    /// </summary>
    private void ApplyInitialGeometry()
    {
        var saved = _settingsService?.Current.WindowState;
        var resolved = WindowGeometryValidator.Resolve(
            saved,
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        if (resolved is null)
        {
            Width = DefaultWindowWidth;
            Height = DefaultWindowHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = resolved.Left;
        Top = resolved.Top;
        Width = resolved.Width;
        Height = resolved.Height;
        if (resolved.IsMaximized)
        {
            // Pre-Show() WindowState assignment is honored by WPF; the
            // pre-maximize Left/Top/Width/Height become the
            // RestoreBounds the user will un-maximize to.
            WindowState = System.Windows.WindowState.Maximized;
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        // Persist on Normal <-> Maximized transitions. Skip Minimized
        // (a) so a minimize-and-quit does not lose the maximized bit,
        // and (b) because restoring to Minimized would be user-hostile.
        if (WindowState == System.Windows.WindowState.Minimized) return;
        SaveCurrentGeometry();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (WindowState == System.Windows.WindowState.Minimized) return;
        SaveCurrentGeometry();
    }

    /// <summary>
    /// Capture the *restore* bounds (so un-maximize returns to the right
    /// place) plus the maximized bit, and persist via
    /// <see cref="ISettingsService"/>. Any I/O failure is swallowed: a
    /// best-effort persist must never crash the app on shutdown.
    /// </summary>
    private void SaveCurrentGeometry()
    {
        if (_settingsService is null) return;

        bool isMaximized = WindowState == System.Windows.WindowState.Maximized;
        Rect bounds = isMaximized
            ? RestoreBounds
            : new Rect(Left, Top, ActualWidth, ActualHeight);

        if (bounds.IsEmpty ||
            double.IsNaN(bounds.Width) || double.IsNaN(bounds.Height) ||
            bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var snapshot = new WindowStateSnapshot(
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, isMaximized);
        try
        {
            _settingsService.Update(s => s with { WindowState = snapshot });
        }
        catch
        {
            // Best-effort persistence on a closing window — never crash.
        }
    }

    /// <summary>
    /// Fires when the <see cref="MainViewModel"/> DataTemplate's root Grid
    /// is loaded. The file-list ColumnDefinition lives inside that
    /// template, so it isn't a code-behind field on the Window — we
    /// reach it via the Grid's <see cref="Grid.ColumnDefinitions"/>.
    /// Applies the persisted width from
    /// <see cref="AppSettings.FileListPaneWidthPixels"/> (clamped via
    /// <see cref="FileListLayout.ClampWidth"/>) so the user opens
    /// looking at the split they left at.
    /// </summary>
    private void OnMainLayoutGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Grid grid || grid.ColumnDefinitions.Count == 0) return;

        _fileListColumn = grid.ColumnDefinitions[0];

        double saved = _settingsService?.Current.FileListPaneWidthPixels
                       ?? FileListLayout.DefaultFileListPaneWidthPixels;
        _fileListColumn.Width = new GridLength(FileListLayout.ClampWidth(saved));
    }

    /// <summary>
    /// Fires once at the end of each splitter drag. Persists the new
    /// column width via <see cref="ISettingsService"/>. We persist on
    /// DragCompleted rather than on window <c>Closing</c> so a
    /// crash-mid-session never loses the user's most recent drag.
    /// Best-effort: any I/O failure is swallowed so a transient
    /// permission glitch can't bubble out of a UI event handler.
    /// </summary>
    private void OnFileListSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_settingsService is null || _fileListColumn is null) return;

        double width = FileListLayout.ClampWidth(_fileListColumn.ActualWidth);
        try
        {
            _settingsService.Update(s => s with { FileListPaneWidthPixels = width });
        }
        catch
        {
            // Best-effort persistence on a UI event — never crash the app.
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MainViewModel vm)
        {
            vm.ShowSettingsHandler = ShowSettingsDialog;
            vm.ShowKeyboardShortcutsHandler = ShowKeyboardShortcutsDialog;
            vm.ConfirmHandler = ShowConfirmDialog;
            vm.ToastHandler = ShowToast;
            vm.FocusCycleRequested = CycleFocusAcrossPanes;
        }
    }

    /// <summary>
    /// 3-stop focus cycle: file list → left diff editor → right diff editor →
    /// file list. Implemented via direct FocusManager calls so AvalonEdit's
    /// internal Tab handling stays unaffected. If focus is somewhere
    /// unexpected (e.g. inside an open dialog), the cycle resets to the
    /// file list as the fallback start point.
    /// </summary>
    private void CycleFocusAcrossPanes()
    {
        // Find the three target controls by name. They live in named
        // resources in their respective Views; we walk the visual tree.
        var fileList = FindDescendant<System.Windows.Controls.ListBox>("FileListBox")
                       ?? FindDescendant<System.Windows.Controls.TreeView>("FileListTree")
                       ?? FindDescendant<System.Windows.Controls.ItemsControl>(null);
        var leftEditor  = FindDescendant<ICSharpCode.AvalonEdit.TextEditor>("LeftEditor");
        var rightEditor = FindDescendant<ICSharpCode.AvalonEdit.TextEditor>("RightEditor");

        var stops = new System.Collections.Generic.List<System.Windows.IInputElement>();
        if (fileList    is not null) stops.Add(fileList);
        if (leftEditor  is not null) stops.Add(leftEditor);
        if (rightEditor is not null) stops.Add(rightEditor);
        if (stops.Count == 0) return;

        var focused = FocusManager.GetFocusedElement(this);
        int currentIndex = focused is null ? -1 : stops.IndexOf(focused);
        var next = stops[(currentIndex + 1) % stops.Count];
        next.Focus();
    }

    private T? FindDescendant<T>(string? name) where T : DependencyObject
    {
        return FindDescendantCore<T>(this, name);
    }

    private static T? FindDescendantCore<T>(DependencyObject root, string? name) where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                if (name is null) return match;
                if (child is FrameworkElement fe && fe.Name == name) return match;
            }
            var deep = FindDescendantCore<T>(child, name);
            if (deep is not null) return deep;
        }
        return null;
    }

    /// <summary>
    /// Window-level key handler. Currently routes two shortcuts:
    /// <list type="bullet">
    /// <item><c>Esc</c> — cascading-close that prefers an open find
    /// panel over the file-list deselect fallback.</item>
    /// <item><c>Ctrl+F</c> — if focus is outside the three diff
    /// editors, focus the visible one and open its find bar. When
    /// focus is already inside an editor we let the event keep
    /// tunneling so the editor's own <c>SearchPanel</c> binding
    /// handles it.</item>
    /// </list>
    /// </summary>
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            HandleEscape(e);
            return;
        }
        if (e.Key == System.Windows.Input.Key.F &&
            System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            HandleCtrlF(e);
            return;
        }
    }

    /// <summary>
    /// Esc cascading-close: closes the closest find-panel first.
    /// 1. If the focused diff pane has its find panel open, close it.
    /// 2. Else if another diff pane has its find open, close it.
    /// 3. Else clear the file-list selection.
    /// </summary>
    private void HandleEscape(System.Windows.Input.KeyEventArgs e)
    {
        var leftEditor   = FindDescendant<ICSharpCode.AvalonEdit.TextEditor>("LeftEditor");
        var rightEditor  = FindDescendant<ICSharpCode.AvalonEdit.TextEditor>("RightEditor");
        var inlineEditor = FindDescendant<ICSharpCode.AvalonEdit.TextEditor>("InlineEditor");
        var focused = FocusManager.GetFocusedElement(this);

        // Walk focus → other side → inline → fall through to selection
        // clear. Only one editor is "visible" at a time (SBS hides
        // Inline; inline hides SBS) so TryCloseFindPanel naturally
        // short-circuits on collapsed editors via the IsVisible check
        // on the panel itself.
        var focusOwner = WhichEditor(focused, leftEditor, rightEditor, inlineEditor);
        var ordered = focusOwner switch
        {
            "left"   => new[] { leftEditor,   rightEditor,  inlineEditor },
            "right"  => new[] { rightEditor,  leftEditor,   inlineEditor },
            "inline" => new[] { inlineEditor, leftEditor,   rightEditor  },
            _        => new[] { leftEditor,   rightEditor,  inlineEditor },
        };
        foreach (var ed in ordered)
        {
            if (TryCloseFindPanel(ed)) { e.Handled = true; return; }
        }
        if (DataContext is MainViewModel vm && vm.FileList.SelectedEntry is not null)
        {
            vm.FileList.SelectedEntry = null;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Global Ctrl+F: route to the visible diff editor when focus is
    /// outside the three editors. The most common path here is
    /// "click file in list → press Ctrl+F" — file-list selection does
    /// not auto-transfer focus to the editor, so without this redirect
    /// Ctrl+F would feel broken from the file list. When focus is
    /// already inside an editor we deliberately do nothing: the event
    /// keeps tunneling and the editor's own <c>SearchPanel</c>-
    /// installed command binding handles Find natively.
    /// </summary>
    private void HandleCtrlF(System.Windows.Input.KeyEventArgs e)
    {
        var leftEditor   = FindDescendant<ICSharpCode.AvalonEdit.TextEditor>("LeftEditor");
        var rightEditor  = FindDescendant<ICSharpCode.AvalonEdit.TextEditor>("RightEditor");
        var inlineEditor = FindDescendant<ICSharpCode.AvalonEdit.TextEditor>("InlineEditor");
        var focused = FocusManager.GetFocusedElement(this);

        if (WhichEditor(focused, leftEditor, rightEditor, inlineEditor) is not null)
        {
            // Editor-internal Ctrl+F: let the editor's own SearchPanel
            // binding handle it via continued tunneling.
            return;
        }

        // Prefer InlineEditor (only visible in inline mode), then Left,
        // then Right (the rare side-by-side fallback when Left is
        // hidden via the per-side toggles). IsVisible reflects the
        // effective visibility through the Visibility bindings on the
        // editors and their containing Grids.
        var candidates = new[] { inlineEditor, leftEditor, rightEditor };
        foreach (var editor in candidates)
        {
            if (editor is null) continue;
            if (!editor.IsVisible) continue;
            editor.Focus();
            if (System.Windows.Input.ApplicationCommands.Find.CanExecute(null, editor.TextArea))
            {
                System.Windows.Input.ApplicationCommands.Find.Execute(null, editor.TextArea);
                e.Handled = true;
            }
            return;
        }
    }

    private static string? WhichEditor(IInputElement? focused,
        ICSharpCode.AvalonEdit.TextEditor? left,
        ICSharpCode.AvalonEdit.TextEditor? right,
        ICSharpCode.AvalonEdit.TextEditor? inline)
    {
        if (focused is null) return null;
        if (left   is not null && IsLogicalDescendant(left,   focused as DependencyObject)) return "left";
        if (right  is not null && IsLogicalDescendant(right,  focused as DependencyObject)) return "right";
        if (inline is not null && IsLogicalDescendant(inline, focused as DependencyObject)) return "inline";
        return null;
    }

    private static bool IsLogicalDescendant(DependencyObject ancestor, DependencyObject? candidate)
    {
        while (candidate is not null)
        {
            if (ReferenceEquals(candidate, ancestor)) return true;
            candidate = System.Windows.Media.VisualTreeHelper.GetParent(candidate)
                        ?? System.Windows.LogicalTreeHelper.GetParent(candidate);
        }
        return false;
    }

    private static bool TryCloseFindPanel(ICSharpCode.AvalonEdit.TextEditor? editor)
    {
        if (editor is null) return false;
        // AvalonEdit's SearchPanel attaches itself as a logical child of the
        // TextArea once Ctrl+F has opened it once. We discover any open
        // SearchPanel by walking the visual tree under the editor and
        // checking IsVisible / IsClosed via reflection-friendly API.
        var panel = FindDescendantCore<ICSharpCode.AvalonEdit.Search.SearchPanel>(editor, null);
        if (panel is null) return false;
        // The Reopen / Close methods are public; IsClosed is internal, so
        // we just call Close() which is idempotent on an already-closed
        // panel and returns false in that case via the visual-tree probe
        // (we won't find a visual). Visibility check guards against
        // double-handling Esc when the panel is logically attached but
        // not currently shown.
        if (!panel.IsVisible) return false;
        panel.Close();
        return true;
    }


    private ConfirmationResult ShowConfirmDialog(ConfirmationRequest request)
    {
        var dialog = new ConfirmDialog(request) { Owner = this };
        dialog.ShowDialog();
        return dialog.Result;
    }

    private void ShowToast(string message)
    {
        // v1: simple status-line surface via the title bar; richer toast UX
        // is in the polish phase. Always marshal to the UI thread.
        Dispatcher.BeginInvoke(() =>
        {
            var prevTitle = Title;
            Title = $"DiffViewer — {message}";
            // Best-effort restore after 4s; not exact, but good enough
            // for a v1 status line.
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = System.TimeSpan.FromSeconds(4),
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (Title.StartsWith("DiffViewer — " + message))
                {
                    Title = prevTitle;
                }
            };
            timer.Start();
        });
    }

    private void ShowKeyboardShortcutsDialog()
    {
        var dialog = new KeyboardShortcutsDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void ShowSettingsDialog()
    {
        if (DataContext is not MainViewModel vm || vm.SettingsService is null) return;

        var dialogVm = new SettingsViewModel(
            vm.SettingsService,
            confirmReset: prompt => MessageBox.Show(
                this, prompt, "Reset settings",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK,
            availableFonts: DiffViewer.Rendering.SystemFontEnumerator.Enumerate(),
            pickFolder: initial =>
            {
                var picker = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Pick a folder",
                    Multiselect = false,
                };
                if (!string.IsNullOrWhiteSpace(initial) && System.IO.Directory.Exists(initial))
                {
                    picker.InitialDirectory = initial;
                }
                return picker.ShowDialog(this) == true ? picker.FolderName : null;
            },
            confirmRememberDefaultClone: parent => MessageBox.Show(
                this,
                $"Remember \"{parent}\" as the default destination for future clones?",
                "Default clone destination",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes);

        var dialog = new SettingsDialog(dialogVm) { Owner = this };
        try { dialog.ShowDialog(); }
        finally { dialogVm.Dispose(); }
    }
}
