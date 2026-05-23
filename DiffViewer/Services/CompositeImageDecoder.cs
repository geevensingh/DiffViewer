using DiffViewer.Models;
using DiffViewer.Utility;

namespace DiffViewer.Services;

/// <summary>
/// <see cref="IImageDecoder"/> that routes by detected image format:
/// SVG bytes go to <see cref="SharpVectorsSvgDecoder"/>, every other
/// format goes to the raster decoder
/// (<see cref="WpfImageDecoder"/> in production).
///
/// <para>Exists so <see cref="DiffViewer.ViewModels.DiffPaneViewModel"/>
/// can stay agnostic to "is this a raster blob or a vector blob"
/// — the same <see cref="IImageDecoder"/> seam handles both.
/// Constructor takes both inner decoders by interface so the tests
/// can swap in fakes per branch without spinning up the real
/// SharpVectors / WPF stacks.</para>
/// </summary>
public sealed class CompositeImageDecoder : IImageDecoder
{
    private readonly IImageDecoder _rasterDecoder;
    private readonly IImageDecoder _svgDecoder;

    public CompositeImageDecoder(IImageDecoder rasterDecoder, IImageDecoder svgDecoder)
    {
        ArgumentNullException.ThrowIfNull(rasterDecoder);
        ArgumentNullException.ThrowIfNull(svgDecoder);
        _rasterDecoder = rasterDecoder;
        _svgDecoder = svgDecoder;
    }

    public ImageDecodeResult Decode(byte[] bytes, string? path)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var format = ImageFormatDetector.Detect(bytes, path);
        return format == ImageFormat.Svg
            ? _svgDecoder.Decode(bytes, path)
            : _rasterDecoder.Decode(bytes, path);
    }
}
