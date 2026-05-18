using System;
using System.Threading;
using System.Windows.Controls;
using DiffViewer.Behaviors;
using FluentAssertions;
using ICSharpCode.AvalonEdit;
using Xunit;

namespace DiffViewer.Tests.Behaviors;

/// <summary>
/// Tests for the <see cref="SearchPanelInstaller"/> attached behavior
/// that wires AvalonEdit's <c>SearchPanel</c> into the diff editors.
/// Mirrors the STA-thread harness from <see cref="CaretVisibilityTests"/>:
/// AvalonEdit's <c>TextEditor</c> creates WPF controls in its ctor and
/// can only be instantiated on an STA thread.
/// </summary>
public class SearchPanelInstallerTests
{
    [Fact]
    public void SetIsEnabled_True_RegistersSearchInputHandlerOnTextArea()
    {
        RunOnStaThread(() =>
        {
            var editor = new TextEditor();
            int initialHandlerCount = editor.TextArea.DefaultInputHandler.NestedInputHandlers.Count;

            SearchPanelInstaller.SetIsEnabled(editor, true);

            // SearchPanel.Install registers a SearchInputHandler in the
            // TextArea's default-input-handler nested chain. That's the
            // observable side effect we can verify without a layout pass.
            editor.TextArea.DefaultInputHandler.NestedInputHandlers.Count.Should().Be(
                initialHandlerCount + 1,
                "SearchPanel.Install must add exactly one nested input handler so the editor recognizes Ctrl+F / F3 / Shift+F3 / Esc");
        });
    }

    [Fact]
    public void SetIsEnabled_True_IsIdempotent()
    {
        RunOnStaThread(() =>
        {
            var editor = new TextEditor();
            int initialHandlerCount = editor.TextArea.DefaultInputHandler.NestedInputHandlers.Count;

            SearchPanelInstaller.SetIsEnabled(editor, true);
            SearchPanelInstaller.SetIsEnabled(editor, false);
            SearchPanelInstaller.SetIsEnabled(editor, true);

            // SearchPanel.Install itself is NOT idempotent — it
            // unconditionally creates a new panel and pushes a new
            // handler. The installer must guard against double-install
            // so a re-Loaded fire (or a toggle off-and-on) doesn't
            // stack multiple find panels on the same editor.
            editor.TextArea.DefaultInputHandler.NestedInputHandlers.Count.Should().Be(
                initialHandlerCount + 1,
                "the installer must guard against repeated installs even when IsEnabled toggles");
        });
    }

    [Fact]
    public void SetIsEnabled_OnNonTextEditor_DoesNotThrow()
    {
        RunOnStaThread(() =>
        {
            var notAnEditor = new Border();

            Action act = () => SearchPanelInstaller.SetIsEnabled(notAnEditor, true);

            act.Should().NotThrow(
                "the attached property is declared on DependencyObject and may be applied to non-TextEditor targets without crashing; the change handler must early-return safely");
            SearchPanelInstaller.GetIsEnabled(notAnEditor).Should().BeTrue(
                "the property storage itself must still work on any DependencyObject even when the change handler is a no-op");
        });
    }

    [Fact]
    public void GetIsEnabled_DefaultValue_IsFalse()
    {
        RunOnStaThread(() =>
        {
            var editor = new TextEditor();

            SearchPanelInstaller.GetIsEnabled(editor).Should().BeFalse(
                "the attached property must default to false so editors that don't opt in don't accidentally get a find panel wired up");
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
