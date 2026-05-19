using System.Windows.Media.Imaging;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Decodes a raw image blob into a renderable
/// <see cref="BitmapSource"/> + companion <see cref="ImageMetadata"/>.
/// The seam exists so <see cref="DiffViewer.ViewModels.DiffPaneViewModel"/>
/// can be unit-tested without spinning up a WPF dispatcher — tests
/// substitute a fake decoder that returns canned results.
///
/// <para>Implementations must <see cref="System.Windows.Freezable.Freeze"/>
/// the returned <see cref="BitmapSource"/> so callers can hand it to the
/// UI thread without further marshalling. A decode that fails for any
/// reason (corrupt bytes, unsupported format, OOM) must return
/// <see cref="ImageDecodeResult"/> with <c>Image = null</c> and a
/// non-null <c>Error</c>; throwing from <see cref="Decode"/> is a
/// contract violation.</para>
/// </summary>
public interface IImageDecoder
{
    ImageDecodeResult Decode(byte[] bytes, string? path);
}

/// <summary>
/// Outcome of a single <see cref="IImageDecoder.Decode"/> call.
/// </summary>
/// <param name="Image">Frozen rendered first-frame bitmap, or
/// <c>null</c> if decoding failed.</param>
/// <param name="Metadata">Companion metadata when <see cref="Image"/>
/// is non-null; <c>null</c> otherwise.</param>
/// <param name="Error">Short human-readable explanation when
/// <see cref="Image"/> is <c>null</c>; <c>null</c> on success. Not
/// surfaced to end users today — <see cref="DiffViewer.ViewModels.DiffPaneViewModel"/>
/// falls back to the binary placeholder — but kept for diagnostics.</param>
public sealed record ImageDecodeResult(
    BitmapSource? Image,
    ImageMetadata? Metadata,
    string? Error);
