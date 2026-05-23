using DiffViewer.Models;
using DiffViewer.Utility;
using FluentAssertions;
using System.Linq;
using Xunit;

namespace DiffViewer.Tests.Utility;

public class ImageFormatDetectorTests
{
    // Golden 8-byte PNG signature, copied from RFC 2083 §3.1 — placed
    // here as a literal so a future tweak to the detector's signature
    // constant can never silently agree with itself in tests.
    private static readonly byte[] PngSignature =
        { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] JpegSignature = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] Gif87aSignature = "GIF87a"u8.ToArray();
    private static readonly byte[] Gif89aSignature = "GIF89a"u8.ToArray();
    private static readonly byte[] BmpSignature = "BM"u8.ToArray();
    // ICO header: 00 00 (reserved) + 01 00 (type=icon, little-endian).
    private static readonly byte[] IcoSignature = { 0x00, 0x00, 0x01, 0x00 };

    [Fact]
    public void Detect_PngMagic_ReturnsPng()
        => ImageFormatDetector.Detect(PngSignature, "icon.png").Should().Be(ImageFormat.Png);

    [Fact]
    public void Detect_JpegMagic_ReturnsJpeg()
        => ImageFormatDetector.Detect(JpegSignature, "photo.jpg").Should().Be(ImageFormat.Jpeg);

    [Fact]
    public void Detect_Gif87aMagic_ReturnsGif()
        => ImageFormatDetector.Detect(Gif87aSignature, "anim.gif").Should().Be(ImageFormat.Gif);

    [Fact]
    public void Detect_Gif89aMagic_ReturnsGif()
        => ImageFormatDetector.Detect(Gif89aSignature, "anim.gif").Should().Be(ImageFormat.Gif);

    [Fact]
    public void Detect_BmpMagic_ReturnsBmp()
        => ImageFormatDetector.Detect(BmpSignature, "drawing.bmp").Should().Be(ImageFormat.Bmp);

    [Fact]
    public void Detect_IcoMagic_ReturnsIco()
        => ImageFormatDetector.Detect(IcoSignature, "favicon.ico").Should().Be(ImageFormat.Ico);

    [Fact]
    public void Detect_IcoMagicWrongType_DoesNotMatch()
    {
        // CUR files use 00 00 02 00 (type=cursor). They are out of scope
        // in v1, so a CUR-shaped header must not classify as Ico.
        var curHeader = new byte[] { 0x00, 0x00, 0x02, 0x00 };
        ImageFormatDetector.Detect(curHeader, "pointer.cur").Should().Be(ImageFormat.NotAnImage);
    }

    [Fact]
    public void Detect_MagicBytesWinOverExtension_PngLabelledAsJpg()
    {
        // A PNG file mislabelled with a .jpg extension should classify
        // as Png. Magic bytes are authoritative.
        ImageFormatDetector.Detect(PngSignature, "trick.jpg")
            .Should().Be(ImageFormat.Png);
    }

    [Fact]
    public void Detect_MagicBytesWinOverExtension_JpegLabelledAsPng()
    {
        ImageFormatDetector.Detect(JpegSignature, "trick.png")
            .Should().Be(ImageFormat.Jpeg);
    }

    [Fact]
    public void Detect_EmptyBuffer_FallsBackToExtension()
    {
        // Zero-byte adds/deletes need to still dispatch on extension.
        ImageFormatDetector.Detect(ReadOnlySpan<byte>.Empty, "deleted.png")
            .Should().Be(ImageFormat.Png);
    }

    [Fact]
    public void Detect_EmptyBufferAndNullPath_ReturnsNotAnImage()
        => ImageFormatDetector.Detect(ReadOnlySpan<byte>.Empty, null).Should().Be(ImageFormat.NotAnImage);

    [Fact]
    public void Detect_UnrecognizedBytesAndUnknownExtension_ReturnsNotAnImage()
    {
        var notAnImage = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        ImageFormatDetector.Detect(notAnImage, "file.txt").Should().Be(ImageFormat.NotAnImage);
    }

    [Fact]
    public void Detect_UnrecognizedBytesButImageExtension_FallsBackToExtension()
    {
        // A truncated / corrupt PNG should still take the image path on
        // extension alone — the decoder will reject it later if it can't
        // be parsed.
        var almostPng = new byte[] { 0x89, 0x50, 0x4E };
        ImageFormatDetector.Detect(almostPng, "broken.png").Should().Be(ImageFormat.Png);
    }

    [Theory]
    [InlineData("photo.JPG", ImageFormat.Jpeg)]
    [InlineData("photo.JPEG", ImageFormat.Jpeg)]
    [InlineData("photo.Png", ImageFormat.Png)]
    [InlineData("anim.GIF", ImageFormat.Gif)]
    [InlineData("draw.BMP", ImageFormat.Bmp)]
    [InlineData("favicon.ICO", ImageFormat.Ico)]
    public void DetectByExtension_IsCaseInsensitive(string path, ImageFormat expected)
        => ImageFormatDetector.DetectByExtension(path).Should().Be(expected);

    [Theory]
    [InlineData(".png", ImageFormat.Png)]
    [InlineData(".jpg", ImageFormat.Jpeg)]
    [InlineData(".jpeg", ImageFormat.Jpeg)]
    [InlineData(".gif", ImageFormat.Gif)]
    [InlineData(".bmp", ImageFormat.Bmp)]
    [InlineData(".ico", ImageFormat.Ico)]
    [InlineData(".svg", ImageFormat.Svg)]
    public void DetectByExtension_BareExtensionDotPrefix_Resolves(string path, ImageFormat expected)
        => ImageFormatDetector.DetectByExtension(path).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-extension")]
    [InlineData("trailing.dot.")]
    [InlineData(".webp")]
    [InlineData(".tiff")]
    [InlineData(".cur")]
    public void DetectByExtension_UnsupportedOrMissing_ReturnsNotAnImage(string? path)
        => ImageFormatDetector.DetectByExtension(path).Should().Be(ImageFormat.NotAnImage);

    [Theory]
    [InlineData("icon.svg")]
    [InlineData("LOGO.SVG")]
    [InlineData("nested\\path\\to\\drawing.svg")]
    public void DetectByExtension_Svg_ResolvesByExtension(string path)
        => ImageFormatDetector.DetectByExtension(path).Should().Be(ImageFormat.Svg);

    [Fact]
    public void Detect_SvgWithXmlProlog_ReturnsSvg()
    {
        // Real-world SVG: leading XML declaration + namespace.
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"/>\n");
        ImageFormatDetector.Detect(bytes, "icon.svg").Should().Be(ImageFormat.Svg);
    }

    [Fact]
    public void Detect_SvgNoExtension_ContentSniffMatches()
    {
        // No path hint - the content sniff must still catch it.
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        ImageFormatDetector.Detect(bytes, null).Should().Be(ImageFormat.Svg);
    }

    [Fact]
    public void Detect_SvgWithUtf8Bom_ContentSniffMatches()
    {
        // UTF-8 BOM (EF BB BF) before the SVG content - must not defeat
        // the sniff.
        var preamble = new byte[] { 0xEF, 0xBB, 0xBF };
        var body = System.Text.Encoding.UTF8.GetBytes("<svg/>");
        var bytes = preamble.Concat(body).ToArray();
        ImageFormatDetector.Detect(bytes, null).Should().Be(ImageFormat.Svg);
    }

    [Fact]
    public void Detect_SvgWithLeadingWhitespace_ContentSniffMatches()
    {
        // Tab + newline + whitespace before the opening tag.
        var bytes = System.Text.Encoding.UTF8.GetBytes("  \t\n<svg/>");
        ImageFormatDetector.Detect(bytes, null).Should().Be(ImageFormat.Svg);
    }

    [Fact]
    public void Detect_BinaryBytesWithSvgExtension_ContentBeatsExtension()
    {
        // A PNG-shaped blob labelled .svg should classify as Png. Magic
        // bytes are authoritative — same rule as the other formats.
        ImageFormatDetector.Detect(PngSignature, "trick.svg")
            .Should().Be(ImageFormat.Png);
    }

    [Fact]
    public void Detect_PlainTextWithSvgExtension_FallsBackToExtension()
    {
        // The file says .svg but the first 1 KiB has no <svg or SVG-NS
        // marker. Extension-based dispatch still classifies it as Svg
        // so the decoder gets a chance to reject it cleanly.
        var bytes = System.Text.Encoding.UTF8.GetBytes("not really an svg\n");
        ImageFormatDetector.Detect(bytes, "broken.svg").Should().Be(ImageFormat.Svg);
    }

    [Fact]
    public void DetectByExtension_PathWithDirectories_ResolvesByTrailingExtension()
        => ImageFormatDetector.DetectByExtension(@"docs\screenshots\login.png")
            .Should().Be(ImageFormat.Png);
}
