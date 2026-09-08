using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DiffViewer.ViewModels;

namespace DiffViewer.Views;

/// <summary>
/// View wiring for the worktree-picker popup. Mirrors
/// <see cref="RefPicker"/>: the control's
/// <see cref="UserControl.DataContext"/> is a
/// <see cref="WorktreePickerViewModel"/> owned by the surrounding form,
/// and <see cref="IsOpen"/> / <see cref="PlacementTarget"/> are exposed
/// as dependency properties so the host can drive them from a toggle
/// button in pure XAML.
/// </summary>
public partial class WorktreePicker : UserControl
{
    /// <summary>Drives the embedded <see cref="Popup.IsOpen"/>. Two-way
    /// so the popup's <c>StaysOpen=False</c> auto-dismiss flows back to
    /// the host toggle button's <c>IsChecked</c> state.</summary>
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(
            nameof(IsOpen),
            typeof(bool),
            typeof(WorktreePicker),
            new FrameworkPropertyMetadata(
                defaultValue: false,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>The element the popup anchors itself to.</summary>
    public static readonly DependencyProperty PlacementTargetProperty =
        DependencyProperty.Register(
            nameof(PlacementTarget),
            typeof(UIElement),
            typeof(WorktreePicker),
            new PropertyMetadata(null));

    public UIElement? PlacementTarget
    {
        get => (UIElement?)GetValue(PlacementTargetProperty);
        set => SetValue(PlacementTargetProperty, value);
    }

    public WorktreePicker()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Enumerate when the popup opens rather than when the form is
    /// constructed, so opening the dialog never pays for a worktree scan
    /// the user didn't ask for. Fire-and-forget; the VM handles
    /// re-entrancy and stale results internally.
    /// </summary>
    private async void OnPopupOpened(object sender, EventArgs e)
    {
        if (DataContext is not WorktreePickerViewModel vm) return;
        try
        {
            await vm.EnsureLoadedAsync();
        }
        catch
        {
            // EnsureLoadedAsync does not catch: it relies on the
            // enumerator contract, and LibGit2GitWorktreeEnumerator
            // returns an empty list rather than throwing. This handler
            // is the actual safety net — an async void event handler is
            // where an unexpected exception would otherwise tear the
            // dialog down.
        }
    }

    /// <summary>Close the popup after a pick. The Button's Click fires
    /// after its Command, so the VM has already written the chosen
    /// path back into the form by the time we run.</summary>
    private void OnWorktreeRowClicked(object sender, RoutedEventArgs e)
    {
        IsOpen = false;
    }
}
