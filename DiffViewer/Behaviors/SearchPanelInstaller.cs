using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Search;

namespace DiffViewer.Behaviors;

/// <summary>
/// Attached property that installs AvalonEdit's built-in
/// <see cref="SearchPanel"/> on a <see cref="TextEditor"/>. AvalonEdit's
/// <c>TextEditor</c> does not auto-install the search panel — the host
/// has to call <see cref="SearchPanel.Install(TextEditor)"/> explicitly,
/// otherwise Ctrl+F (and any <see cref="System.Windows.Input.ApplicationCommands.Find"/>
/// binding routed at the editor) is a no-op.
/// <para>
/// Setting <c>IsEnabled="True"</c> on a <see cref="TextEditor"/> installs
/// the search panel, giving it the standard Ctrl+F / F3 / Shift+F3 / Esc
/// keybinds plus the Match-Case / Whole-Words / Regex toggle buttons that
/// ship with the panel out of the box.
/// </para>
/// <para>
/// <see cref="SearchPanel.Install(TextEditor)"/> itself is NOT idempotent
/// — every call creates a fresh <see cref="SearchPanel"/> and pushes a
/// new nested input handler onto the editor's <c>TextArea</c>. To prevent
/// duplicate panels stacking on the same editor across <c>Loaded</c>
/// re-fires or <c>IsEnabled</c> toggles, this behavior tracks an
/// internal <c>IsInstalled</c> flag and short-circuits subsequent calls.
/// We don't expose an uninstall path because <see cref="SearchPanel"/>
/// has no public uninstall API; flipping <c>IsEnabled</c> to false is a
/// no-op rather than a misleading partial-uninstall.
/// </para>
/// </summary>
public static class SearchPanelInstaller
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SearchPanelInstaller),
            new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>
    /// Internal one-shot flag: set to true the first time we successfully
    /// call <see cref="SearchPanel.Install(TextEditor)"/> on a given
    /// editor, and read on every subsequent <see cref="IsEnabledProperty"/>
    /// change to short-circuit. Private — callers never read or write
    /// this directly.
    /// </summary>
    private static readonly DependencyProperty IsInstalledProperty =
        DependencyProperty.RegisterAttached(
            "IsInstalled",
            typeof(bool),
            typeof(SearchPanelInstaller),
            new PropertyMetadata(false));

    public static bool GetIsEnabled(DependencyObject obj) =>
        (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) =>
        obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextEditor editor)
        {
            return;
        }

        if (e.NewValue is not true)
        {
            return;
        }

        if ((bool)editor.GetValue(IsInstalledProperty))
        {
            return;
        }

        SearchPanel.Install(editor);
        editor.SetValue(IsInstalledProperty, true);
    }
}

