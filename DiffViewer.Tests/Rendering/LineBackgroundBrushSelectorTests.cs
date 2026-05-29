using System.Windows.Media;
using DiffViewer.Models;
using DiffViewer.Rendering;
using FluentAssertions;
using Xunit;
using DiffSide = DiffViewer.Rendering.DiffSide;

namespace DiffViewer.Tests.Rendering;

public sealed class LineBackgroundBrushSelectorTests
{
    private static readonly DiffColorScheme Scheme = DiffColorScheme.Classic;

    [Fact]
    public void Pick_LeftDeleted_ReturnsStrongRemovedBrush()
    {
        // The whole point of the strong-when-no-spans rule: a Deleted
        // line on the left side has, by DiffHighlightMap contract, no
        // intra-line spans -- so the brush must be strong, not soft.
        var brush = LineBackgroundBrushSelector.Pick(DiffSide.Left, DiffLineKind.Deleted, Scheme);

        brush.Should().BeSameAs(Scheme.RemovedIntraLineBackground);
        brush.Should().NotBeSameAs(Scheme.RemovedLineBackground);
    }

    [Fact]
    public void Pick_RightInserted_ReturnsStrongAddedBrush()
    {
        var brush = LineBackgroundBrushSelector.Pick(DiffSide.Right, DiffLineKind.Inserted, Scheme);

        brush.Should().BeSameAs(Scheme.AddedIntraLineBackground);
        brush.Should().NotBeSameAs(Scheme.AddedLineBackground);
    }

    [Fact]
    public void Pick_InlineDeleted_ReturnsStrongRemovedBrush()
    {
        var brush = LineBackgroundBrushSelector.Pick(DiffSide.Inline, DiffLineKind.Deleted, Scheme);

        brush.Should().BeSameAs(Scheme.RemovedIntraLineBackground);
    }

    [Fact]
    public void Pick_InlineInserted_ReturnsStrongAddedBrush()
    {
        var brush = LineBackgroundBrushSelector.Pick(DiffSide.Inline, DiffLineKind.Inserted, Scheme);

        brush.Should().BeSameAs(Scheme.AddedIntraLineBackground);
    }

    [Theory]
    [InlineData(DiffSide.Left)]
    [InlineData(DiffSide.Right)]
    [InlineData(DiffSide.Inline)]
    public void Pick_Modified_ReturnsSoftModifiedBrush(DiffSide side)
    {
        // Modified means DiffHighlightMap also attached intra-line spans;
        // the IntraLineColorizer overlays the strong red/green spans on
        // top of the soft yellow background, so the soft brush is the
        // correct base layer.
        var brush = LineBackgroundBrushSelector.Pick(side, DiffLineKind.Modified, Scheme);

        brush.Should().BeSameAs(Scheme.ModifiedLineBackground);
    }

    [Theory]
    [InlineData(DiffSide.Left, DiffLineKind.Context)]
    [InlineData(DiffSide.Right, DiffLineKind.Context)]
    [InlineData(DiffSide.Inline, DiffLineKind.Context)]
    [InlineData(DiffSide.Left, DiffLineKind.Inserted)] // wrong side
    [InlineData(DiffSide.Right, DiffLineKind.Deleted)] // wrong side
    public void Pick_NonTintedCases_ReturnsTransparent(DiffSide side, DiffLineKind kind)
    {
        var brush = LineBackgroundBrushSelector.Pick(side, kind, Scheme);

        brush.Should().BeSameAs(Brushes.Transparent);
    }

    [Fact]
    public void Pick_NullScheme_ThrowsArgumentNullException()
    {
        var act = () => LineBackgroundBrushSelector.Pick(DiffSide.Left, DiffLineKind.Deleted, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
