using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiffViewer.ViewModels;

namespace DiffViewer.Views;

/// <summary>
/// Code-behind for <see cref="ImageDiffView"/>. The view-model is a
/// pure POCO with no opinion on layout, so the small amount of
/// imperative work needed for the Swipe mode (rebuild the clip
/// geometry, position the divider line, and translate mouse drag
/// into <see cref="ImageDiffViewModel.SwipePosition"/>) lives here.
///
/// <para>The rest of the view (SideBySide and OnionSkin layouts,
/// header strip, opacity slider) is pure XAML wiring.</para>
/// </summary>
public partial class ImageDiffView : UserControl
{
    private ImageDiffViewModel? _vm;

    public ImageDiffView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SwipeCanvas.SizeChanged += (_, _) => UpdateSwipeOverlay();
        SwipeCanvas.MouseLeftButtonDown += OnSwipeMouseDown;
        SwipeCanvas.MouseMove += OnSwipeMouseMove;
        SwipeCanvas.MouseLeftButtonUp += OnSwipeMouseUp;
        SwipeCanvas.LostMouseCapture += OnSwipeLostMouseCapture;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = e.NewValue as ImageDiffViewModel;

        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;

        UpdateSwipeOverlay();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ImageDiffViewModel.SwipePosition)
                          or nameof(ImageDiffViewModel.Mode))
        {
            UpdateSwipeOverlay();
        }
    }

    private void UpdateSwipeOverlay()
    {
        if (_vm is null) return;
        var width = SwipeCanvas.ActualWidth;
        var height = SwipeCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var splitX = width * _vm.SwipePosition;
        SwipeLeftClip.Rect = new Rect(0, 0, splitX, height);
        SwipeDividerLine.X1 = splitX;
        SwipeDividerLine.X2 = splitX;
        SwipeDividerLine.Y1 = 0;
        SwipeDividerLine.Y2 = height;
    }

    private void OnSwipeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null) return;
        SwipeCanvas.CaptureMouse();
        UpdateSwipePositionFromMouse(e.GetPosition(SwipeCanvas));
    }

    private void OnSwipeMouseMove(object sender, MouseEventArgs e)
    {
        if (_vm is null || !SwipeCanvas.IsMouseCaptured) return;
        UpdateSwipePositionFromMouse(e.GetPosition(SwipeCanvas));
    }

    private void OnSwipeMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (SwipeCanvas.IsMouseCaptured) SwipeCanvas.ReleaseMouseCapture();
    }

    private void OnSwipeLostMouseCapture(object sender, MouseEventArgs e)
    {
        // Intentionally empty — the capture release is the signal we
        // care about; UpdateSwipeOverlay was already called during the
        // drag, and the property setter on the VM clamped the value.
    }

    private void UpdateSwipePositionFromMouse(Point p)
    {
        if (_vm is null) return;
        var width = SwipeCanvas.ActualWidth;
        if (width <= 0) return;
        // Clamping happens inside ImageDiffViewModel.SwipePosition's
        // OnSwipePositionChanged partial; the cast to [0..1] here is
        // defensive only.
        _vm.SwipePosition = System.Math.Clamp(p.X / width, 0.0, 1.0);
    }
}
