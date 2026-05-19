using System.IO;
using DiffViewer.Models;

namespace DiffViewer.Utility;

/// <summary>
/// Identifies image container formats from the first few bytes of a blob,
/// with a fallback to the file extension when the magic-byte signature is
/// inconclusive. Magic bytes are checked first so a mislabelled file
/// (e.g. a JPEG saved with a <c>.png</c> extension) is classified by what
/// it actually is rather than what it says it is.
///
/// <para>Used by the image-diff dispatch in
/// <see cref="DiffViewer.ViewModels.DiffPaneViewModel"/> to decide whether
/// a binary blob should be routed to the image pane or fall through to
/// the "Binary file - diff not displayed." placeholder.</para>
///
/// <para>Mirrors <see cref="BinaryDetector"/>'s style: pure static,
/// no allocations on the hot path, lives in <c>DiffViewer.Utility</c>.</para>
/// </summary>
internal static class ImageFormatDetector
{
    // 8-byte PNG signature (RFC 2083 §3.1).
    private static ReadOnlySpan<byte> PngSignature => new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
    };

    // JPEG SOI marker — every valid JPEG starts with FF D8 FF.
    private static ReadOnlySpan<byte> JpegSignature => new byte[]
    {
        0xFF, 0xD8, 0xFF,
    };

    // GIF87a / GIF89a (the only two GIF version headers ever shipped).
    private static ReadOnlySpan<byte> Gif87aSignature => "GIF87a"u8;
    private static ReadOnlySpan<byte> Gif89aSignature => "GIF89a"u8;

    // BMP — 2-byte "BM" header. Weakest of the magic-byte signatures;
    // we still accept it on bytes alone because no other common format
    // starts with those two ASCII letters.
    private static ReadOnlySpan<byte> BmpSignature => "BM"u8;

    // ICO — 4-byte header: 00 00 (reserved) + 01 00 (image type = icon,
    // little-endian). CUR files use 02 00 in the type field and are not
    // supported. The signature is short and not particularly distinctive,
    // but combined with the extension fallback in DetectByExtension this
    // is enough for practical use.
    private static ReadOnlySpan<byte> IcoSignature => new byte[]
    {
        0x00, 0x00, 0x01, 0x00,
    };

    /// <summary>
    /// Identify the image format of <paramref name="bytes"/>. Falls back
    /// to <paramref name="path"/>'s extension when the byte signature is
    /// inconclusive (or <paramref name="bytes"/> is empty), so we can
    /// still dispatch zero-byte add/delete sides on extension alone.
    /// </summary>
    public static ImageFormat Detect(ReadOnlySpan<byte> bytes, string? path)
    {
        if (StartsWith(bytes, PngSignature)) return ImageFormat.Png;
        if (StartsWith(bytes, JpegSignature)) return ImageFormat.Jpeg;
        if (StartsWith(bytes, Gif87aSignature) ||
            StartsWith(bytes, Gif89aSignature)) return ImageFormat.Gif;
        if (StartsWith(bytes, BmpSignature)) return ImageFormat.Bmp;
        if (StartsWith(bytes, IcoSignature)) return ImageFormat.Ico;

        return path is null ? ImageFormat.NotAnImage : DetectByExtension(path);
    }

    /// <summary>
    /// Extension-only detection. Used by <see cref="Detect"/> when bytes
    /// are inconclusive, and directly by callers that have a path but no
    /// bytes (e.g. an add-only or delete-only side).
    /// </summary>
    public static ImageFormat DetectByExtension(string? path)
    {
        if (string.IsNullOrEmpty(path)) return ImageFormat.NotAnImage;

        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return ImageFormat.NotAnImage;

        return ext.ToLowerInvariant() switch
        {
            ".png" => ImageFormat.Png,
            ".jpg" => ImageFormat.Jpeg,
            ".jpeg" => ImageFormat.Jpeg,
            ".gif" => ImageFormat.Gif,
            ".bmp" => ImageFormat.Bmp,
            ".ico" => ImageFormat.Ico,
            _ => ImageFormat.NotAnImage,
        };
    }

    private static bool StartsWith(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> signature)
        => bytes.Length >= signature.Length && bytes[..signature.Length].SequenceEqual(signature);
}
