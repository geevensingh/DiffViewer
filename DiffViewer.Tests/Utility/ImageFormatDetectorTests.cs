using DiffViewer.Models;
using DiffViewer.Utility;
using FluentAssertions;
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
    public void DetectByExtension_IsCaseInsensitive(string path, ImageFormat expected)
        => ImageFormatDetector.DetectByExtension(path).Should().Be(expected);

    [Theory]
    [InlineData(".png", ImageFormat.Png)]
    [InlineData(".jpg", ImageFormat.Jpeg)]
    [InlineData(".jpeg", ImageFormat.Jpeg)]
    [InlineData(".gif", ImageFormat.Gif)]
    [InlineData(".bmp", ImageFormat.Bmp)]
    public void DetectByExtension_BareExtensionDotPrefix_Resolves(string path, ImageFormat expected)
        => ImageFormatDetector.DetectByExtension(path).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-extension")]
    [InlineData("trailing.dot.")]
    [InlineData(".svg")]
    [InlineData(".webp")]
    [InlineData(".tiff")]
    public void DetectByExtension_UnsupportedOrMissing_ReturnsNotAnImage(string? path)
        => ImageFormatDetector.DetectByExtension(path).Should().Be(ImageFormat.NotAnImage);

    [Fact]
    public void DetectByExtension_PathWithDirectories_ResolvesByTrailingExtension()
        => ImageFormatDetector.DetectByExtension(@"docs\screenshots\login.png")
            .Should().Be(ImageFormat.Png);
}
