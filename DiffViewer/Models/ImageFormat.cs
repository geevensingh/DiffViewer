namespace DiffViewer.Models;

/// <summary>
/// Recognised image container format for a binary blob, as identified by
/// <see cref="DiffViewer.Utility.ImageFormatDetector"/>. Drives image-diff
/// dispatch in <see cref="DiffViewer.ViewModels.DiffPaneViewModel"/> and
/// the animated-GIF hint in the image-diff header strip.
///
/// <para>SVG is intentionally absent — it is XML and never trips the
/// binary detector, and routing it through the image pane requires a
/// new WPF SVG renderer plus a UX decision about text-vs-image dispatch.
/// Tracked separately as issue #15.</para>
/// </summary>
public enum ImageFormat
{
    NotAnImage,
    Png,
    Jpeg,
    Gif,
    Bmp,
    Ico,
}
