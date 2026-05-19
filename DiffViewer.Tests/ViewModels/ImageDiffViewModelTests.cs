using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiffViewer.Models;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;
using ImageMetadata = DiffViewer.Models.ImageMetadata;

namespace DiffViewer.Tests.ViewModels;

public class ImageDiffViewModelTests
{
    // Helpers — all metadata factories take optional frameCount so tests
    // for the animated-GIF hint stay readable.
    private static ImageMetadata Png(int w, int h, long size) =>
        new(w, h, size, ImageFormat.Png, FrameCount: 1);

    private static ImageMetadata Gif(int w, int h, long size, int frameCount = 1) =>
        new(w, h, size, ImageFormat.Gif, FrameCount: frameCount);

    private static ImageDiffViewModel Make(
        ImageMetadata? left = null, ImageMetadata? right = null)
        => new(leftImage: null, rightImage: null, leftMetadata: left, rightMetadata: right);

    [Fact]
    public void Mode_DefaultsToSideBySide()
        => Make(Png(10, 10, 100), Png(10, 10, 100)).Mode.Should().Be(ImageDiffMode.SideBySide);

    [Fact]
    public void OnionOpacity_DefaultsToHalf()
        => Make(Png(10, 10, 100), Png(10, 10, 100)).OnionOpacity.Should().Be(0.5);

    [Fact]
    public void SwipePosition_DefaultsToHalf()
        => Make(Png(10, 10, 100), Png(10, 10, 100)).SwipePosition.Should().Be(0.5);

    [Fact]
    public void Mode_Change_RaisesPropertyChanged()
    {
        var vm = Make(Png(10, 10, 100), Png(10, 10, 100));
        var raised = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Mode = ImageDiffMode.Swipe;

        raised.Should().Contain(nameof(ImageDiffViewModel.Mode));
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(-0.001, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.25, 0.25)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.001, 1.0)]
    [InlineData(2.0, 1.0)]
    public void OnionOpacity_IsClampedToUnitInterval(double set, double expected)
    {
        var vm = Make(Png(10, 10, 100), Png(10, 10, 100));
        vm.OnionOpacity = set;
        vm.OnionOpacity.Should().Be(expected);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 1.0)]
    public void SwipePosition_IsClampedToUnitInterval(double set, double expected)
    {
        var vm = Make(Png(10, 10, 100), Png(10, 10, 100));
        vm.SwipePosition = set;
        vm.SwipePosition.Should().Be(expected);
    }

    [Fact]
    public void DimensionsSummary_SameDimensions_DifferentBytes_ShowsDelta()
    {
        var vm = Make(Png(512, 512, 18_739), Png(512, 512, 22_640));
        // 18,739 B → 18.3 KB; 22,640 B → 22.11 KB; delta = +3,901 B → +3.81 KB.
        vm.DimensionsSummary.Should().Be(
            "512 × 512 / 18.3 KB → 512 × 512 / 22.11 KB  (+3.81 KB)");
    }

    [Fact]
    public void DimensionsSummary_DifferentDimensions_ShowsBothAndDelta()
    {
        var vm = Make(Png(64, 64, 1_200), Png(128, 128, 4_800));
        vm.DimensionsSummary.Should().Be(
            "64 × 64 / 1.17 KB → 128 × 128 / 4.69 KB  (+3.52 KB)");
    }

    [Fact]
    public void DimensionsSummary_IdenticalBytes_ShowsNoChange()
    {
        var vm = Make(Png(64, 64, 1_200), Png(64, 64, 1_200));
        vm.DimensionsSummary.Should().EndWith("  (no change)");
    }

    [Fact]
    public void DimensionsSummary_NegativeDelta_RendersMinusSign()
    {
        var vm = Make(Png(64, 64, 5_000), Png(64, 64, 1_000));
        vm.DimensionsSummary.Should().EndWith("  (-3.91 KB)");
    }

    [Fact]
    public void DimensionsSummary_BytesBelowKb_FormatsAsBytes()
    {
        var vm = Make(Png(64, 64, 800), Png(64, 64, 900));
        vm.DimensionsSummary.Should().Be("64 × 64 / 800 B → 64 × 64 / 900 B  (+100 B)");
    }

    [Fact]
    public void DimensionsSummary_BytesAboveMb_FormatsAsMb()
    {
        long oneMb = 1024L * 1024;
        var vm = Make(Png(8000, 8000, oneMb), Png(8000, 8000, 3 * oneMb));
        vm.DimensionsSummary.Should().Be(
            "8000 × 8000 / 1 MB → 8000 × 8000 / 3 MB  (+2 MB)");
    }

    [Fact]
    public void DimensionsSummary_AddOnly_LeftNull()
    {
        var vm = Make(left: null, right: Png(128, 128, 4_800));
        vm.DimensionsSummary.Should().Be("(added) → 128 × 128 / 4.69 KB");
    }

    [Fact]
    public void DimensionsSummary_DeleteOnly_RightNull()
    {
        var vm = Make(left: Png(128, 128, 4_800), right: null);
        vm.DimensionsSummary.Should().Be("128 × 128 / 4.69 KB → (deleted)");
    }

    [Fact]
    public void DimensionsSummary_BothNull_IsEmpty()
        => Make().DimensionsSummary.Should().BeEmpty();

    [Fact]
    public void DimensionsSummary_AnimatedGifOnLeftOnly_FlagsLeftSide()
    {
        var vm = Make(Gif(64, 64, 1_200, frameCount: 5), Gif(64, 64, 1_400, frameCount: 1));
        vm.DimensionsSummary.Should().Be(
            "64 × 64 / 1.17 KB (animated, first frame) → 64 × 64 / 1.37 KB  (+200 B)");
    }

    [Fact]
    public void DimensionsSummary_AnimatedGifOnRightOnly_FlagsRightSide()
    {
        var vm = Make(Gif(64, 64, 1_200, frameCount: 1), Gif(64, 64, 1_400, frameCount: 7));
        vm.DimensionsSummary.Should().Be(
            "64 × 64 / 1.17 KB → 64 × 64 / 1.37 KB (animated, first frame)  (+200 B)");
    }

    [Fact]
    public void DimensionsSummary_AnimatedGifOnBothSides_FlagsBoth()
    {
        var vm = Make(Gif(64, 64, 1_200, frameCount: 3), Gif(64, 64, 1_200, frameCount: 4));
        vm.DimensionsSummary.Should().Be(
            "64 × 64 / 1.17 KB (animated, first frame) → 64 × 64 / 1.17 KB (animated, first frame)  (no change)");
    }

    [Fact]
    public void DimensionsSummary_MultiFrameNonGif_DoesNotFlag()
    {
        // A PNG reports FrameCount = 1 in v1; defensively, even if a
        // future decoder reports >1 for a non-GIF format the hint
        // should not fire.
        var weird = new ImageMetadata(64, 64, 1_200, ImageFormat.Png, FrameCount: 3);
        var vm = Make(weird, Png(64, 64, 1_200));
        vm.DimensionsSummary.Should().NotContain("animated");
    }

    [Fact]
    public void LeftAndRightImage_AreExposedFromConstructor()
    {
        // Trivial pass-through test; constructed with nulls because a
        // real BitmapSource requires STA. The image setter path is
        // exercised end-to-end via WpfImageDecoderTests in Phase 3.
        var vm = Make(Png(10, 10, 100), Png(10, 10, 100));
        vm.LeftImage.Should().BeNull();
        vm.RightImage.Should().BeNull();
        vm.LeftMetadata.Should().NotBeNull();
        vm.RightMetadata.Should().NotBeNull();
    }

    // ---- HasBothImages / SingleImage gates ----
    //
    // STA needed because BitmapSource.Create touches WPF imaging
    // services. Mirrors the helper in DiffPaneViewModelTests.

    private static BitmapSource MakeFrozenBitmap()
    {
        var bmp = BitmapSource.Create(
            pixelWidth: 1,
            pixelHeight: 1,
            dpiX: 96,
            dpiY: 96,
            pixelFormat: PixelFormats.Bgra32,
            palette: null,
            pixels: new byte[] { 0, 0, 0, 255 },
            stride: 4);
        bmp.Freeze();
        return bmp;
    }

    [Fact]
    public void HasBothImages_BothNull_IsFalse()
        => new ImageDiffViewModel(null, null, null, null).HasBothImages.Should().BeFalse();

    [StaFact]
    public void HasBothImages_BothPresent_IsTrue()
    {
        var vm = new ImageDiffViewModel(MakeFrozenBitmap(), MakeFrozenBitmap(), null, null);
        vm.HasBothImages.Should().BeTrue();
    }

    [StaFact]
    public void HasBothImages_AddOnly_IsFalse()
    {
        var vm = new ImageDiffViewModel(null, MakeFrozenBitmap(), null, null);
        vm.HasBothImages.Should().BeFalse();
    }

    [StaFact]
    public void HasBothImages_DeleteOnly_IsFalse()
    {
        var vm = new ImageDiffViewModel(MakeFrozenBitmap(), null, null, null);
        vm.HasBothImages.Should().BeFalse();
    }

    [Fact]
    public void SingleImage_BothNull_IsNull()
        => new ImageDiffViewModel(null, null, null, null).SingleImage.Should().BeNull();

    [StaFact]
    public void SingleImage_AddOnly_ReturnsRight()
    {
        var right = MakeFrozenBitmap();
        var vm = new ImageDiffViewModel(null, right, null, null);
        vm.SingleImage.Should().BeSameAs(right);
    }

    [StaFact]
    public void SingleImage_DeleteOnly_ReturnsLeft()
    {
        var left = MakeFrozenBitmap();
        var vm = new ImageDiffViewModel(left, null, null, null);
        vm.SingleImage.Should().BeSameAs(left);
    }
}
