using System.IO;
using System.Windows.Media.Imaging;
using DiffViewer.Models;
using DiffViewer.Utility;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="IImageDecoder"/> built on
/// <see cref="BitmapDecoder"/>. Decodes the first frame to a
/// frozen <see cref="BitmapSource"/> and reports the frame count back
/// in <see cref="ImageMetadata"/> so animated GIFs can be flagged in
/// the header strip (only frame 0 is rendered in v1).
///
/// <para><b>Color profiles.</b> Uses
/// <see cref="BitmapCreateOptions.IgnoreColorProfile"/> to sidestep
/// the well-known WPF hang on certain CMYK / odd-ICC PNGs. Pixel
/// colors may render slightly different from a "real" image viewer
/// as a result — acceptable for a diff tool.</para>
///
/// <para><b>Failure modes.</b> Any
/// <see cref="FileFormatException"/> / <see cref="NotSupportedException"/>
/// / <see cref="OutOfMemoryException"/> / <see cref="ArgumentException"/>
/// thrown by <see cref="BitmapDecoder.Create(System.IO.Stream, BitmapCreateOptions, BitmapCacheOption)"/>
/// is caught and turned into an
/// <see cref="ImageDecodeResult"/> with <c>Image = null</c> and a
/// short <c>Error</c> string. The caller — typically
/// <see cref="DiffViewer.ViewModels.DiffPaneViewModel"/> — falls back
/// to the existing binary placeholder in that case.</para>
/// </summary>
public sealed class WpfImageDecoder : IImageDecoder
{
    public ImageDecodeResult Decode(byte[] bytes, string? path)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length == 0)
            return new ImageDecodeResult(Image: null, Metadata: null, Error: "Empty buffer.");

        var format = ImageFormatDetector.Detect(bytes, path);
        if (format == ImageFormat.NotAnImage)
            return new ImageDecodeResult(Image: null, Metadata: null, Error: "Unrecognised image format.");

        try
        {
            // MemoryStream lifetime must exceed BitmapDecoder.Create
            // long enough for the cache load; BitmapCacheOption.OnLoad
            // forces a full read here so the stream can be disposed
            // after the call returns.
            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
            {
                return new ImageDecodeResult(
                    Image: null, Metadata: null,
                    Error: "Decoder reported zero frames.");
            }

            // ICOs embed the same icon at multiple resolutions (16x16,
            // 32x32, 48x48, 256x256). Frames[0] is typically the
            // smallest and looks blurry when fit-to-canvas, so pick the
            // largest frame by area for icons. For animated GIFs and
            // single-frame formats Frames[0] is correct, so the
            // selector is gated on format.
            BitmapFrame frame = format == ImageFormat.Ico
                ? PickLargestFrame(decoder.Frames)
                : decoder.Frames[0];
            // Freeze so the bitmap can cross threads. Required because
            // the production decode runs on a Task.Run worker and the
            // bitmap is handed off to the UI thread for binding.
            if (frame.CanFreeze && !frame.IsFrozen)
                frame.Freeze();

            var metadata = new ImageMetadata(
                Width: frame.PixelWidth,
                Height: frame.PixelHeight,
                ByteSize: bytes.LongLength,
                Format: format,
                FrameCount: decoder.Frames.Count);

            return new ImageDecodeResult(Image: frame, Metadata: metadata, Error: null);
        }
        catch (Exception ex) when (
            ex is FileFormatException
                or NotSupportedException
                or OutOfMemoryException
                or ArgumentException
                or InvalidOperationException
                or IOException)
        {
            return new ImageDecodeResult(
                Image: null, Metadata: null,
                Error: $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // Picks the largest frame by pixel area, with bit depth as a
    // tiebreaker so a 256x256 32bpp icon beats a 256x256 4bpp icon.
    // WIC exposes Format.BitsPerPixel; for unknown formats it returns
    // 0 which sorts last, also fine.
    private static BitmapFrame PickLargestFrame(IReadOnlyList<BitmapFrame> frames)
    {
        BitmapFrame best = frames[0];
        long bestArea = (long)best.PixelWidth * best.PixelHeight;
        int bestDepth = best.Format.BitsPerPixel;
        for (int i = 1; i < frames.Count; i++)
        {
            var f = frames[i];
            long area = (long)f.PixelWidth * f.PixelHeight;
            int depth = f.Format.BitsPerPixel;
            if (area > bestArea || (area == bestArea && depth > bestDepth))
            {
                best = f;
                bestArea = area;
                bestDepth = depth;
            }
        }
        return best;
    }
}
