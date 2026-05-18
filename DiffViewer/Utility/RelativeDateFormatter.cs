namespace DiffViewer.Utility;

/// <summary>
/// Git-style relative date formatting ("X seconds/minutes/hours/days/
/// weeks/months/years ago", "just now", "in the future"). Matches
/// <c>git log --pretty=relative</c>'s thresholds so users coming from
/// a Git CLI get the same mental model in DiffViewer's commit-metadata
/// header rows.
///
/// <para>Pure: takes both the timestamp and the "now" reference as
/// parameters so unit tests can pin time without freezing the clock
/// globally.</para>
/// </summary>
public static class RelativeDateFormatter
{
    /// <summary>
    /// Format the gap between <paramref name="point"/> and
    /// <paramref name="now"/> as a Git-style relative phrase.
    /// </summary>
    /// <param name="point">The timestamp being described.</param>
    /// <param name="now">The reference "now" the gap is measured against.</param>
    public static string Format(DateTimeOffset point, DateTimeOffset now)
    {
        var span = now - point;
        if (span.TotalSeconds < 0)
        {
            // Commits with future-dated authorship can happen (clock skew,
            // signature forgery, deliberate test fixtures). Don't render
            // "-5 seconds ago" — that's worse than honest.
            return "in the future";
        }

        var seconds = (long)span.TotalSeconds;
        if (seconds < 10) return "just now";
        if (seconds < 60) return $"{seconds} seconds ago";

        var minutes = (long)span.TotalMinutes;
        if (minutes < 2) return "1 minute ago";
        if (minutes < 60) return $"{minutes} minutes ago";

        var hours = (long)span.TotalHours;
        if (hours < 2) return "1 hour ago";
        if (hours < 24) return $"{hours} hours ago";

        var days = (long)span.TotalDays;
        if (days < 2) return "yesterday";
        if (days < 7) return $"{days} days ago";

        var weeks = days / 7;
        if (weeks < 2) return "1 week ago";
        if (days < 30) return $"{weeks} weeks ago";

        // Git's `relative` formatter switches to months at the 30-day mark
        // (approximate) and to years at 365 days. Match that.
        var months = days / 30;
        if (months < 2) return "1 month ago";
        if (days < 365) return $"{months} months ago";

        var years = days / 365;
        if (years < 2) return "1 year ago";
        return $"{years} years ago";
    }
}
