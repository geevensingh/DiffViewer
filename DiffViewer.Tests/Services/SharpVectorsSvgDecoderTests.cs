using System.Text;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// <see cref="SharpVectorsSvgDecoder"/> end-to-end tests. Marked
/// <c>[StaFact]</c> because the WPF <c>DrawingVisual</c> +
/// <c>RenderTargetBitmap</c> path that SharpVectors uses internally
/// expects STA on some code paths; Xunit.StaFact gives us that
/// guarantee uniformly. Issue #15.
/// </summary>
public class SharpVectorsSvgDecoderTests
{
    private readonly SharpVectorsSvgDecoder _decoder = new();

    [StaFact]
    public void Decode_NullBytes_Throws()
    {
        var act = () => _decoder.Decode(null!, path: null);
        act.Should().Throw<ArgumentNullException>();
    }

    [StaFact]
    public void Decode_EmptyBytes_ReturnsError()
    {
        var result = _decoder.Decode(Array.Empty<byte>(), path: "empty.svg");

        result.Image.Should().BeNull();
        result.Metadata.Should().BeNull();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [StaFact]
    public void Decode_NonSvgBytes_ReturnsError()
    {
        // PNG signature labelled .svg — the decoder's content sniff
        // should reject before invoking SharpVectors.
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        var result = _decoder.Decode(bytes, "trick.svg");

        result.Image.Should().BeNull();
        result.Metadata.Should().BeNull();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [StaFact]
    public void Decode_MalformedXml_ReturnsError()
    {
        // Looks like SVG enough to pass the sniff, but the XML is
        // truncated so SharpVectors' reader throws.
        var bytes = Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" +
            "<rect width=\"16\" height=\"16\" fill=\"#abc");

        var result = _decoder.Decode(bytes, "broken.svg");

        result.Image.Should().BeNull();
        result.Metadata.Should().BeNull();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [StaFact]
    public void Decode_MinimalValidSvg_ReturnsFrozenBitmap()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" +
            "<rect width=\"16\" height=\"16\" fill=\"red\"/>" +
            "</svg>");

        var result = _decoder.Decode(bytes, "icon.svg");

        result.Image.Should().NotBeNull();
        result.Image!.IsFrozen.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Format.Should().Be(ImageFormat.Svg);
        result.Metadata.FrameCount.Should().Be(1);
        result.Metadata.ByteSize.Should().Be(bytes.LongLength);
    }

    [StaFact]
    public void Decode_SmallSvg_UpscalesToTargetMaxDimension()
    {
        // A 16x16 SVG should be rasterised up to 1024 px on the
        // longer axis (aspect preserved) so the diff pane has
        // headroom to down-sample crisply.
        var bytes = Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\">" +
            "<circle cx=\"8\" cy=\"8\" r=\"6\" fill=\"blue\"/></svg>");

        var result = _decoder.Decode(bytes, "icon.svg");

        result.Image.Should().NotBeNull();
        result.Metadata.Should().NotBeNull();
        // 16x16 input → 1024x1024 output (square stays square).
        result.Metadata!.Width.Should().Be(SharpVectorsSvgDecoder.TargetMaxDimension);
        result.Metadata.Height.Should().Be(SharpVectorsSvgDecoder.TargetMaxDimension);
    }

    [StaFact]
    public void Decode_NonSquareSvg_PreservesAspectRatio()
    {
        // 20x10 → longer axis should hit 1024, shorter axis half of
        // that (i.e. 512). Exact rounding is implementation detail,
        // so we just assert the ratio.
        var bytes = Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"10\">" +
            "<rect width=\"20\" height=\"10\" fill=\"green\"/></svg>");

        var result = _decoder.Decode(bytes, "wide.svg");

        result.Metadata.Should().NotBeNull();
        result.Metadata!.Width.Should().Be(SharpVectorsSvgDecoder.TargetMaxDimension);
        result.Metadata.Height.Should().Be(SharpVectorsSvgDecoder.TargetMaxDimension / 2);
    }

    [StaFact]
    public void Decode_OversizedSvg_ClampsToTargetMaxDimension()
    {
        // viewBox = 50000x50000 — the decoder must clamp the
        // natural size (NaturalDimensionCap) before scaling, so the
        // rasterised bitmap still tops out at 1024 px.
        var bytes = Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            "width=\"50000\" height=\"50000\" viewBox=\"0 0 50000 50000\">" +
            "<rect width=\"50000\" height=\"50000\" fill=\"yellow\"/></svg>");

        var result = _decoder.Decode(bytes, "huge.svg");

        result.Image.Should().NotBeNull();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Width.Should().BeLessThanOrEqualTo(SharpVectorsSvgDecoder.TargetMaxDimension);
        result.Metadata.Height.Should().BeLessThanOrEqualTo(SharpVectorsSvgDecoder.TargetMaxDimension);
    }
}
