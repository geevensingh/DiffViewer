using DiffViewer.Utility;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Utility;

public sealed class FileListLayoutTests
{
    [Fact]
    public void ClampWidth_InRange_PassesThroughUnchanged()
    {
        FileListLayout.ClampWidth(320.0).Should().Be(320.0);
        FileListLayout.ClampWidth(500.0).Should().Be(500.0);
        FileListLayout.ClampWidth(
            FileListLayout.MinFileListPaneWidthPixels).Should().Be(
            FileListLayout.MinFileListPaneWidthPixels);
        FileListLayout.ClampWidth(
            FileListLayout.MaxFileListPaneWidthPixels).Should().Be(
            FileListLayout.MaxFileListPaneWidthPixels);
    }

    [Fact]
    public void ClampWidth_BelowMin_ReturnsMin()
    {
        FileListLayout.ClampWidth(
            FileListLayout.MinFileListPaneWidthPixels - 50)
            .Should().Be(FileListLayout.MinFileListPaneWidthPixels);
    }

    [Fact]
    public void ClampWidth_AboveMax_ReturnsMax()
    {
        FileListLayout.ClampWidth(
            FileListLayout.MaxFileListPaneWidthPixels + 1000)
            .Should().Be(FileListLayout.MaxFileListPaneWidthPixels);
    }

    [Fact]
    public void ClampWidth_NaN_ReturnsDefault()
    {
        FileListLayout.ClampWidth(double.NaN)
            .Should().Be(FileListLayout.DefaultFileListPaneWidthPixels);
    }

    [Fact]
    public void ClampWidth_PositiveInfinity_ReturnsDefault()
    {
        FileListLayout.ClampWidth(double.PositiveInfinity)
            .Should().Be(FileListLayout.DefaultFileListPaneWidthPixels);
    }

    [Fact]
    public void ClampWidth_NegativeInfinity_ReturnsDefault()
    {
        FileListLayout.ClampWidth(double.NegativeInfinity)
            .Should().Be(FileListLayout.DefaultFileListPaneWidthPixels);
    }

    [Fact]
    public void ClampWidth_Zero_ReturnsDefault()
    {
        // Zero is degenerate (and likely a sign of a corrupted settings
        // file or an unloaded ColumnDefinition reporting its initial
        // value). Falling back to default is safer than clamping to
        // MinFileListPaneWidthPixels because the latter would silently
        // present a different value to the user than they ever chose.
        FileListLayout.ClampWidth(0)
            .Should().Be(FileListLayout.DefaultFileListPaneWidthPixels);
    }

    [Fact]
    public void ClampWidth_Negative_ReturnsDefault()
    {
        FileListLayout.ClampWidth(-100)
            .Should().Be(FileListLayout.DefaultFileListPaneWidthPixels);
    }

    [Fact]
    public void Constants_HaveExpectedRelationship()
    {
        // Sanity check: the default falls inside the [min, max] band so
        // a fresh settings file deserializes to a value the clamp
        // accepts as-is.
        FileListLayout.DefaultFileListPaneWidthPixels
            .Should().BeGreaterThan(FileListLayout.MinFileListPaneWidthPixels);
        FileListLayout.DefaultFileListPaneWidthPixels
            .Should().BeLessThan(FileListLayout.MaxFileListPaneWidthPixels);
    }
}
