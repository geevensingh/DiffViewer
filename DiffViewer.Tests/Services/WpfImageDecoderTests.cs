using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// <see cref="WpfImageDecoder"/> end-to-end tests. All marked
/// <c>[StaFact]</c> because <see cref="BitmapDecoder.Create(System.IO.Stream, BitmapCreateOptions, BitmapCacheOption)"/>
/// and the <c>*BitmapEncoder</c> family require STA (provided by the
/// Xunit.StaFact NuGet package on the test side only).
/// </summary>
public class WpfImageDecoderTests
{
    private readonly WpfImageDecoder _decoder = new();

    [StaFact]
    public void Decode_NullBytes_Throws()
    {
        var act = () => _decoder.Decode(null!, path: null);
        act.Should().Throw<ArgumentNullException>();
    }

    [StaFact]
    public void Decode_EmptyBytes_ReturnsError()
    {
        var result = _decoder.Decode(Array.Empty<byte>(), path: null);

        result.Image.Should().BeNull();
        result.Metadata.Should().BeNull();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [StaFact]
    public void Decode_NonImageBytes_ReturnsError()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("hello world, not an image");

        var result = _decoder.Decode(bytes, path: null);

        result.Image.Should().BeNull();
        result.Metadata.Should().BeNull();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [StaFact]
    public void Decode_CorruptPngBytes_ReturnsError()
    {
        var bytes = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF,
        };

        var result = _decoder.Decode(bytes, "test.png");

        result.Image.Should().BeNull();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [StaFact]
    public void Decode_RoundtrippedPng_ReturnsFrozenBitmap()
    {
        var bytes = EncodeSolidPng(width: 4, height: 3);

        var result = _decoder.Decode(bytes, path: null);

        result.Image.Should().NotBeNull();
        result.Image!.IsFrozen.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Width.Should().Be(4);
        result.Metadata.Height.Should().Be(3);
        result.Metadata.Format.Should().Be(ImageFormat.Png);
        result.Metadata.FrameCount.Should().Be(1);
        result.Metadata.ByteSize.Should().Be(bytes.LongLength);
    }

    [StaFact]
    public void Decode_RoundtrippedJpeg_ReturnsJpegFormat()
    {
        var bytes = EncodeSolidJpeg(width: 8, height: 5);

        var result = _decoder.Decode(bytes, path: null);

        result.Image.Should().NotBeNull();
        result.Error.Should().BeNull();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Format.Should().Be(ImageFormat.Jpeg);
        result.Metadata.Width.Should().Be(8);
        result.Metadata.Height.Should().Be(5);
        result.Metadata.FrameCount.Should().Be(1);
    }

    [StaFact]
    public void Decode_RoundtrippedBmp_ReturnsBmpFormat()
    {
        var bytes = EncodeSolidBmp(width: 2, height: 2);

        var result = _decoder.Decode(bytes, path: null);

        result.Image.Should().NotBeNull();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Format.Should().Be(ImageFormat.Bmp);
        result.Metadata.Width.Should().Be(2);
        result.Metadata.Height.Should().Be(2);
    }

    [StaFact]
    public void Decode_MultiFrameGif_ReportsFrameCount()
    {
        var bytes = EncodeMultiFrameGif(width: 2, height: 2, frames: 3);

        var result = _decoder.Decode(bytes, path: null);

        result.Image.Should().NotBeNull();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Format.Should().Be(ImageFormat.Gif);
        result.Metadata.FrameCount.Should().Be(3);
    }

    [StaFact]
    public void Decode_SingleFrameGif_ReportsFrameCountOne()
    {
        var bytes = EncodeMultiFrameGif(width: 2, height: 2, frames: 1);

        var result = _decoder.Decode(bytes, path: null);

        result.Image.Should().NotBeNull();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Format.Should().Be(ImageFormat.Gif);
        result.Metadata.FrameCount.Should().Be(1);
    }

    [StaFact]
    public void Decode_SingleFrameIco_RoundtripsAsIco()
    {
        var bytes = EncodeIco((32, 32, Colors.Purple));

        var result = _decoder.Decode(bytes, "favicon.ico");

        result.Image.Should().NotBeNull();
        result.Error.Should().BeNull();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Format.Should().Be(ImageFormat.Ico);
        result.Metadata.Width.Should().Be(32);
        result.Metadata.Height.Should().Be(32);
        result.Metadata.FrameCount.Should().Be(1);
    }

    [StaFact]
    public void Decode_MultiResolutionIco_PicksLargestFrameByArea()
    {
        // Three frames at 16x16, 48x48, 32x32 (deliberately out of size
        // order to verify the decoder picks by pixel area, not by frame
        // index). The rendered bitmap should be the 48x48 one;
        // FrameCount reports the total embedded count.
        var bytes = EncodeIco(
            (16, 16, Colors.Red),
            (48, 48, Colors.Green),
            (32, 32, Colors.Blue));

        var result = _decoder.Decode(bytes, "favicon.ico");

        result.Image.Should().NotBeNull();
        result.Error.Should().BeNull();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Format.Should().Be(ImageFormat.Ico);
        result.Metadata.Width.Should().Be(48);
        result.Metadata.Height.Should().Be(48);
        result.Metadata.FrameCount.Should().Be(3);
    }

    private static byte[] EncodeSolidPng(int width, int height)
    {
        var frame = MakeBgra32Frame(width, height, Colors.Red);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static byte[] EncodeSolidJpeg(int width, int height)
    {
        var frame = MakeBgra32Frame(width, height, Colors.Blue);
        var encoder = new JpegBitmapEncoder { QualityLevel = 75 };
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static byte[] EncodeSolidBmp(int width, int height)
    {
        var frame = MakeBgra32Frame(width, height, Colors.Green);
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static byte[] EncodeMultiFrameGif(int width, int height, int frames)
    {
        var encoder = new GifBitmapEncoder();
        var palette = new[] { Colors.Red, Colors.Green, Colors.Blue, Colors.Yellow };
        for (var i = 0; i < frames; i++)
        {
            var frame = MakeBgra32Frame(width, height, palette[i % palette.Length]);
            encoder.Frames.Add(BitmapFrame.Create(frame));
        }
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    // Build a minimal ICO containing the given frames, each embedded as
    // a PNG payload (Vista+ ICO format). We synthesise the ICONDIR +
    // ICONDIRENTRY bytes by hand because WPF ships an IconBitmapDecoder
    // but no IconBitmapEncoder, so a roundtrip via WPF isn't possible.
    private static byte[] EncodeIco(params (int width, int height, Color color)[] frames)
    {
        var pngBlobs = frames
            .Select(f => EncodeSolidPng(f.width, f.height, f.color))
            .ToArray();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // ICONDIR (6 bytes)
        writer.Write((ushort)0);              // idReserved
        writer.Write((ushort)1);              // idType (1 = icon)
        writer.Write((ushort)pngBlobs.Length); // idCount

        // Each ICONDIRENTRY is 16 bytes; image data follows after all
        // entries so initial offset = 6 + 16 * N.
        var offset = 6 + 16 * pngBlobs.Length;
        for (var i = 0; i < pngBlobs.Length; i++)
        {
            var (w, h, _) = frames[i];
            writer.Write((byte)(w == 256 ? 0 : w)); // bWidth (0 means 256)
            writer.Write((byte)(h == 256 ? 0 : h)); // bHeight
            writer.Write((byte)0);                  // bColorCount (0 for >=8bpp)
            writer.Write((byte)0);                  // bReserved
            writer.Write((ushort)1);                // wPlanes
            writer.Write((ushort)32);               // wBitCount
            writer.Write((uint)pngBlobs[i].Length); // dwBytesInRes
            writer.Write((uint)offset);             // dwImageOffset
            offset += pngBlobs[i].Length;
        }

        foreach (var blob in pngBlobs)
        {
            writer.Write(blob);
        }

        return stream.ToArray();
    }

    private static byte[] EncodeSolidPng(int width, int height, Color color)
    {
        var frame = MakeBgra32Frame(width, height, color);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource MakeBgra32Frame(int width, int height, Color color)
    {
        var stride = width * 4;
        var pixels = new byte[height * stride];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 0] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = color.A;
        }
        var source = BitmapSource.Create(
            width, height,
            dpiX: 96, dpiY: 96,
            PixelFormats.Bgra32, palette: null,
            pixels, stride);
        source.Freeze();
        return source;
    }
}
