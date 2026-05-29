using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace DiffViewer.Rendering;

/// <summary>
/// Inline-editor replacement for AvalonEdit's built-in
/// <see cref="ICSharpCode.AvalonEdit.Editing.LineNumberMargin"/>. Each
/// row's number comes from
/// <see cref="DiffPaneViewModel.InlineLineToSourceLines"/> via
/// <see cref="InlineLineNumberResolver"/>, so the gutter shows the
/// row's source-file line (<c>NewLine ?? OldLine</c>) instead of the
/// inline buffer's sequential row index. See
/// <see cref="InlineLineNumberResolver"/> for the per-row rule and the
/// rationale.
///
/// <para>Width is reserved for the widest source line that could be
/// drawn — which is normally the larger of the two file lengths, not
/// the inline buffer's row count, so the gutter is the same width as
/// (or slightly narrower than) the built-in margin would draw on the
/// matching side-by-side editors.</para>
/// </summary>
internal sealed class InlineLineNumberMargin : AbstractMargin
{
    private IReadOnlyList<(int? OldLine, int? NewLine)> _sourceLines = Array.Empty<(int?, int?)>();
    private Typeface _typeface = new(
        new FontFamily("Consolas"),
        FontStyles.Normal,
        FontWeights.Normal,
        FontStretches.Normal);
    private double _emSize = 12;

    public IReadOnlyList<(int? OldLine, int? NewLine)> SourceLines
    {
        get => _sourceLines;
        set
        {
            value ??= Array.Empty<(int?, int?)>();
            if (ReferenceEquals(_sourceLines, value)) return;
            _sourceLines = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _typeface = new Typeface(
            (FontFamily)GetValue(TextBlock.FontFamilyProperty),
            (FontStyle)GetValue(TextBlock.FontStyleProperty),
            (FontWeight)GetValue(TextBlock.FontWeightProperty),
            (FontStretch)GetValue(TextBlock.FontStretchProperty));
        _emSize = (double)GetValue(TextBlock.FontSizeProperty);

        int maxValue = Math.Max(
            1,
            InlineLineNumberResolver.MaxDisplayedValue(_sourceLines));
        int digits = maxValue.ToString(CultureInfo.InvariantCulture).Length;

        var sample = MakeFormattedText(
            new string('9', digits),
            (Brush)GetValue(Control.ForegroundProperty));

        return new Size(sample.Width, 0);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var textView = TextView;
        if (textView is null || !textView.VisualLinesValid) return;

        var foreground = (Brush)GetValue(Control.ForegroundProperty);
        double renderWidth = RenderSize.Width;

        foreach (var visualLine in textView.VisualLines)
        {
            int docLine = visualLine.FirstDocumentLine.LineNumber;
            int? toDisplay = InlineLineNumberResolver.Resolve(_sourceLines, docLine);
            if (toDisplay is null) continue;

            var text = MakeFormattedText(
                toDisplay.Value.ToString(CultureInfo.CurrentCulture),
                foreground);

            double y = visualLine.VisualTop - textView.VerticalOffset;
            drawingContext.DrawText(text, new Point(renderWidth - text.Width, y));
        }
    }

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null)
        {
            oldTextView.VisualLinesChanged -= OnTextViewVisualLinesChanged;
        }
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null)
        {
            newTextView.VisualLinesChanged += OnTextViewVisualLinesChanged;
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnTextViewVisualLinesChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    private FormattedText MakeFormattedText(string text, Brush foreground)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface,
            _emSize,
            foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}
