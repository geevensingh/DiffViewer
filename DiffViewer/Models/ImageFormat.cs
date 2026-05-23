namespace DiffViewer.Models;

/// <summary>
/// Recognised image container format for a binary blob, as identified by
/// <see cref="DiffViewer.Utility.ImageFormatDetector"/>. Drives image-diff
/// dispatch in <see cref="DiffViewer.ViewModels.DiffPaneViewModel"/> and
/// the animated-GIF hint in the image-diff header strip.
///
/// <para><b>SVG</b> is the one entry that isn't a binary container — it
/// is XML, and never trips <see cref="DiffViewer.Utility.BinaryDetector"/>.
/// The dispatch logic in <see cref="DiffViewer.ViewModels.DiffPaneViewModel"/>
/// special-cases it so the user can flip between an XML text diff and a
/// SharpVectors-rasterised image diff via the <c>Rendered</c> toolbar
/// toggle (issue #15).</para>
/// </summary>
public enum ImageFormat
{
    NotAnImage,
    Png,
    Jpeg,
    Gif,
    Bmp,
    Ico,
    Svg,
}
