using System.Globalization;
using System.Windows;
using System.Windows.Data;
using DiffViewer.Models;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public class XamlConvertersTests
{
    [Fact]
    public void BoolToGridLength_True_DefaultsToOneStar()
    {
        var result = (GridLength)BoolToGridLengthConverter.Instance.Convert(
            true, typeof(GridLength), null!, CultureInfo.InvariantCulture);

        result.IsStar.Should().BeTrue();
        result.Value.Should().Be(1.0);
    }

    [Fact]
    public void BoolToGridLength_True_WithPixelParameter_ReturnsPixels()
    {
        // ConverterParameter is parsed using WPF's GridLengthConverter
        // (same syntax as XAML), so a bare integer string is *pixels*,
        // matching how a designer writes <ColumnDefinition Width="5"/>.
        // This is the splitter-column path: ShowMiddleDivider toggles a
        // fixed 5 px GridLength on/off, not a star length.
        var result = (GridLength)BoolToGridLengthConverter.Instance.Convert(
            true, typeof(GridLength), "5", CultureInfo.InvariantCulture);

        result.IsAbsolute.Should().BeTrue();
        result.Value.Should().Be(5.0);
    }

    [Fact]
    public void BoolToGridLength_True_WithStarParameter_ReturnsStarUnits()
    {
        var result = (GridLength)BoolToGridLengthConverter.Instance.Convert(
            true, typeof(GridLength), "3*", CultureInfo.InvariantCulture);

        result.IsStar.Should().BeTrue();
        result.Value.Should().Be(3.0);
    }

    [Fact]
    public void BoolToGridLength_True_WithBareStarParameter_ReturnsOneStar()
    {
        var result = (GridLength)BoolToGridLengthConverter.Instance.Convert(
            true, typeof(GridLength), "*", CultureInfo.InvariantCulture);

        result.IsStar.Should().BeTrue();
        result.Value.Should().Be(1.0);
    }

    [Fact]
    public void BoolToGridLength_True_WithAutoParameter_ReturnsAuto()
    {
        var result = (GridLength)BoolToGridLengthConverter.Instance.Convert(
            true, typeof(GridLength), "Auto", CultureInfo.InvariantCulture);

        result.IsAuto.Should().BeTrue();
    }

    [Fact]
    public void BoolToGridLength_False_CollapsesToZero()
    {
        var result = (GridLength)BoolToGridLengthConverter.Instance.Convert(
            false, typeof(GridLength), "5", CultureInfo.InvariantCulture);

        result.Value.Should().Be(0);
    }

    [Fact]
    public void EnumToBool_Convert_MatchingEnum_ReturnsTrue()
    {
        var result = EnumToBoolConverter.Instance.Convert(
            DiffSideVisibility.LeftOnly,
            typeof(bool),
            DiffSideVisibility.LeftOnly,
            CultureInfo.InvariantCulture);

        result.Should().Be(true);
    }

    [Fact]
    public void EnumToBool_Convert_NonMatchingEnum_ReturnsFalse()
    {
        var result = EnumToBoolConverter.Instance.Convert(
            DiffSideVisibility.Both,
            typeof(bool),
            DiffSideVisibility.LeftOnly,
            CultureInfo.InvariantCulture);

        result.Should().Be(false);
    }

    [Fact]
    public void EnumToBool_Convert_StringParameter_ParsedAgainstSourceEnum()
    {
        var result = EnumToBoolConverter.Instance.Convert(
            DiffSideVisibility.RightOnly,
            typeof(bool),
            "RightOnly",
            CultureInfo.InvariantCulture);

        result.Should().Be(true);
    }

    [Fact]
    public void EnumToBool_ConvertBack_True_ReturnsParameter()
    {
        var result = EnumToBoolConverter.Instance.ConvertBack(
            true,
            typeof(DiffSideVisibility),
            DiffSideVisibility.RightOnly,
            CultureInfo.InvariantCulture);

        result.Should().Be(DiffSideVisibility.RightOnly);
    }

    [Fact]
    public void EnumToBool_ConvertBack_False_ReturnsBindingDoNothing()
    {
        // Defensive: even though RadioButton.OnToggle never sets IsChecked
        // to false on click, a programmatic IsChecked=false (or a
        // future ToggleButton usage) must not clobber the source enum.
        var result = EnumToBoolConverter.Instance.ConvertBack(
            false,
            typeof(DiffSideVisibility),
            DiffSideVisibility.RightOnly,
            CultureInfo.InvariantCulture);

        result.Should().Be(Binding.DoNothing);
    }

    [Fact]
    public void EnumToBool_ConvertBack_NullableEnumTargetType_StillResolvesParameter()
    {
        // RadioButton.IsChecked is bool?, and the binding hands us the
        // underlying enum's nullable wrapper as the target type.
        var result = EnumToBoolConverter.Instance.ConvertBack(
            true,
            typeof(DiffSideVisibility?),
            DiffSideVisibility.LeftOnly,
            CultureInfo.InvariantCulture);

        result.Should().Be(DiffSideVisibility.LeftOnly);
    }
}
