using System.Windows;
using System.Windows.Media;
using DiffViewer.Rendering;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Rendering;

public class LineBackgroundRectCoalescerTests
{
    private static readonly Brush BrushA = new SolidColorBrush(Color.FromRgb(255, 0, 0));
    private static readonly Brush BrushB = new SolidColorBrush(Color.FromRgb(0, 255, 0));

    [Fact]
    public void Coalesce_EmptyInput_ReturnsEmpty()
    {
        LineBackgroundRectCoalescer
            .Coalesce(Array.Empty<(Rect, Brush)>())
            .Should().BeEmpty();
    }

    [Fact]
    public void Coalesce_SingleRect_ReturnsThatRect()
    {
        var input = new[] { (new Rect(0, 0, 100, 15), BrushA) };

        var result = LineBackgroundRectCoalescer.Coalesce(input).ToList();

        result.Should().HaveCount(1);
        result[0].Rect.Should().Be(new Rect(0, 0, 100, 15));
        result[0].Brush.Should().BeSameAs(BrushA);
    }

    [Fact]
    public void Coalesce_TwoAdjacentSameBrushSameXWidth_MergesIntoOne()
    {
        var input = new[]
        {
            (new Rect(0, 0, 100, 15), BrushA),
            (new Rect(0, 15, 100, 15), BrushA),
        };

        var result = LineBackgroundRectCoalescer.Coalesce(input).ToList();

        result.Should().HaveCount(1);
        result[0].Rect.Should().Be(new Rect(0, 0, 100, 30));
        result[0].Brush.Should().BeSameAs(BrushA);
    }

    [Fact]
    public void Coalesce_TwoAdjacentDifferentBrush_KeepsSeparate()
    {
        var input = new[]
        {
            (new Rect(0, 0, 100, 15), BrushA),
            (new Rect(0, 15, 100, 15), BrushB),
        };

        var result = LineBackgroundRectCoalescer.Coalesce(input).ToList();

        result.Should().HaveCount(2);
        result[0].Brush.Should().BeSameAs(BrushA);
        result[1].Brush.Should().BeSameAs(BrushB);
    }

    [Fact]
    public void Coalesce_TwoSameBrushWithGap_KeepsSeparate()
    {
        // 1 px gap between the two rects (Y=16 not 15).
        var input = new[]
        {
            (new Rect(0, 0, 100, 15), BrushA),
            (new Rect(0, 16, 100, 15), BrushA),
        };

        var result = LineBackgroundRectCoalescer.Coalesce(input).ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Coalesce_TwoSameBrushOverlapping_KeepsSeparate()
    {
        // Second rect starts before first rect ends — not vertically adjacent.
        var input = new[]
        {
            (new Rect(0, 0, 100, 15), BrushA),
            (new Rect(0, 10, 100, 15), BrushA),
        };

        var result = LineBackgroundRectCoalescer.Coalesce(input).ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Coalesce_TwoSameBrushDifferentX_KeepsSeparate()
    {
        var input = new[]
        {
            (new Rect(0, 0, 100, 15), BrushA),
            (new Rect(5, 15, 100, 15), BrushA),
        };

        var result = LineBackgroundRectCoalescer.Coalesce(input).ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Coalesce_TwoSameBrushDifferentWidth_KeepsSeparate()
    {
        var input = new[]
        {
            (new Rect(0, 0, 100, 15), BrushA),
            (new Rect(0, 15, 120, 15), BrushA),
        };

        var result = LineBackgroundRectCoalescer.Coalesce(input).ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Coalesce_ThreeAdjacentSameBrush_MergesAllIntoOne()
    {
        var input = new[]
        {
            (new Rect(0, 0, 100, 15), BrushA),
            (new Rect(0, 15, 100, 15), BrushA),
            (new Rect(0, 30, 100, 15), BrushA),
        };

        var result = LineBackgroundRectCoalescer.Coalesce(input).ToList();

        result.Should().HaveCount(1);
        result[0].Rect.Should().Be(new Rect(0, 0, 100, 45));
    }

    [Fact]
    public void Coalesce_AdjacentABA_KeepsThreeSeparate()
    {
        var input = new[]
        {
            (new Rect(0, 0, 100, 15), BrushA),
            (new Rect(0, 15, 100, 15), BrushB),
            (new Rect(0, 30, 100, 15), BrushA),
        };

        var result = LineBackgroundRectCoalescer.Coalesce(input).ToList();

        result.Should().HaveCount(3);
        result[0].Brush.Should().BeSameAs(BrushA);
        result[1].Brush.Should().BeSameAs(BrushB);
        result[2].Brush.Should().BeSameAs(BrushA);
    }

    [Fact]
    public void Coalesce_AAABBA_MergesRunsIntoThreeBlocks()
    {
        var input = new[]
        {
            (new Rect(0, 0, 100, 15), BrushA),
            (new Rect(0, 15, 100, 15), BrushA),
            (new Rect(0, 30, 100, 15), BrushA),
            (new Rect(0, 45, 100, 15), BrushB),
            (new Rect(0, 60, 100, 15), BrushB),
            (new Rect(0, 75, 100, 15), BrushA),
        };

        var result = LineBackgroundRectCoalescer.Coalesce(input).ToList();

        result.Should().HaveCount(3);
        result[0].Rect.Should().Be(new Rect(0, 0, 100, 45));
        result[0].Brush.Should().BeSameAs(BrushA);
        result[1].Rect.Should().Be(new Rect(0, 45, 100, 30));
        result[1].Brush.Should().BeSameAs(BrushB);
        result[2].Rect.Should().Be(new Rect(0, 75, 100, 15));
        result[2].Brush.Should().BeSameAs(BrushA);
    }

    [Fact]
    public void Coalesce_SubPixelOffsetWithinEpsilon_StillMerges()
    {
        // Bottom of first = 15, top of second = 15.2 — within 0.5px tolerance.
        var input = new[]
        {
            (new Rect(0, 0, 100, 15), BrushA),
            (new Rect(0.1, 15.2, 100.05, 14.9), BrushA),
        };

        var result = LineBackgroundRectCoalescer.Coalesce(input).ToList();

        result.Should().HaveCount(1);
        result[0].Brush.Should().BeSameAs(BrushA);
        // Merged rect should run from y=0 to second rect's bottom (~30.1).
        result[0].Rect.Y.Should().Be(0);
        result[0].Rect.Bottom.Should().BeApproximately(30.1, 0.001);
    }

    [Fact]
    public void Coalesce_NullInput_Throws()
    {
        Action act = () => LineBackgroundRectCoalescer.Coalesce(null!).ToList();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Coalesce_PreservesOrderWhenNothingMerges()
    {
        var input = new[]
        {
            (new Rect(0, 0, 100, 15), BrushA),
            (new Rect(0, 100, 100, 15), BrushB),
            (new Rect(0, 200, 100, 15), BrushA),
        };

        var result = LineBackgroundRectCoalescer.Coalesce(input).ToList();

        result.Should().HaveCount(3);
        result[0].Rect.Y.Should().Be(0);
        result[1].Rect.Y.Should().Be(100);
        result[2].Rect.Y.Should().Be(200);
    }
}
