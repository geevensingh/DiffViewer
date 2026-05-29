using System.Windows;
using System.Windows.Media;
using DiffViewer.Models;
using ICSharpCode.AvalonEdit.Rendering;

namespace DiffViewer.Rendering;

/// <summary>
/// Background renderer used by the inline-mode editor. Reads its
/// per-line highlight dictionary from <see cref="LineHighlights"/> on every
/// <see cref="Draw"/> and paints the appropriate brush for that kind.
/// Hunk-header lines and blank separators are absent from the dictionary
/// and rendered without a tint.
///
/// <para>Brush selection is delegated to
/// <see cref="LineBackgroundBrushSelector"/> (with <see cref="DiffSide.Inline"/>);
/// see its summary for the strong-vs-soft rule.</para>
/// </summary>
public sealed class InlineDiffBackgroundRenderer : IBackgroundRenderer
{
    private readonly DiffColorScheme _scheme;

    public IReadOnlyDictionary<int, LineHighlight>? LineHighlights { get; set; }

    public InlineDiffBackgroundRenderer(DiffColorScheme scheme)
    {
        _scheme = scheme ?? throw new ArgumentNullException(nameof(scheme));
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (LineHighlights is null || LineHighlights.Count == 0) return;
        if (!textView.VisualLinesValid) return;

        foreach (var visualLine in textView.VisualLines)
        {
            int docLine = visualLine.FirstDocumentLine.LineNumber;
            if (!LineHighlights.TryGetValue(docLine, out var highlight)) continue;

            Brush brush = LineBackgroundBrushSelector.Pick(DiffSide.Inline, highlight.Kind, _scheme);

            // Paint the full visual-line box (VisualTop + Height) rather
            // than the text-segment rect from BackgroundGeometryBuilder:
            // the segment-rect approach leaves 1-2px horizontal gaps
            // between adjacent lines on some DPI / line-spacing settings.
            // VisualLine.Height covers all wrapped sub-rows for the
            // document line, so this works for word-wrap too. See the
            // matching comment in DiffBackgroundRenderer.Draw.
            double y = visualLine.VisualTop - textView.ScrollOffset.Y;
            drawingContext.DrawRectangle(
                brush,
                pen: null,
                new Rect(0, y, textView.ActualWidth, visualLine.Height));
        }
    }
}
