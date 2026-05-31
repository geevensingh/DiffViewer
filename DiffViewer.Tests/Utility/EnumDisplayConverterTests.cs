using System.Globalization;
using System.Windows.Data;
using DiffViewer.Models;
using DiffViewer.Utility;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Utility;

public sealed class EnumDisplayConverterTests
{
    [Theory]
    [InlineData(AutoUpdateMode.Automatic, "Automatic (silent install)")]
    [InlineData(AutoUpdateMode.NotifyOnly, "Notify only (show banner)")]
    [InlineData(AutoUpdateMode.Disabled, "Disabled")]
    public void Convert_AutoUpdateMode_ProducesFriendlyString(AutoUpdateMode mode, string expected)
    {
        var result = EnumDisplayConverter.Instance.Convert(
            mode, typeof(string), parameter: null, culture: CultureInfo.InvariantCulture);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(UpdateCheckCadence.StartupOnly, "Startup only")]
    [InlineData(UpdateCheckCadence.Hourly, "Hourly")]
    [InlineData(UpdateCheckCadence.EverySixHours, "Every six hours")]
    [InlineData(UpdateCheckCadence.Daily, "Daily")]
    [InlineData(UpdateCheckCadence.Weekly, "Weekly")]
    public void Convert_UpdateCheckCadence_ProducesFriendlyString(UpdateCheckCadence cadence, string expected)
    {
        var result = EnumDisplayConverter.Instance.Convert(
            cadence, typeof(string), parameter: null, culture: CultureInfo.InvariantCulture);

        result.Should().Be(expected);
    }

    [Fact]
    public void Convert_UnknownValue_FallsBackToToString()
    {
        var result = EnumDisplayConverter.Instance.Convert(
            "some-string", typeof(string), parameter: null, culture: CultureInfo.InvariantCulture);

        result.Should().Be("some-string");
    }

    [Fact]
    public void Convert_NullValue_ReturnsEmptyString()
    {
        var result = EnumDisplayConverter.Instance.Convert(
            null, typeof(string), parameter: null, culture: CultureInfo.InvariantCulture);

        result.Should().Be(string.Empty);
    }

    [Fact]
    public void ConvertBack_ReturnsDoNothing()
    {
        // The converter only feeds the display path; the underlying
        // enum value is what SelectedItem binds to. ConvertBack must
        // not interfere.
        var result = EnumDisplayConverter.Instance.ConvertBack(
            "Daily", typeof(UpdateCheckCadence), parameter: null, culture: CultureInfo.InvariantCulture);

        result.Should().Be(Binding.DoNothing);
    }
}
