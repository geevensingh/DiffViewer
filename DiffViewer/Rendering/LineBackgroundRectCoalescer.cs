using System.Windows;
using System.Windows.Media;

namespace DiffViewer.Rendering;

/// <summary>
/// Merges vertically-adjacent same-brush background rectangles into
/// single taller rectangles before they reach
/// <see cref="System.Windows.Media.DrawingContext.DrawRectangle"/>.
///
/// <para>The diff background renderers
/// (<see cref="DiffBackgroundRenderer"/> and
/// <see cref="InlineDiffBackgroundRenderer"/>) walk visible lines one
/// at a time and emit one rectangle per line per
/// <c>BackgroundGeometryBuilder.GetRectsFromVisualSegment</c> result.
/// When several consecutive lines share the same tint (e.g. a block
/// of added or modified rows) those rects are stacked vertically with
/// no gap mathematically, but WPF antialiases the top and bottom
/// edges of each individual rect. Where two same-brush rects meet,
/// each contributes a sub-pixel antialiased edge against the
/// transparent background underneath — and the two faint AA stripes
/// add up to a visible horizontal seam between the rows.</para>
///
/// <para>Coalescing the rects into a single taller rect removes the
/// internal edges entirely, so the only AA seams are at the top and
/// bottom of each coloured block (where they belong). Brushes are
/// compared by reference because the <see cref="DiffColorScheme"/>
/// hands out shared brush instances; two visual lines with the same
/// kind will always pick the same brush instance.</para>
///
/// <para>This is a pure transformation — input order is preserved and
/// no merge is performed across non-adjacent or different-brush rects.
/// The coalescer is stateless and safe to call once per Draw pass.</para>
/// </summary>
internal static class LineBackgroundRectCoalescer
{
    // Tolerance for treating two rects as adjacent / aligned. AvalonEdit
    // produces rects in WPF DIPs that should align exactly across visual
    // lines, but using a half-pixel tolerance defends against sub-pixel
    // drift introduced by zoom levels and layout rounding.
    private const double Epsilon = 0.5;

    public static IEnumerable<(Rect Rect, Brush Brush)> Coalesce(
        IEnumerable<(Rect Rect, Brush Brush)> input)
    {
        ArgumentNullException.ThrowIfNull(input);

        (Rect Rect, Brush Brush)? current = null;

        foreach (var next in input)
        {
            if (current is null)
            {
                current = next;
                continue;
            }

            var (curRect, curBrush) = current.Value;

            if (CanMerge(curRect, curBrush, next.Rect, next.Brush))
            {
                current = (
                    new Rect(
                        curRect.X,
                        curRect.Y,
                        curRect.Width,
                        next.Rect.Bottom - curRect.Y),
                    curBrush);
            }
            else
            {
                yield return current.Value;
                current = next;
            }
        }

        if (current is not null)
        {
            yield return current.Value;
        }
    }

    private static bool CanMerge(Rect a, Brush brushA, Rect b, Brush brushB)
    {
        if (!ReferenceEquals(brushA, brushB)) return false;
        if (Math.Abs(a.X - b.X) > Epsilon) return false;
        if (Math.Abs(a.Width - b.Width) > Epsilon) return false;
        if (Math.Abs(a.Bottom - b.Y) > Epsilon) return false;
        return true;
    }
}
