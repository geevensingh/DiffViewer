using System.Globalization;
using System.Text;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;

namespace DiffViewer.ViewModels;

/// <summary>
/// View-model for the image-diff pane (issue #9). Holds the pre-decoded
/// pair of <see cref="BitmapSource"/>s plus per-side
/// <see cref="ImageMetadata"/>, and exposes the user-controllable mode,
/// onion-skin opacity, and swipe-divider position.
///
/// <para>Constructed by <see cref="DiffPaneViewModel"/> when the
/// currently-selected file is identified as an image; both
/// <see cref="LeftImage"/> and <see cref="RightImage"/> may be null
/// for add-only / delete-only changes. At least one side must be
/// non-null in production — a VM with both sides null is structurally
/// valid for tests but produces an empty
/// <see cref="DimensionsSummary"/>.</para>
///
/// <para>The bitmaps are expected to be already
/// <see cref="System.Windows.Freezable.Freeze"/>n by the decoder so
/// they can be bound from any thread.</para>
/// </summary>
public sealed partial class ImageDiffViewModel : ObservableObject
{
    [ObservableProperty]
    private ImageDiffMode _mode = ImageDiffMode.SideBySide;

    /// <summary>
    /// Opacity of the right image when stacked over the left in
    /// <see cref="ImageDiffMode.OnionSkin"/>. <c>0.0</c> shows only the
    /// left side; <c>1.0</c> shows only the right. Always clamped to
    /// <c>[0.0, 1.0]</c>.
    /// </summary>
    [ObservableProperty]
    private double _onionOpacity = 0.5;

    /// <summary>
    /// Normalised X position of the swipe divider in
    /// <see cref="ImageDiffMode.Swipe"/>, expressed as a fraction of the
    /// composite canvas's width. <c>0.0</c> reveals only the right side;
    /// <c>1.0</c> reveals only the left. Always clamped to
    /// <c>[0.0, 1.0]</c>.
    /// </summary>
    [ObservableProperty]
    private double _swipePosition = 0.5;

    public BitmapSource? LeftImage { get; }
    public BitmapSource? RightImage { get; }
    public ImageMetadata? LeftMetadata { get; }
    public ImageMetadata? RightMetadata { get; }

    public ImageDiffViewModel(
        BitmapSource? leftImage,
        BitmapSource? rightImage,
        ImageMetadata? leftMetadata,
        ImageMetadata? rightMetadata)
    {
        LeftImage = leftImage;
        RightImage = rightImage;
        LeftMetadata = leftMetadata;
        RightMetadata = rightMetadata;
    }

    partial void OnOnionOpacityChanged(double value)
    {
        var clamped = Math.Clamp(value, 0.0, 1.0);
        if (Math.Abs(clamped - value) > double.Epsilon)
        {
            // Setting the property again is re-entrant and lands here
            // with the already-clamped value, which is a no-op.
            OnionOpacity = clamped;
        }
    }

    partial void OnSwipePositionChanged(double value)
    {
        var clamped = Math.Clamp(value, 0.0, 1.0);
        if (Math.Abs(clamped - value) > double.Epsilon)
        {
            SwipePosition = clamped;
        }
    }

    /// <summary>
    /// One-line header summary for the image-diff pane: dimensions and
    /// byte size of each side, joined by an arrow, with the byte delta
    /// in parens when both sides are present. Animated GIFs are flagged
    /// per-side so the user can tell which side is animated when only
    /// one is.
    ///
    /// <para>Examples:</para>
    /// <list type="bullet">
    /// <item><description>Same dims:
    /// <c>512 × 512 / 18.3 KB → 512 × 512 / 22.1 KB  (+3.8 KB)</c></description></item>
    /// <item><description>Different dims:
    /// <c>64 × 64 / 1.2 KB → 128 × 128 / 4.8 KB  (+3.6 KB)</c></description></item>
    /// <item><description>Identical bytes:
    /// <c>512 × 512 / 18.3 KB → 512 × 512 / 18.3 KB  (no change)</c></description></item>
    /// <item><description>Added file:
    /// <c>(added) → 128 × 128 / 4.8 KB</c></description></item>
    /// <item><description>Deleted file:
    /// <c>512 × 512 / 18.3 KB → (deleted)</c></description></item>
    /// <item><description>Animated GIF (left):
    /// <c>64 × 64 / 1.2 KB (animated, first frame) → 128 × 128 / 4.8 KB  (+3.6 KB)</c></description></item>
    /// </list>
    /// </summary>
    public string DimensionsSummary
    {
        get
        {
            if (LeftMetadata is null && RightMetadata is null)
                return string.Empty;

            var sb = new StringBuilder();
            sb.Append(LeftMetadata is null ? "(added)" : FormatSide(LeftMetadata));
            sb.Append(" → ");
            sb.Append(RightMetadata is null ? "(deleted)" : FormatSide(RightMetadata));

            if (LeftMetadata is not null && RightMetadata is not null)
            {
                long delta = RightMetadata.ByteSize - LeftMetadata.ByteSize;
                sb.Append("  ");
                sb.Append(FormatDelta(delta));
            }

            return sb.ToString();
        }
    }

    private static string FormatSide(ImageMetadata m)
    {
        // "W × H / SIZE" with an "(animated, first frame)" suffix for
        // multi-frame GIFs. The full phrase ("first frame") is verbatim
        // from the issue-#9 design decision so users are clear that
        // only frame 0 is being compared.
        var sb = new StringBuilder();
        sb.Append(m.Width.ToString(CultureInfo.InvariantCulture));
        sb.Append(" × ");
        sb.Append(m.Height.ToString(CultureInfo.InvariantCulture));
        sb.Append(" / ");
        sb.Append(FormatBytes(m.ByteSize));
        if (m.Format == ImageFormat.Gif && m.FrameCount > 1)
        {
            sb.Append(" (animated, first frame)");
        }
        return sb.ToString();
    }

    private static string FormatDelta(long delta)
    {
        if (delta == 0) return "(no change)";
        var sign = delta > 0 ? "+" : "-";
        return $"({sign}{FormatBytes(Math.Abs(delta))})";
    }

    // Duplicated (intentionally) from DiffPaneViewModel.FormatBytes —
    // see issue #9 plan. Once a third caller needs this, lift it into
    // DiffViewer.Utility/ByteSizeFormatter.
    private static string FormatBytes(long bytes)
    {
        const long Mb = 1024L * 1024;
        const long Kb = 1024L;
        if (bytes >= Mb) return $"{bytes / (double)Mb:0.##} MB";
        if (bytes >= Kb) return $"{bytes / (double)Kb:0.##} KB";
        return $"{bytes} B";
    }
}
