using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DiffViewer.Models;
using DiffViewer.ViewModels;

namespace DiffViewer.Rendering;

/// <summary>
/// Thin vertical strip painted between (or alongside) the diff panes. Hunks
/// render as a pair of small rectangles — deletions on the left, insertions
/// on the right — at vertical positions proportional to their location in
/// the corresponding file. Mixed hunks (those that both delete and insert
/// content) get a horizontal gradient ribbon connecting the two rects.
///
/// <para>Implementation notes:</para>
/// <list type="bullet">
///   <item>Each column scales independently to its file's line count, so
///   the relative heights of the two columns also visualise the relative
///   sizes of the two files (mirrors JetBrains' diff bar).</item>
///   <item>Markers shorter than <see cref="HunkOverviewBarGeometry.MinHitHeight"/>
///   are inflated so they stay clickable on any DPI.</item>
///   <item>Hover tooltip shows the hunk's old- and new-side line ranges so
///   the user can scan without clicking. Clicking a marker (or the ribbon
///   between two markers) jumps the editors to that hunk via
///   <see cref="DiffPaneViewModel.JumpToHunk"/>.</item>
///   <item>Cluster markers, popups, and keyboard focus are listed in the
///   plan but deferred to a follow-up — the hit-test still falls through
///   to the nearest marker when two rects overlap.</item>
/// </list>
/// </summary>
public sealed class HunkOverviewBar : FrameworkElement
{
    /// <summary>
    /// Width of each colored column (deletions on the left, insertions on
    /// the right). The middle of the bar is reserved for the gradient
    /// ribbons that connect paired markers.
    /// </summary>
    private const double ColumnWidth = 10.0;

    /// <summary>Total bar width — two columns plus a ribbon gutter.</summary>
    private const double DefaultBarWidth = 32.0;

    private DiffPaneViewModel? _vm;

    /// <summary>
    /// Phase of the bar's left-button interaction. <c>Idle</c> is the
    /// default. <c>PendingClick</c> means the user pressed but we have
    /// not yet decided whether it's a click (jump-to-hunk) or a drag
    /// (band scroll) — promoted to <c>Dragging</c> on the first
    /// MouseMove that exceeds the system drag threshold, or resolved as
    /// a click on MouseUp if the threshold is never crossed.
    /// </summary>
    private enum BarInteractionState
    {
        Idle,
        PendingClick,
        Dragging,
    }

    private BarInteractionState _interaction = BarInteractionState.Idle;

    /// <summary>
    /// Mouse position recorded at MouseDown, used as the threshold
    /// reference for promoting <c>PendingClick</c> to <c>Dragging</c>.
    /// </summary>
    private Point _interactionStartPoint;

    /// <summary>
    /// Hunk index hit by the MouseDown, or <c>-1</c> if none. Commits
    /// to a JumpToHunk on MouseUp iff we never promoted to drag and the
    /// pointer never strayed past the system click-vs-drag threshold.
    /// </summary>
    private int _pendingHunkIndex = -1;

    /// <summary>
    /// True if the MouseDown landed inside the viewport band — the
    /// click-vs-drag promotion is gated on this so a press outside the
    /// band (e.g. on a hunk marker that doesn't overlap the band)
    /// can never accidentally start a drag.
    /// </summary>
    private bool _bandWasUnderClick;

    /// <summary>
    /// Pixel offset from the click point to the band's top edge,
    /// recorded at MouseDown. Replayed on every <see cref="OnMouseMove"/>
    /// during a drag so the cursor "sticks" to whichever part of the
    /// band the user grabbed (scrollbar-thumb feel). Anchored on top
    /// (not center) so the resulting fraction maps 1:1 to the editor's
    /// scroll offset — pixel-precise drag with no line-rounding.
    /// </summary>
    private double _bandDragOffsetFromTop;

    public HunkOverviewBar()
    {
        Cursor = Cursors.Hand;
        ToolTipService.SetInitialShowDelay(this, 200);
        ToolTipService.SetShowDuration(this, 8000);
        Width = DefaultBarWidth;
        SnapsToDevicePixels = true;
        Focusable = false;

        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => AttachToVm(DataContext as DiffPaneViewModel);
        Unloaded += (_, _) => DetachFromVm();
        MouseMove += OnMouseMove;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachFromVm();
        AttachToVm(e.NewValue as DiffPaneViewModel);
    }

    private void AttachToVm(DiffPaneViewModel? vm)
    {
        _vm = vm;
        if (_vm is null) return;
        _vm.HighlightMapChanged += OnHighlightMapChanged;
        _vm.ColorSchemeChanged += OnColorSchemeChanged;
        _vm.PropertyChanged += OnVmPropertyChanged;
        InvalidateVisual();
    }

    private void DetachFromVm()
    {
        if (_vm is null) return;
        _vm.HighlightMapChanged -= OnHighlightMapChanged;
        _vm.ColorSchemeChanged -= OnColorSchemeChanged;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = null;
    }

    private void OnHighlightMapChanged(object? sender, EventArgs e) => InvalidateVisual();
    private void OnColorSchemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiffPaneViewModel.CurrentHunkIndex) ||
            e.PropertyName == nameof(DiffPaneViewModel.Viewport))
            InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Faint background so the strip is visible even before any diff is
        // loaded — gives the user a column to aim at and reinforces the
        // gutter between the two text panes.
        var bg = new SolidColorBrush(Color.FromArgb(0x10, 0x80, 0x80, 0x80));
        if (bg.CanFreeze) bg.Freeze();
        dc.DrawRectangle(bg, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (_vm is null || ActualHeight <= 0 || ActualWidth <= 0) return;
        var hunks = _vm.CurrentHunks;
        if (hunks.Count == 0) return;

        int leftTotal = Math.Max(1, _vm.LeftDocument.LineCount);
        int rightTotal = Math.Max(1, _vm.RightDocument.LineCount);

        var scheme = _vm.CurrentColorScheme;
        var removedBrush = scheme.RemovedIntraLineBackground;
        var addedBrush = scheme.AddedIntraLineBackground;
        Color removedColor = ((SolidColorBrush)removedBrush).Color;
        Color addedColor = ((SolidColorBrush)addedBrush).Color;

        var layouts = HunkOverviewBarGeometry.ComputeLayouts(
            hunks, leftTotal, rightTotal, ActualWidth, ActualHeight, ColumnWidth);

        // Outline pen for the active hunk. Reused per-layout below.
        var activePen = new Pen(Brushes.Black, 1.0);
        if (activePen.CanFreeze) activePen.Freeze();

        for (int i = 0; i < layouts.Count; i++)
        {
            var layout = layouts[i];

            // Mixed hunk: paint the trapezoid first so the column rects
            // sit on top (column rects own the click target's left/right
            // ends; the ribbon owns the middle).
            if (layout.LeftRect is Rect L && layout.RightRect is Rect R)
            {
                var ribbon = BuildTrapezoid(L, R);
                var gradient = new LinearGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    StartPoint = new Point(L.Right, 0),
                    EndPoint = new Point(R.Left, 0),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(removedColor, 0),
                        new GradientStop(addedColor, 1),
                    },
                };
                if (gradient.CanFreeze) gradient.Freeze();
                dc.DrawGeometry(gradient, null, ribbon);
            }

            if (layout.LeftRect is Rect lr)
                dc.DrawRectangle(removedBrush, null, lr);
            if (layout.RightRect is Rect rr)
                dc.DrawRectangle(addedBrush, null, rr);

            // Highlight the active hunk by outlining the whole section
            // (left rect + ribbon + right rect) as a single polygon so it
            // reads as one shape instead of two disconnected boxes.
            if (i == _vm.CurrentHunkIndex)
            {
                var outline = BuildOutline(layout.LeftRect, layout.RightRect);
                if (outline is not null)
                    dc.DrawGeometry(null, activePen, outline);
            }
        }

        // Viewport indicator sits ON TOP of the hunks: the soft accent-
        // blue fill is translucent enough that an underlying hunk's red /
        // green / yellow still reads through (the user can still see WHAT
        // changed inside the visible window), but the crisp outline keeps
        // the band visible even when it lines up exactly with a hunk that
        // spans the whole viewport. Painted only when a viewport is
        // known — null before the first layout pass or when no file is
        // loaded.
        var viewportBand = HunkOverviewBarGeometry.ComputeViewport(
            _vm.Viewport, leftTotal, rightTotal, ActualWidth, ActualHeight, ColumnWidth);
        if (viewportBand is not null)
        {
            DrawViewport(dc, viewportBand);
        }
    }

    /// <summary>
    /// Paint the viewport band: soft accent-blue fill + crisp outline,
    /// reusing the same union-polygon geometry the active-hunk outline
    /// uses. Brushes resolve from <see cref="FrameworkElement.TryFindResource"/>
    /// so themes can override them; falls back to hardcoded constants
    /// when the resources aren't present (e.g. in unit tests or before
    /// <see cref="DiffPaneView"/>'s resources are loaded).
    ///
    /// <para>Stroke is 1.5 px (slightly thicker than the active-hunk
    /// outline) so the band stays visible over a hunk that fills the
    /// whole viewport — without it, a pure-insert hunk's green column
    /// can completely hide a 1 px outline.</para>
    /// </summary>
    private void DrawViewport(DrawingContext dc, HunkOverviewBarGeometry.ViewportBand band)
    {
        var outline = BuildOutline(band.LeftRect, band.RightRect);
        if (outline is null) return;

        var fill = TryFindResource("HunkBarViewportFillBrush") as Brush
                   ?? new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0x78, 0xD4));
        var stroke = TryFindResource("HunkBarViewportStrokeBrush") as Brush
                     ?? new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0x78, 0xD4));
        if (fill is SolidColorBrush fb && fb.CanFreeze) fb.Freeze();
        if (stroke is SolidColorBrush sb && sb.CanFreeze) sb.Freeze();
        var pen = new Pen(stroke, 1.5);
        if (pen.CanFreeze) pen.Freeze();
        dc.DrawGeometry(fill, pen, outline);
    }

    /// <summary>
    /// Build the outline geometry for a (left, right) rect pair:
    /// <list type="bullet">
    ///   <item>Both rects present: trace the outer perimeter of
    ///   (LeftRect ∪ ribbon ∪ RightRect) as one polygon so it reads as
    ///   a single connected shape.</item>
    ///   <item>Only one rect: just that rect — there's no ribbon to
    ///   include.</item>
    /// </list>
    /// Used by both the active-hunk highlight and the viewport band so
    /// the two indicators share visual language.
    /// </summary>
    private static Geometry? BuildOutline(Rect? leftRect, Rect? rightRect)
    {
        if (leftRect is Rect L && rightRect is Rect R)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(L.Left, L.Top), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(L.Right, L.Top), true, false);    // top of L
                ctx.LineTo(new Point(R.Left, R.Top), true, false);     // ribbon top
                ctx.LineTo(new Point(R.Right, R.Top), true, false);    // top of R
                ctx.LineTo(new Point(R.Right, R.Bottom), true, false); // right of R
                ctx.LineTo(new Point(R.Left, R.Bottom), true, false);  // bottom of R
                ctx.LineTo(new Point(L.Right, L.Bottom), true, false); // ribbon bottom
                ctx.LineTo(new Point(L.Left, L.Bottom), true, false);  // bottom of L
            }
            if (g.CanFreeze) g.Freeze();
            return g;
        }
        if (leftRect is Rect lOnly) return new RectangleGeometry(lOnly);
        if (rightRect is Rect rOnly) return new RectangleGeometry(rOnly);
        return null;
    }

    /// <summary>
    /// Build the trapezoid path that bridges <paramref name="left"/> and
    /// <paramref name="right"/> for a mixed hunk: top edge connects the two
    /// rects' top corners, bottom edge connects their bottom corners.
    /// </summary>
    private static StreamGeometry BuildTrapezoid(Rect left, Rect right)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(left.Right, left.Top), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(right.Left, right.Top), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(right.Left, right.Bottom), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(left.Right, left.Bottom), isStroked: false, isSmoothJoin: false);
        }
        if (g.CanFreeze) g.Freeze();
        return g;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_vm is null) return;
        var p = e.GetPosition(this);
        var layouts = ComputeLayouts();
        int idx = HunkOverviewBarGeometry.HitTest(layouts, p);

        int leftTotal = Math.Max(1, _vm.LeftDocument.LineCount);
        int rightTotal = Math.Max(1, _vm.RightDocument.LineCount);
        var band = HunkOverviewBarGeometry.ComputeViewport(
            _vm.Viewport, leftTotal, rightTotal, ActualWidth, ActualHeight, ColumnWidth);
        bool inBand = band is not null && HunkOverviewBarGeometry.IsInsideBand(band, p);

        // Empty press — let it bubble.
        if (idx < 0 && !inBand) return;

        // Defer both the JumpToHunk commit and the drag start to
        // MouseMove/MouseUp so a click that lands on a hunk marker
        // overlapping the viewport band can still promote to a drag
        // instead of immediately firing JumpToHunk and scrolling out
        // from under the user.
        _interactionStartPoint = p;
        _pendingHunkIndex = idx;
        _bandWasUnderClick = inBand;
        if (inBand)
        {
            _bandDragOffsetFromTop = p.Y - HunkOverviewBarGeometry.GetBandTopY(band!);
        }
        _interaction = BarInteractionState.PendingClick;
        ToolTip = null;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_interaction == BarInteractionState.Idle) return;

        bool committedAsDrag = _interaction == BarInteractionState.Dragging;
        int hunkIdx = _pendingHunkIndex;
        bool stayedWithinClickThreshold = ExceededDragThreshold(e.GetPosition(this)) == false;

        ResetInteraction();

        // Commit the JumpToHunk only if the gesture stayed a click:
        // never promoted to drag AND never wandered past the system
        // click-vs-drag threshold. The threshold check matches the
        // standard Windows button convention ("click and drag off
        // cancels"); without it, a near-miss drag on a hunk-marker-only
        // press (no band overlap) would still fire JumpToHunk.
        if (!committedAsDrag && stayedWithinClickThreshold && hunkIdx >= 0 && _vm is not null)
        {
            _vm.JumpToHunk(hunkIdx);
        }
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // Captures can be lost without a corresponding MouseUp (e.g. the
        // user Alt-Tabs away). Clear interaction state so we don't get
        // stuck mid-drag or with a pending click that never resolves.
        ResetInteraction();
    }

    private void ResetInteraction()
    {
        _interaction = BarInteractionState.Idle;
        _pendingHunkIndex = -1;
        _bandWasUnderClick = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    /// <summary>
    /// True if <paramref name="p"/> has wandered far enough from
    /// <see cref="_interactionStartPoint"/> to count as a drag rather
    /// than a click, per the system-wide drag thresholds. Either axis
    /// crossing the threshold is enough — matches how WPF's own
    /// drag-and-drop machinery and the standard listbox drag-select
    /// behave.
    /// </summary>
    private bool ExceededDragThreshold(Point p)
    {
        double dx = Math.Abs(p.X - _interactionStartPoint.X);
        double dy = Math.Abs(p.Y - _interactionStartPoint.Y);
        return dx >= SystemParameters.MinimumHorizontalDragDistance
            || dy >= SystemParameters.MinimumVerticalDragDistance;
    }

    /// <summary>
    /// Wheel scrolling over the bar mirrors wheel scrolling over the
    /// editor itself: one notch (<see cref="MouseWheelEventArgs.Delta"/>
    /// of 120) moves the editor by
    /// <see cref="SystemParameters.WheelScrollLines"/> lines. Sign is
    /// inverted from <c>Delta</c> because a positive <c>Delta</c> means
    /// the wheel rolled away from the user (scroll up = decrease line
    /// number), and our line-delta convention is "positive = scroll
    /// toward later lines".
    /// </summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_vm is null || e.Delta == 0) return;

        // Whole-notch math — fractional wheel deltas (high-precision
        // trackpads) accumulate into MouseWheelEventArgs.Delta as integer
        // multiples of 120, so integer division is safe and matches
        // editor scroll behaviour exactly.
        int linesPerNotch = Math.Max(1, SystemParameters.WheelScrollLines);
        int notches = e.Delta / 120;
        if (notches == 0)
        {
            // Sub-notch wheel events (rare on Windows; mostly Mac magic
            // mice via remote desktop). Round away from zero so a small
            // flick still scrolls at least one line in the right
            // direction.
            notches = e.Delta > 0 ? 1 : -1;
        }
        int lineDelta = -notches * linesPerNotch;
        _vm.RequestScrollByLineDelta(lineDelta);
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_vm is null) return;
        var p = e.GetPosition(this);

        // Active drag: drive editor scroll from the cursor, preserving
        // the click-offset captured at MouseDown. Routed through
        // RequestScrollByVerticalFraction (not RequestScrollByFraction)
        // so each pixel of cursor movement maps to a sub-line of editor
        // scroll. No tooltip / no hunk hover during drag.
        if (_interaction == BarInteractionState.Dragging)
        {
            EmitDragScroll(p);
            return;
        }

        // Pending click + click-was-in-band: promote to drag once the
        // pointer moves past the system drag threshold. Press-and-hold
        // without movement stays pending so MouseUp can still commit as
        // a hunk-jump.
        if (_interaction == BarInteractionState.PendingClick
            && _bandWasUnderClick
            && ExceededDragThreshold(p))
        {
            _interaction = BarInteractionState.Dragging;
            EmitDragScroll(p);
            return;
        }

        // Pending click on a hunk-only press (no band overlap): nothing
        // to drag, so we just wait for MouseUp. Suppress tooltips while
        // a click is pending — tooltip flicker during a press is noise.
        if (_interaction == BarInteractionState.PendingClick)
        {
            return;
        }

        // Idle: regular hover tooltip on hunk markers.
        var hunks = _vm.CurrentHunks;
        if (hunks.Count == 0)
        {
            ToolTip = null;
            return;
        }
        var layouts = ComputeLayouts();
        int idx = HunkOverviewBarGeometry.HitTest(layouts, p);
        if (idx < 0)
        {
            ToolTip = null;
            return;
        }
        var h = hunks[idx];
        ToolTip = FormatTooltip(idx, hunks.Count, h, layouts[idx].Shape);
    }

    private void EmitDragScroll(Point p)
    {
        if (_vm is null) return;
        double targetBandTopY = p.Y - _bandDragOffsetFromTop;
        double fraction = ActualHeight <= 0 ? 0 : targetBandTopY / ActualHeight;
        _vm.RequestScrollByVerticalFraction(fraction);
    }

    private IReadOnlyList<HunkOverviewBarGeometry.HunkBarLayout> ComputeLayouts()
    {
        if (_vm is null) return Array.Empty<HunkOverviewBarGeometry.HunkBarLayout>();
        int leftTotal = Math.Max(1, _vm.LeftDocument.LineCount);
        int rightTotal = Math.Max(1, _vm.RightDocument.LineCount);
        return HunkOverviewBarGeometry.ComputeLayouts(
            _vm.CurrentHunks, leftTotal, rightTotal, ActualWidth, ActualHeight, ColumnWidth);
    }

    /// <summary>
    /// Builds a tooltip showing the hunk's index plus the per-side line
    /// ranges that actually changed. Pure-add and pure-delete hunks only
    /// show the side that has content; mixed hunks show both ranges so
    /// the user can correlate the bar with the diff text.
    ///
    /// <para>Note: this does NOT key off
    /// <see cref="DiffHunk.OldLineCount"/>/<see cref="DiffHunk.NewLineCount"/>
    /// because those include context lines — using them would claim
    /// "added L1-L6" for a pure-delete hunk that's just surrounded by
    /// 3 lines of context on each side, contradicting the bar.</para>
    /// </summary>
    internal static string FormatTooltip(int idx, int total, DiffHunk h, HunkChangeShape shape)
    {
        string? oldRange = ComputeChangedRange(h, isOldSide: true);
        string? newRange = ComputeChangedRange(h, isOldSide: false);

        return shape switch
        {
            HunkChangeShape.PureInsert when newRange is not null
                => $"Hunk {idx + 1} of {total}: added {newRange}",
            HunkChangeShape.PureDelete when oldRange is not null
                => $"Hunk {idx + 1} of {total}: removed {oldRange}",
            HunkChangeShape.Mixed when oldRange is not null && newRange is not null
                => $"Hunk {idx + 1} of {total}: old {oldRange} → new {newRange}",
            HunkChangeShape.Mixed when oldRange is not null
                => $"Hunk {idx + 1} of {total}: removed {oldRange}",
            HunkChangeShape.Mixed when newRange is not null
                => $"Hunk {idx + 1} of {total}: added {newRange}",
            _ => $"Hunk {idx + 1} of {total}",
        };
    }

    /// <summary>
    /// Compute the contiguous line-number range of the lines that actually
    /// changed on one side of the hunk. Returns <c>null</c> when nothing
    /// on that side changed (e.g. old-side for a pure-insert). Counts
    /// <see cref="DiffLineKind.Modified"/> on both sides since those lines
    /// exist in both buffers with intra-line edits.
    /// </summary>
    private static string? ComputeChangedRange(DiffHunk h, bool isOldSide)
    {
        int? first = null;
        int? last = null;
        int count = 0;
        foreach (var line in h.Lines)
        {
            bool counts = isOldSide
                ? line.Kind == DiffLineKind.Deleted || line.Kind == DiffLineKind.Modified
                : line.Kind == DiffLineKind.Inserted || line.Kind == DiffLineKind.Modified;
            if (!counts) continue;

            int? n = isOldSide ? line.OldLineNumber : line.NewLineNumber;
            if (n is null) continue;
            first ??= n;
            last = n;
            count++;
        }
        if (first is null || last is null || count == 0) return null;
        // Use count for the display so e.g. interleaved Modified ranges
        // still report an accurate line count even when first..last span
        // a wider window than `count`.
        return FormatRange(first.Value, count);
    }

    private static string FormatRange(int start, int count) =>
        count <= 1 ? $"L{start}" : $"L{start}-{start + count - 1} ({count} lines)";
}
