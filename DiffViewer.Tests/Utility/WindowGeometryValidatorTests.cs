using DiffViewer.Models;
using DiffViewer.Utility;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Utility;

public sealed class WindowGeometryValidatorTests
{
    // A "typical" single-1080p-monitor virtual screen.
    private const double VsLeft = 0;
    private const double VsTop = 0;
    private const double VsWidth = 1920;
    private const double VsHeight = 1080;

    private static WindowStateSnapshot? Resolve(WindowStateSnapshot? snapshot)
        => WindowGeometryValidator.Resolve(snapshot, VsLeft, VsTop, VsWidth, VsHeight);

    [Fact]
    public void Null_Snapshot_Returns_Null()
    {
        Resolve(null).Should().BeNull();
    }

    [Fact]
    public void Fully_In_Bounds_Returns_Same_Snapshot()
    {
        var s = new WindowStateSnapshot(100, 100, 1200, 800, IsMaximized: false);
        Resolve(s).Should().Be(s);
    }

    [Fact]
    public void Maximized_Flag_Is_Preserved()
    {
        var s = new WindowStateSnapshot(100, 100, 1200, 800, IsMaximized: true);
        Resolve(s).Should().Be(s);
    }

    [Fact]
    public void Fully_Off_Screen_Right_Returns_Null()
    {
        var s = new WindowStateSnapshot(99999, 100, 1200, 800, IsMaximized: false);
        Resolve(s).Should().BeNull();
    }

    [Fact]
    public void Fully_Off_Screen_Left_Returns_Null()
    {
        var s = new WindowStateSnapshot(-5000, 100, 1200, 800, IsMaximized: false);
        Resolve(s).Should().BeNull();
    }

    [Fact]
    public void Title_Bar_Entirely_Above_Virtual_Screen_Returns_Null()
    {
        // Window top is far above the virtual screen top; even though the
        // body extends down into visible territory, the user cannot grab
        // the title bar.
        var s = new WindowStateSnapshot(100, -200, 1200, 800, IsMaximized: false);
        Resolve(s).Should().BeNull();
    }

    [Fact]
    public void Title_Bar_Partially_Above_But_Still_Reachable_Is_Accepted()
    {
        // Top -10 → title strip spans y=-10..20, which overlaps the
        // virtual screen's y>=0 region. Reachable; accept.
        var s = new WindowStateSnapshot(100, -10, 1200, 800, IsMaximized: false);
        Resolve(s).Should().Be(s);
    }

    [Fact]
    public void Overlap_Width_Below_Threshold_Returns_Null()
    {
        // Only the rightmost ~50 DIPs of the window are inside the
        // virtual screen on the left edge.
        var s = new WindowStateSnapshot(-1150, 100, 1200, 800, IsMaximized: false);
        Resolve(s).Should().BeNull();
    }

    [Fact]
    public void Overlap_Height_Below_Threshold_Returns_Null()
    {
        // Only the bottom ~50 DIPs of the window are inside the virtual
        // screen on the top edge.
        var s = new WindowStateSnapshot(100, -750, 1200, 800, IsMaximized: false);
        Resolve(s).Should().BeNull();
    }

    [Fact]
    public void Too_Small_Width_Returns_Null()
    {
        var s = new WindowStateSnapshot(100, 100, 50, 800, IsMaximized: false);
        Resolve(s).Should().BeNull();
    }

    [Fact]
    public void Too_Small_Height_Returns_Null()
    {
        var s = new WindowStateSnapshot(100, 100, 1200, 50, IsMaximized: false);
        Resolve(s).Should().BeNull();
    }

    [Fact]
    public void NaN_Values_Return_Null()
    {
        Resolve(new WindowStateSnapshot(double.NaN, 100, 1200, 800, false)).Should().BeNull();
        Resolve(new WindowStateSnapshot(100, double.NaN, 1200, 800, false)).Should().BeNull();
        Resolve(new WindowStateSnapshot(100, 100, double.NaN, 800, false)).Should().BeNull();
        Resolve(new WindowStateSnapshot(100, 100, 1200, double.NaN, false)).Should().BeNull();
    }

    [Fact]
    public void Infinity_Values_Return_Null()
    {
        Resolve(new WindowStateSnapshot(double.PositiveInfinity, 100, 1200, 800, false)).Should().BeNull();
        Resolve(new WindowStateSnapshot(100, double.NegativeInfinity, 1200, 800, false)).Should().BeNull();
    }

    [Fact]
    public void Zero_Virtual_Screen_Returns_Null()
    {
        // E.g. SystemParameters never reported a valid monitor.
        var s = new WindowStateSnapshot(100, 100, 1200, 800, false);
        WindowGeometryValidator.Resolve(s, 0, 0, 0, 0).Should().BeNull();
    }

    [Fact]
    public void Multi_Monitor_Secondary_To_The_Left_Is_Accepted()
    {
        // Two 1920×1080 monitors: secondary at (-1920, 0), primary at (0, 0).
        // Window saved on the secondary monitor.
        var s = new WindowStateSnapshot(-1800, 100, 1200, 800, IsMaximized: false);
        WindowGeometryValidator.Resolve(s, -1920, 0, 3840, 1080).Should().Be(s);
    }

    [Fact]
    public void Multi_Monitor_Secondary_Unplugged_Falls_Back_To_Null()
    {
        // Same saved snapshot as above, but the secondary monitor is now
        // gone; only the primary (0,0,1920,1080) remains.
        var s = new WindowStateSnapshot(-1800, 100, 1200, 800, IsMaximized: false);
        Resolve(s).Should().BeNull();
    }

    [Fact]
    public void Exact_Minimum_Overlap_Is_Accepted()
    {
        // Left edge of window flush with right edge minus exactly the
        // 100×100 visible-threshold. Window spans x = 1820..3020,
        // virtual screen ends at x = 1920 → 100 DIPs of horizontal
        // overlap. Title bar (y = 100..130) is fully inside.
        var s = new WindowStateSnapshot(1820, 100, 1200, 800, IsMaximized: false);
        Resolve(s).Should().Be(s);
    }
}
