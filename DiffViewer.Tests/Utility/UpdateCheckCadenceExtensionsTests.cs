using System;
using DiffViewer.Models;
using DiffViewer.Utility;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Utility;

public sealed class UpdateCheckCadenceExtensionsTests
{
    [Fact]
    public void ToInterval_StartupOnly_IsNull()
    {
        UpdateCheckCadence.StartupOnly.ToInterval().Should().BeNull();
    }

    [Theory]
    [InlineData(UpdateCheckCadence.Hourly, 1)]
    [InlineData(UpdateCheckCadence.EverySixHours, 6)]
    [InlineData(UpdateCheckCadence.Daily, 24)]
    public void ToInterval_HourlyCadences_MapToHours(UpdateCheckCadence cadence, int expectedHours)
    {
        cadence.ToInterval().Should().Be(TimeSpan.FromHours(expectedHours));
    }

    [Fact]
    public void ToInterval_Weekly_IsSevenDays()
    {
        UpdateCheckCadence.Weekly.ToInterval().Should().Be(TimeSpan.FromDays(7));
    }
}
