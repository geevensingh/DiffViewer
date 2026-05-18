using DiffViewer.Utility;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Utility;

/// <summary>
/// Coverage for <see cref="RelativeDateFormatter"/>. Verbose Git-style
/// phrasing, deliberately distinct from
/// <see cref="RelativeTimeFormatter"/>'s compact recents-bar format.
/// </summary>
public class RelativeDateFormatterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 14, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InFuture_ReturnsFutureMarker()
    {
        var future = Now.AddMinutes(5);
        RelativeDateFormatter.Format(future, Now).Should().Be("in the future");
    }

    [Fact]
    public void Within10Seconds_JustNow()
    {
        RelativeDateFormatter.Format(Now.AddSeconds(-5), Now).Should().Be("just now");
    }

    [Fact]
    public void SecondsAgo_BetweenTenAndSixty()
    {
        RelativeDateFormatter.Format(Now.AddSeconds(-42), Now).Should().Be("42 seconds ago");
    }

    [Fact]
    public void OneMinuteAgo_SingularForm()
    {
        RelativeDateFormatter.Format(Now.AddSeconds(-90), Now).Should().Be("1 minute ago");
    }

    [Fact]
    public void MinutesAgo_PluralForm()
    {
        RelativeDateFormatter.Format(Now.AddMinutes(-15), Now).Should().Be("15 minutes ago");
    }

    [Fact]
    public void OneHourAgo_SingularForm()
    {
        RelativeDateFormatter.Format(Now.AddMinutes(-90), Now).Should().Be("1 hour ago");
    }

    [Fact]
    public void HoursAgo_PluralForm()
    {
        RelativeDateFormatter.Format(Now.AddHours(-5), Now).Should().Be("5 hours ago");
    }

    [Fact]
    public void Yesterday_RendersAsLiteral()
    {
        RelativeDateFormatter.Format(Now.AddHours(-30), Now).Should().Be("yesterday");
    }

    [Fact]
    public void DaysAgo_BetweenTwoAndSeven()
    {
        RelativeDateFormatter.Format(Now.AddDays(-3), Now).Should().Be("3 days ago");
    }

    [Fact]
    public void OneWeekAgo_SingularForm()
    {
        RelativeDateFormatter.Format(Now.AddDays(-9), Now).Should().Be("1 week ago");
    }

    [Fact]
    public void WeeksAgo_PluralForm()
    {
        RelativeDateFormatter.Format(Now.AddDays(-20), Now).Should().Be("2 weeks ago");
    }

    [Fact]
    public void OneMonthAgo_SingularForm()
    {
        RelativeDateFormatter.Format(Now.AddDays(-45), Now).Should().Be("1 month ago");
    }

    [Fact]
    public void MonthsAgo_PluralForm()
    {
        RelativeDateFormatter.Format(Now.AddDays(-180), Now).Should().Be("6 months ago");
    }

    [Fact]
    public void OneYearAgo_SingularForm()
    {
        RelativeDateFormatter.Format(Now.AddDays(-400), Now).Should().Be("1 year ago");
    }

    [Fact]
    public void YearsAgo_PluralForm()
    {
        RelativeDateFormatter.Format(Now.AddDays(-800), Now).Should().Be("2 years ago");
    }
}
