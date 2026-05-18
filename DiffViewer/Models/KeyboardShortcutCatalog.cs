using System.Windows.Input;

namespace DiffViewer.Models;

/// <summary>
/// A single <c>(Key, Modifiers)</c> tuple. Mirrors a WPF
/// <c>KeyBinding</c> at the data level so the catalog and the XAML
/// can be compared in tests.
/// </summary>
/// <remarks>
/// Types in this file are <c>public</c> (not <c>internal</c>) because
/// the XAML binding system used by <c>KeyboardShortcutsDialog</c>
/// reflects against <c>public</c> properties on the dialog's
/// DataContext. This is a binding-surface requirement, not a
/// testability concession.
/// </remarks>
public readonly record struct KeyChord(Key Key, ModifierKeys Modifiers);

/// <summary>
/// One row in the cheat sheet.
/// </summary>
/// <param name="Gesture">
/// Human-readable gesture string (e.g. <c>"Ctrl+I"</c>,
/// <c>"F8 / Alt+↓"</c>, <c>"Right-click file"</c>). Used as the left
/// column in the dialog.
/// </param>
/// <param name="Description">
/// What the gesture does. Used as the right column.
/// </param>
/// <param name="ContextNote">
/// Optional small annotation rendered next to the description, e.g.
/// <c>"working tree only"</c> for actions that only apply when one
/// side of the comparison is the working tree.
/// </param>
public sealed record ShortcutEntry(string Gesture, string Description, string? ContextNote = null)
{
    /// <summary>
    /// The set of <c>(Key, Modifiers)</c> tuples this entry stands in
    /// for in <c>MainWindow.xaml</c>'s
    /// <c>&lt;Window.InputBindings&gt;</c>. Empty when the entry
    /// represents a code-behind shortcut (e.g. <c>Esc</c>,
    /// <c>Ctrl+F</c>) or a mouse-only action. The drift-detection test
    /// uses this list to assert a bijection between the XAML bindings
    /// and the catalog.
    /// </summary>
    public IReadOnlyList<KeyChord> XamlBindings { get; init; } = Array.Empty<KeyChord>();
}

/// <summary>
/// A labelled group of <see cref="ShortcutEntry"/> rows rendered as
/// one section in the cheat sheet.
/// </summary>
public sealed record ShortcutGroup(string Name, IReadOnlyList<ShortcutEntry> Entries);

/// <summary>
/// The canonical list of every keyboard shortcut and mouse action the
/// app exposes, used to drive the F1 cheat sheet
/// (<c>KeyboardShortcutsDialog</c>).
///
/// <para>The XAML <c>&lt;KeyBinding&gt;</c>s in <c>MainWindow.xaml</c>
/// are still the single source of truth for what the app actually
/// does — this catalog is the documentation layer. The
/// <c>KeyboardShortcutsDriftTests</c> unit test parses
/// <c>MainWindow.xaml</c> and asserts a bijection between its
/// <c>&lt;Window.InputBindings&gt;</c> set and the
/// <see cref="ShortcutEntry.XamlBindings"/> tuples collected across
/// every entry below. That gives us a verified single source of
/// truth without paying for the larger refactor of moving every
/// <c>KeyBinding</c> into code-behind.</para>
///
/// <para>Mouse actions and code-behind handlers (Esc, Ctrl+F) appear
/// here but have an empty <see cref="ShortcutEntry.XamlBindings"/>
/// list — they are intentionally excluded from the drift bijection.</para>
/// </summary>
public static class KeyboardShortcutCatalog
{
    public static IReadOnlyList<ShortcutGroup> Groups { get; } = BuildGroups();

    private static IReadOnlyList<ShortcutGroup> BuildGroups() => new ShortcutGroup[]
    {
        new("View", new ShortcutEntry[]
        {
            new("Ctrl+I", "Toggle side-by-side / inline")
            {
                XamlBindings = new[] { new KeyChord(Key.I, ModifierKeys.Control) },
            },
            new("Ctrl+D", "Toggle intra-line (word) diff")
            {
                XamlBindings = new[] { new KeyChord(Key.D, ModifierKeys.Control) },
            },
            new("Ctrl+W", "Toggle ignore whitespace")
            {
                XamlBindings = new[] { new KeyChord(Key.W, ModifierKeys.Control) },
            },
            new("Ctrl+Shift+W", "Toggle visible whitespace")
            {
                XamlBindings = new[] { new KeyChord(Key.W, ModifierKeys.Control | ModifierKeys.Shift) },
            },
            new("Ctrl+Shift+L", "Toggle word wrap")
            {
                XamlBindings = new[] { new KeyChord(Key.L, ModifierKeys.Control | ModifierKeys.Shift) },
            },
            new("Ctrl+1", "File list: full path")
            {
                XamlBindings = new[] { new KeyChord(Key.D1, ModifierKeys.Control) },
            },
            new("Ctrl+2", "File list: repo-relative")
            {
                XamlBindings = new[] { new KeyChord(Key.D2, ModifierKeys.Control) },
            },
            new("Ctrl+3", "File list: grouped by directory")
            {
                XamlBindings = new[] { new KeyChord(Key.D3, ModifierKeys.Control) },
            },
            new("Ctrl+/", "Focus file-list filter")
            {
                XamlBindings = new[] { new KeyChord(Key.OemQuestion, ModifierKeys.Control) },
            },
            new("Space", "Toggle viewed on the selected file", "file list focused"),
            new("Ctrl++", "Zoom in")
            {
                XamlBindings = new[]
                {
                    new KeyChord(Key.OemPlus, ModifierKeys.Control),
                    new KeyChord(Key.Add,     ModifierKeys.Control),
                },
            },
            new("Ctrl+-", "Zoom out")
            {
                XamlBindings = new[]
                {
                    new KeyChord(Key.OemMinus, ModifierKeys.Control),
                    new KeyChord(Key.Subtract, ModifierKeys.Control),
                },
            },
            new("Ctrl+0", "Reset zoom")
            {
                XamlBindings = new[]
                {
                    new KeyChord(Key.D0,      ModifierKeys.Control),
                    new KeyChord(Key.NumPad0, ModifierKeys.Control),
                },
            },
        }),

        new("Navigation", new ShortcutEntry[]
        {
            new("F8  /  Alt+↓", "Next change")
            {
                XamlBindings = new[]
                {
                    new KeyChord(Key.F8,   ModifierKeys.None),
                    new KeyChord(Key.Down, ModifierKeys.Alt),
                },
            },
            new("F7  /  Alt+↑", "Previous change")
            {
                XamlBindings = new[]
                {
                    new KeyChord(Key.F7, ModifierKeys.None),
                    new KeyChord(Key.Up, ModifierKeys.Alt),
                },
            },
            new("Shift+F8", "Next file")
            {
                XamlBindings = new[] { new KeyChord(Key.F8, ModifierKeys.Shift) },
            },
            new("Shift+F7", "Previous file")
            {
                XamlBindings = new[] { new KeyChord(Key.F7, ModifierKeys.Shift) },
            },
            new("Ctrl+F8", "Next section")
            {
                XamlBindings = new[] { new KeyChord(Key.F8, ModifierKeys.Control) },
            },
            new("Ctrl+F7", "Previous section")
            {
                XamlBindings = new[] { new KeyChord(Key.F7, ModifierKeys.Control) },
            },
            new("F6", "Cycle focus: file list → left → right")
            {
                XamlBindings = new[] { new KeyChord(Key.F6, ModifierKeys.None) },
            },
            new("Ctrl+F", "Find in the current diff editor"),
            new("Esc", "Close find panel / clear file selection"),
        }),

        new("App", new ShortcutEntry[]
        {
            new("F1", "Show this cheat sheet")
            {
                XamlBindings = new[] { new KeyChord(Key.F1, ModifierKeys.None) },
            },
            new("F5", "Refresh")
            {
                XamlBindings = new[] { new KeyChord(Key.F5, ModifierKeys.None) },
            },
            new("Ctrl+L", "Toggle live updates", "working tree only")
            {
                XamlBindings = new[] { new KeyChord(Key.L, ModifierKeys.Control) },
            },
            new("Ctrl+,", "Open Settings")
            {
                XamlBindings = new[] { new KeyChord(Key.OemComma, ModifierKeys.Control) },
            },
        }),

        new("Mouse actions", new ShortcutEntry[]
        {
            new("Right-click hunk", "Stage / Unstage / Revert hunk", "working tree only"),
            new("Right-click file", "Stage / Unstage / Revert / Delete file", "working tree only"),
            new("Right-click file", "Copy name / repo-relative / full path; copy blob SHA"),
            new("Right-click file", "Show in Explorer; open with default app; open in external editor"),
            new("Right-click file", "Add to .gitignore", "untracked files only"),
        }),
    };
}
