using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiffViewer.Models;
using DiffViewer.Utility;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace DiffViewer.Services;

/// <summary>
/// <see cref="IImageDecoder"/> implementation for SVG, built on
/// SharpVectors.Wpf. Parses SVG XML to a WPF <see cref="DrawingGroup"/>
/// and rasterises it once at a high-resolution canvas
/// (<see cref="TargetMaxDimension"/> px on the longer axis) so the
/// diff-pane <c>Stretch=Uniform</c> down-sample stays crisp at every
/// reasonable zoom level. Issue #15.
///
/// <para><b>Threading.</b> Production decode runs on a <c>Task.Run</c>
/// worker thread (same pattern as <see cref="WpfImageDecoder"/>). The
/// SharpVectors parse, the standalone <see cref="DrawingVisual"/>, and
/// the <see cref="RenderTargetBitmap"/> render do not touch a UI
/// dispatcher, so they work on any thread. The result is
/// <see cref="System.Windows.Freezable.Freeze"/>n before being returned
/// so the caller can bind it from the UI thread without further
/// marshalling.</para>
///
/// <para><b>Failure modes.</b> XML parse errors, unsupported SVG
/// features, oversized renders, and IO faults are caught and turned
/// into an <see cref="ImageDecodeResult"/> with <c>Image = null</c>
/// and a short <c>Error</c> string. The caller (typically
/// <see cref="DiffViewer.ViewModels.DiffPaneViewModel"/>) falls back
/// to the XML text diff in that case rather than the binary
/// placeholder — SVG is text, so the XML view is the natural fallback.</para>
/// </summary>
public sealed class SharpVectorsSvgDecoder : IImageDecoder
{
    /// <summary>
    /// Longer-axis pixel dimension of the rasterised bitmap. The
    /// shorter axis is scaled to preserve the SVG's aspect ratio.
    /// 1024 px keeps a single rasterised side ≤ 4 MB
    /// (1024 × 1024 × 4 bytes), gives plenty of resolution for the
    /// diff pane to down-sample crisply, and avoids quietly blowing
    /// up memory on pathological SVGs.
    /// </summary>
    internal const int TargetMaxDimension = 1024;

    /// <summary>
    /// Upper bound on the SVG drawing's natural width or height. Any
    /// larger and we cap at <see cref="TargetMaxDimension"/> instead
    /// of risking a huge intermediate <see cref="RenderTargetBitmap"/>.
    /// Same threshold by design — see <see cref="TargetMaxDimension"/>.
    /// </summary>
    private const double NaturalDimensionCap = TargetMaxDimension;

    public ImageDecodeResult Decode(byte[] bytes, string? path)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length == 0)
            return new ImageDecodeResult(Image: null, Metadata: null, Error: "Empty buffer.");

        // Belt-and-braces: refuse to rasterise something that doesn't
        // even look like SVG. Callers route us via ImageFormat.Svg, so
        // this should never fire in production — but a malicious or
        // mislabelled blob shouldn't sink the whole decoder. The text
        // sniff is intentionally identical to ImageFormatDetector's so
        // dispatch and decode agree on what an SVG is.
        if (ImageFormatDetector.Detect(bytes, path) != ImageFormat.Svg)
            return new ImageDecodeResult(Image: null, Metadata: null, Error: "Not an SVG.");

        try
        {
            DrawingGroup? drawing;
            try
            {
                var settings = new WpfDrawingSettings
                {
                    // Default everything; the resulting DrawingGroup
                    // is detached from any element tree.
                    IncludeRuntime = false,
                    TextAsGeometry = false,
                };
                var reader = new FileSvgReader(settings);
                using var stream = new MemoryStream(bytes, writable: false);
                drawing = reader.Read(stream);
            }
            catch (Exception ex) when (
                ex is System.Xml.XmlException
                    or FormatException
                    or InvalidOperationException
                    or NotSupportedException
                    or ArgumentException
                    or IOException)
            {
                return new ImageDecodeResult(
                    Image: null, Metadata: null,
                    Error: $"SVG parse failed: {ex.GetType().Name}: {ex.Message}");
            }

            if (drawing is null)
            {
                return new ImageDecodeResult(
                    Image: null, Metadata: null,
                    Error: "SharpVectors returned a null drawing.");
            }

            var bounds = drawing.Bounds;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            {
                // Some SVGs (e.g. zero-size <svg width="0">) produce
                // empty bounds. Reporting an error is friendlier than
                // a 0×0 bitmap that triggers downstream divide-by-
                // zero risk.
                return new ImageDecodeResult(
                    Image: null, Metadata: null,
                    Error: "SVG rasterised to an empty canvas.");
            }

            // Clamp absurdly large SVGs (some screenshots from
            // figma-style tools come out at 50000×50000 in raw
            // viewBox units). We still preserve the aspect ratio,
            // just at a sane resolution.
            double naturalW = Math.Min(bounds.Width, NaturalDimensionCap);
            double naturalH = Math.Min(bounds.Height, NaturalDimensionCap);

            double maxDim = Math.Max(naturalW, naturalH);
            // Always scale up to TargetMaxDimension so small icons
            // stay crisp when the diff pane up-samples for display.
            // SharpVectors rendered the vector geometry, so scaling
            // here just costs pixels (memory), not fidelity.
            double scale = TargetMaxDimension / maxDim;

            int targetW = Math.Max(1, (int)Math.Round(naturalW * scale));
            int targetH = Math.Max(1, (int)Math.Round(naturalH * scale));

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // Translate so the drawing's natural top-left aligns
                // with (0, 0) on the bitmap, then scale to fill the
                // target rect. Order matters: translate first, then
                // scale.
                var tg = new TransformGroup();
                tg.Children.Add(new TranslateTransform(-bounds.X, -bounds.Y));
                tg.Children.Add(new ScaleTransform(scale, scale));
                dc.PushTransform(tg);
                dc.DrawDrawing(drawing);
                dc.Pop();
            }

            RenderTargetBitmap rtb;
            try
            {
                rtb = new RenderTargetBitmap(
                    pixelWidth: targetW,
                    pixelHeight: targetH,
                    dpiX: 96, dpiY: 96,
                    pixelFormat: PixelFormats.Pbgra32);
                rtb.Render(visual);
            }
            catch (Exception ex) when (
                ex is OutOfMemoryException
                    or ArgumentException
                    or InvalidOperationException)
            {
                return new ImageDecodeResult(
                    Image: null, Metadata: null,
                    Error: $"SVG render failed: {ex.GetType().Name}: {ex.Message}");
            }

            // Freeze so the bitmap can cross threads — required for
            // the same reason WpfImageDecoder freezes its BitmapFrame.
            if (rtb.CanFreeze && !rtb.IsFrozen)
                rtb.Freeze();

            var metadata = new Models.ImageMetadata(
                Width: targetW,
                Height: targetH,
                ByteSize: bytes.LongLength,
                Format: ImageFormat.Svg,
                FrameCount: 1);

            return new ImageDecodeResult(Image: rtb, Metadata: metadata, Error: null);
        }
        catch (Exception ex)
        {
            // Catch-all so an unexpected SharpVectors quirk doesn't
            // crash the dispatcher. The wide net mirrors
            // WpfImageDecoder's contract — the Decode method must
            // not throw.
            return new ImageDecodeResult(
                Image: null, Metadata: null,
                Error: $"SVG decode failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
