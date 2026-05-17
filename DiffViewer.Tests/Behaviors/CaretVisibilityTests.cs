using System;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using DiffViewer.Behaviors;
using FluentAssertions;
using ICSharpCode.AvalonEdit;
using Xunit;

namespace DiffViewer.Tests.Behaviors;

public class CaretVisibilityTests
{
    [Fact]
    public void SetIsHidden_True_MakesCaretBrushTransparent()
    {
        RunOnStaThread(() =>
        {
            var editor = new TextEditor();
            editor.TextArea.Caret.CaretBrush.Should().BeNull(
                "default state must leave CaretBrush untouched so AvalonEdit's built-in brush stays in effect for editors that don't opt in");

            CaretVisibility.SetIsHidden(editor, true);

            editor.TextArea.Caret.CaretBrush.Should().Be(Brushes.Transparent);
        });
    }

    [Fact]
    public void SetIsHidden_False_AfterTrue_RestoresDefaultCaretBrush()
    {
        RunOnStaThread(() =>
        {
            var editor = new TextEditor();
            CaretVisibility.SetIsHidden(editor, true);
            editor.TextArea.Caret.CaretBrush.Should().Be(Brushes.Transparent);

            CaretVisibility.SetIsHidden(editor, false);

            editor.TextArea.Caret.CaretBrush.Should().BeNull(
                "flipping back to false must clear the transparent override so AvalonEdit's default caret behavior is restored");
        });
    }

    [Fact]
    public void GetIsHidden_DefaultValue_IsFalse()
    {
        RunOnStaThread(() =>
        {
            var editor = new TextEditor();

            CaretVisibility.GetIsHidden(editor).Should().BeFalse(
                "the attached property must default to false so editors that don't opt in keep AvalonEdit's normal caret");
        });
    }

    [Fact]
    public void SetIsHidden_OnNonTextEditor_DoesNotThrow()
    {
        RunOnStaThread(() =>
        {
            var notAnEditor = new Border();

            Action act = () => CaretVisibility.SetIsHidden(notAnEditor, true);

            act.Should().NotThrow(
                "the attached property is declared on DependencyObject and may be applied to non-TextEditor targets without crashing; the change handler must early-return safely");
            CaretVisibility.GetIsHidden(notAnEditor).Should().BeTrue(
                "the property storage itself must still work on any DependencyObject even when the change handler is a no-op");
        });
    }

    private static void RunOnStaThread(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (error != null)
        {
            throw new InvalidOperationException("STA test body threw.", error);
        }
    }
}
