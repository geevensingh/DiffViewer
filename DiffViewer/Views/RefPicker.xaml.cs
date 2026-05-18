using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DiffViewer.ViewModels;

namespace DiffViewer.Views;

/// <summary>
/// View wiring for the ref-picker popup. The control's
/// <see cref="UserControl.DataContext"/> is a <see cref="RefPickerViewModel"/>
/// owned by the surrounding form VM; the popup's <see cref="IsOpen"/>
/// and <see cref="PlacementTarget"/> are exposed as dependency
/// properties so the host form can drive them from a "Pick…" toggle
/// button in pure XAML.
///
/// <para><b>Why a UserControl over a raw <c>Popup</c></b>: the
/// dialog's two-pane <c>DataTemplate</c>s would otherwise need to
/// inline the same 100-line popup body next to every commit-ish
/// input. Encapsulating it in a UserControl keeps each input's XAML
/// to a single <c>&lt;vw:RefPicker /&gt;</c> tag and centralises the
/// keyboard / loading / closing wiring.</para>
/// </summary>
public partial class RefPicker : UserControl
{
    /// <summary>Drives the embedded <see cref="Popup.IsOpen"/>. Two-way
    /// so the popup's <c>StaysOpen=False</c> auto-dismiss flows back to
    /// the host toggle button's <c>IsChecked</c> state.</summary>
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(
            nameof(IsOpen),
            typeof(bool),
            typeof(RefPicker),
            new FrameworkPropertyMetadata(
                defaultValue: false,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>The element the popup anchors itself to (typically the
    /// "Pick…" toggle button in the host form's row layout).</summary>
    public static readonly DependencyProperty PlacementTargetProperty =
        DependencyProperty.Register(
            nameof(PlacementTarget),
            typeof(UIElement),
            typeof(RefPicker),
            new PropertyMetadata(null));

    public UIElement? PlacementTarget
    {
        get => (UIElement?)GetValue(PlacementTargetProperty);
        set => SetValue(PlacementTargetProperty, value);
    }

    public RefPicker()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Kick off enumeration when the popup actually opens — not when
    /// the form is constructed. This avoids paying the
    /// branch-enumeration cost on dialog open for every commit-ish
    /// input the user never expands. Fire-and-forget; the VM handles
    /// re-entrancy and stale results internally.
    /// </summary>
    private async void OnPopupOpened(object sender, EventArgs e)
    {
        if (DataContext is not RefPickerViewModel vm) return;
        try
        {
            await vm.EnsureLoadedAsync();
        }
        catch
        {
            // EnsureLoadedAsync is supposed to swallow enumerator
            // failures internally (the production
            // LibGit2GitRefEnumerator returns Empty on throw). Belt
            // and suspenders so an unexpected exception here never
            // tears down the dialog.
        }
    }

    /// <summary>Close the popup after the user picks a row or runs
    /// the merge-base composer. The Button's Click event fires
    /// AFTER its Command, so by the time we run, the picker VM has
    /// already written the chosen ref back into the form.</summary>
    private void OnRefRowClicked(object sender, RoutedEventArgs e)
    {
        IsOpen = false;
    }
}
