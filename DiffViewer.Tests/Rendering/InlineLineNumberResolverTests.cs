using DiffViewer.Rendering;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Rendering;

public class InlineLineNumberResolverTests
{
    private static IReadOnlyList<(int? OldLine, int? NewLine)> Map(
        params (int? OldLine, int? NewLine)[] rows) => rows;

    [Fact]
    public void Resolve_NullSourceLines_Throws()
    {
        Action act = () => InlineLineNumberResolver.Resolve(null!, 1);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Resolve_DocLineBelowOne_ReturnsNull()
    {
        var sourceLines = Map((1, 1));
        InlineLineNumberResolver.Resolve(sourceLines, 0).Should().BeNull();
        InlineLineNumberResolver.Resolve(sourceLines, -5).Should().BeNull();
    }

    [Fact]
    public void Resolve_DocLineBeyondEnd_ReturnsNull()
    {
        var sourceLines = Map((1, 1), (2, 2));
        InlineLineNumberResolver.Resolve(sourceLines, 3).Should().BeNull();
        InlineLineNumberResolver.Resolve(sourceLines, 999).Should().BeNull();
    }

    [Fact]
    public void Resolve_EmptyMap_ReturnsNullForAnyRow()
    {
        var sourceLines = Map();
        InlineLineNumberResolver.Resolve(sourceLines, 1).Should().BeNull();
    }

    [Fact]
    public void Resolve_BothNonNull_PrefersNewLine()
    {
        // Context row: (oldLine=9, newLine=10). Show 10 — the new file's
        // position is what readers expect when they're looking at "the
        // current state" of the file.
        var sourceLines = Map((9, 10));
        InlineLineNumberResolver.Resolve(sourceLines, 1).Should().Be(10);
    }

    [Fact]
    public void Resolve_NewLineNullOldLinePresent_FallsBackToOldLine()
    {
        // Deletion row: no new-side mapping. Show the old-file line so
        // the user can locate the removed line in the pre-image.
        var sourceLines = Map((42, null));
        InlineLineNumberResolver.Resolve(sourceLines, 1).Should().Be(42);
    }

    [Fact]
    public void Resolve_NewLinePresentOldLineNull_ReturnsNewLine()
    {
        // Insertion row: no old-side mapping. Show the new-file line.
        var sourceLines = Map(((int?)null, (int?)17));
        InlineLineNumberResolver.Resolve(sourceLines, 1).Should().Be(17);
    }

    [Fact]
    public void Resolve_BothNull_ReturnsNull()
    {
        var sourceLines = Map(((int?)null, (int?)null));
        InlineLineNumberResolver.Resolve(sourceLines, 1).Should().BeNull();
    }

    [Fact]
    public void Resolve_MultipleRows_IndexesByDocLineMinusOne()
    {
        var sourceLines = Map(
            (1, 1),
            (2, null),
            ((int?)null, 2),
            (3, 3));

        InlineLineNumberResolver.Resolve(sourceLines, 1).Should().Be(1);
        InlineLineNumberResolver.Resolve(sourceLines, 2).Should().Be(2);
        InlineLineNumberResolver.Resolve(sourceLines, 3).Should().Be(2);
        InlineLineNumberResolver.Resolve(sourceLines, 4).Should().Be(3);
    }

    [Fact]
    public void MaxDisplayedValue_NullInput_Throws()
    {
        Action act = () => InlineLineNumberResolver.MaxDisplayedValue(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MaxDisplayedValue_EmptyList_ReturnsZero()
    {
        InlineLineNumberResolver.MaxDisplayedValue(Map()).Should().Be(0);
    }

    [Fact]
    public void MaxDisplayedValue_TakesMaxAcrossRows()
    {
        var sourceLines = Map(
            (5, 6),
            (1, 100),
            (50, 51));

        // newLine ?? oldLine per row: 6, 100, 51 → max 100.
        InlineLineNumberResolver.MaxDisplayedValue(sourceLines).Should().Be(100);
    }

    [Fact]
    public void MaxDisplayedValue_FallsBackToOldLineWhenNewLineNull()
    {
        var sourceLines = Map(
            (10, null),
            (5, null),
            (3, null));

        InlineLineNumberResolver.MaxDisplayedValue(sourceLines).Should().Be(10);
    }

    [Fact]
    public void MaxDisplayedValue_BothNullRowsContributeZero()
    {
        var sourceLines = Map(
            (7, 8),
            ((int?)null, (int?)null),
            (5, 4));

        // newLine ?? oldLine per row: 8, 0, 4 → max 8.
        InlineLineNumberResolver.MaxDisplayedValue(sourceLines).Should().Be(8);
    }

    [Fact]
    public void MaxDisplayedValue_AllNullRows_ReturnsZero()
    {
        var sourceLines = Map(
            ((int?)null, (int?)null),
            ((int?)null, (int?)null));

        InlineLineNumberResolver.MaxDisplayedValue(sourceLines).Should().Be(0);
    }
}
