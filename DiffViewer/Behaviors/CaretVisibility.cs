using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;

namespace DiffViewer.Behaviors;

/// <summary>
/// Attached property that hides the blinking caret in an AvalonEdit
/// <see cref="TextEditor"/>. AvalonEdit renders a visible caret even
/// when <c>IsReadOnly="True"</c>, and the caret is the strongest
/// "this is editable" affordance in the diff panes — stronger than the
/// I-beam cursor, which is the OS-standard signal for "text is
/// selectable" (and is what GitHub's web diff, VS Code's diff view,
/// and Beyond Compare's read-only side all keep). Setting
/// <c>IsHidden="True"</c> on a <see cref="TextEditor"/> renders the
/// caret transparently while keeping the I-beam and click-drag text
/// selection intact.
/// </summary>
public static class CaretVisibility
{
    public static readonly DependencyProperty IsHiddenProperty =
        DependencyProperty.RegisterAttached(
            "IsHidden",
            typeof(bool),
            typeof(CaretVisibility),
            new PropertyMetadata(false, OnIsHiddenChanged));

    public static bool GetIsHidden(DependencyObject obj) =>
        (bool)obj.GetValue(IsHiddenProperty);

    public static void SetIsHidden(DependencyObject obj, bool value) =>
        obj.SetValue(IsHiddenProperty, value);

    private static void OnIsHiddenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextEditor editor)
        {
            return;
        }

        // Caret is a plain CLR object (not a DependencyObject) and
        // CaretBrush is a plain CLR property, so a Style.Setter on the
        // TextEditor can't reach it — direct assignment is the only
        // way. null restores AvalonEdit's built-in default brush.
        editor.TextArea.Caret.CaretBrush =
            (bool)e.NewValue ? Brushes.Transparent : null;
    }
}
