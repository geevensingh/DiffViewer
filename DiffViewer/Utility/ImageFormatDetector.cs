using System.IO;
using System.Text;
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

    // Three BOMs SharpVectors / XmlReader will accept on the input
    // stream. UTF-8 is by far the most common in the wild; UTF-16
    // LE/BE show up occasionally on Windows-authored files.
    private static ReadOnlySpan<byte> Utf8Bom => new byte[] { 0xEF, 0xBB, 0xBF };
    private static ReadOnlySpan<byte> Utf16LeBom => new byte[] { 0xFF, 0xFE };
    private static ReadOnlySpan<byte> Utf16BeBom => new byte[] { 0xFE, 0xFF };

    // SVG content-sniff probe size. SVG files commonly start with a
    // BOM + an XML prolog + optional comments / doctype / processing
    // instructions before the root <svg> tag. 1 KiB covers every
    // real-world preamble we've seen; staying under one page keeps
    // the cost trivial on the hot path.
    private const int SvgProbeBytes = 1024;

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
        if (LooksLikeSvg(bytes)) return ImageFormat.Svg;

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
            ".svg" => ImageFormat.Svg,
            _ => ImageFormat.NotAnImage,
        };
    }

    private static bool StartsWith(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> signature)
        => bytes.Length >= signature.Length && bytes[..signature.Length].SequenceEqual(signature);

    // Looks for an SVG root element in the first ~1 KiB. Handles the
    // four common shapes that show up in real-world SVGs:
    //   - bare <svg ...>
    //   - <?xml ...?> <svg ...>
    //   - <?xml ...?> <!DOCTYPE ...> <svg ...>
    //   - any of the above preceded by a UTF-8 / UTF-16 BOM
    // The probe is intentionally lenient (substring search after BOM
    // strip and whitespace skip) rather than a real XML parse — we
    // only need a yes/no on dispatch routing here; the actual parse
    // happens in SharpVectorsSvgDecoder where errors are properly
    // surfaced.
    private static bool LooksLikeSvg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return false;

        // Strip the leading BOM if present so the substring search
        // below doesn't need a separate code path for UTF-16 content
        // (UTF-16 SVG is rare but legal — Inkscape and some Windows
        // tools occasionally produce it).
        ReadOnlySpan<byte> probe = bytes.Length > SvgProbeBytes ? bytes[..SvgProbeBytes] : bytes;
        Encoding encoding;
        if (StartsWith(probe, Utf8Bom))
        {
            probe = probe[Utf8Bom.Length..];
            encoding = Encoding.UTF8;
        }
        else if (StartsWith(probe, Utf16LeBom))
        {
            probe = probe[Utf16LeBom.Length..];
            encoding = Encoding.Unicode;
        }
        else if (StartsWith(probe, Utf16BeBom))
        {
            probe = probe[Utf16BeBom.Length..];
            encoding = Encoding.BigEndianUnicode;
        }
        else
        {
            encoding = Encoding.UTF8;
        }

        // Empty after BOM strip — not SVG.
        if (probe.IsEmpty) return false;

        // Decode to a string for the substring search. The probe is
        // capped at 1 KiB, so this allocation is small and not on a
        // per-line hot path.
        string text;
        try
        {
            text = encoding.GetString(probe);
        }
        catch
        {
            return false;
        }

        // SVG roots almost always include the SVG namespace declaration
        // somewhere in the first tag. We accept either an "<svg" tag
        // OR the SVG namespace literal — the second covers files
        // produced by tools that emit unusual whitespace or non-ASCII
        // attribute layouts before the tag name.
        return text.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("http://www.w3.org/2000/svg", StringComparison.Ordinal) >= 0;
    }
}
