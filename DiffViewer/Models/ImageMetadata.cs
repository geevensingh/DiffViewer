namespace DiffViewer.Models;

/// <summary>
/// Per-side metadata for the image-diff header strip. One instance is
/// produced for each side that has bytes; the side is <c>null</c> on
/// add-only / delete-only changes.
///
/// <para>Built by <see cref="DiffViewer.Services.IImageDecoder"/> as a
/// by-product of decoding the bytes. The fields drive the
/// "512 × 512 / 18.3 KB → 1024 × 1024 / 64.1 KB  (+45.8 KB)" summary
/// rendered above the image pane.</para>
/// </summary>
/// <param name="Width">Pixel width of the rendered frame.</param>
/// <param name="Height">Pixel height of the rendered frame.</param>
/// <param name="ByteSize">Size of the post-filter blob in bytes — the
/// same number the file list shows.</param>
/// <param name="Format">Identified container format from
/// <see cref="DiffViewer.Utility.ImageFormatDetector"/>.</param>
/// <param name="FrameCount">Number of frames in the container.
/// <see cref="ImageFormat.Gif"/> with <c>FrameCount &gt; 1</c> triggers
/// the "(animated, first frame)" hint in the header — WPF's
/// <c>BitmapDecoder</c> only renders the first frame in v1; full
/// animation is out of scope. All other formats report <c>1</c>.</param>
public sealed record ImageMetadata(
    int Width,
    int Height,
    long ByteSize,
    ImageFormat Format,
    int FrameCount);
