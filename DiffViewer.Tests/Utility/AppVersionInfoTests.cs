using DiffViewer.Utility;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Utility;

public sealed class AppVersionInfoTests
{
    [Theory]
    [InlineData("1.6.0", null, "1.6.0")]
    [InlineData("1.6.0", "1.6.0.0", "1.6.0")]
    [InlineData("1.6.0-rc1", null, "1.6.0-rc1")]
    [InlineData("1.6.0-rc1+abc1234", null, "1.6.0-rc1")]
    [InlineData("1.6.0+abc1234", null, "1.6.0")]
    public void GetDisplayVersionFromValues_PrefersInformational_StripsHashSuffix(
        string informational, string? assemblyVersion, string expected)
    {
        AppVersionInfo.GetDisplayVersionFromValues(informational, assemblyVersion)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "1.6.0.0", "1.6.0.0")]
    [InlineData("", "1.6.0.0", "1.6.0.0")]
    [InlineData("   ", "1.6.0.0", "1.6.0.0")]
    public void GetDisplayVersionFromValues_FallsBackToAssemblyVersion_WhenInformationalMissing(
        string? informational, string assemblyVersion, string expected)
    {
        AppVersionInfo.GetDisplayVersionFromValues(informational, assemblyVersion)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void GetDisplayVersionFromValues_FallsBackToUnknown_WhenBothMissing(
        string? informational, string? assemblyVersion)
    {
        AppVersionInfo.GetDisplayVersionFromValues(informational, assemblyVersion)
            .Should().Be("unknown");
    }
}
