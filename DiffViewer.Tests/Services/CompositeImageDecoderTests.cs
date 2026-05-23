using System.Text;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// <see cref="CompositeImageDecoder"/> dispatch routing tests. Uses
/// recording fakes so we can assert "which inner decoder ran for
/// these bytes" without touching the real WPF or SharpVectors
/// stacks. Issue #15.
/// </summary>
public class CompositeImageDecoderTests
{
    [Fact]
    public void Decode_SvgBytes_RoutesToSvgDecoder()
    {
        var svgFake = new RecordingFake("svg");
        var rasterFake = new RecordingFake("raster");
        var composite = new CompositeImageDecoder(rasterDecoder: rasterFake, svgDecoder: svgFake);

        var bytes = Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"4\" height=\"4\"/>");

        var result = composite.Decode(bytes, "icon.svg");

        svgFake.CallCount.Should().Be(1);
        rasterFake.CallCount.Should().Be(0);
        result.Error.Should().Be("svg");
    }

    [Fact]
    public void Decode_PngBytes_RoutesToRasterDecoder()
    {
        var svgFake = new RecordingFake("svg");
        var rasterFake = new RecordingFake("raster");
        var composite = new CompositeImageDecoder(rasterDecoder: rasterFake, svgDecoder: svgFake);

        // PNG magic bytes — ImageFormatDetector recognises them
        // regardless of the path hint.
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        var result = composite.Decode(bytes, "photo.png");

        rasterFake.CallCount.Should().Be(1);
        svgFake.CallCount.Should().Be(0);
        result.Error.Should().Be("raster");
    }

    [Fact]
    public void Decode_UnrecognisedBytesWithSvgExtension_RoutesToSvgDecoder()
    {
        // Extension-based dispatch: bytes that don't sniff as
        // anything in particular, but the path says .svg, should
        // hit the SVG decoder so it gets a chance to parse or
        // report its own error.
        var svgFake = new RecordingFake("svg");
        var rasterFake = new RecordingFake("raster");
        var composite = new CompositeImageDecoder(rasterDecoder: rasterFake, svgDecoder: svgFake);

        var bytes = Encoding.UTF8.GetBytes("not really an svg\n");

        composite.Decode(bytes, "broken.svg");

        svgFake.CallCount.Should().Be(1);
        rasterFake.CallCount.Should().Be(0);
    }

    [Fact]
    public void Decode_NullBytes_Throws()
    {
        var composite = new CompositeImageDecoder(
            rasterDecoder: new RecordingFake("raster"),
            svgDecoder: new RecordingFake("svg"));

        var act = () => composite.Decode(null!, path: null);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullRasterDecoder_Throws()
    {
        var act = () => new CompositeImageDecoder(
            rasterDecoder: null!,
            svgDecoder: new RecordingFake("svg"));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullSvgDecoder_Throws()
    {
        var act = () => new CompositeImageDecoder(
            rasterDecoder: new RecordingFake("raster"),
            svgDecoder: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private sealed class RecordingFake : IImageDecoder
    {
        private readonly string _tag;
        public int CallCount { get; private set; }

        public RecordingFake(string tag) => _tag = tag;

        public ImageDecodeResult Decode(byte[] bytes, string? path)
        {
            CallCount++;
            return new ImageDecodeResult(Image: null, Metadata: null, Error: _tag);
        }
    }
}
