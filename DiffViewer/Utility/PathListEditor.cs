using System;
using System.Collections.Generic;

namespace DiffViewer.Utility;

/// <summary>
/// Pure helpers for adding/removing a single directory entry to/from a
/// Windows <c>PATH</c>-style, semicolon-delimited string. Used by
/// <see cref="DiffViewer.Services.UserPathRegistrar"/> to make the
/// installed app's directory discoverable on the command line.
/// </summary>
/// <remarks>
/// <para>Comparison is whole-segment (so <c>...\current</c> never matches
/// <c>...\current2</c>), case-insensitive, and tolerant of a trailing
/// directory separator. It also compares the
/// <see cref="Environment.ExpandEnvironmentVariables(string)"/> form of
/// each segment so an entry stored as <c>%LOCALAPPDATA%\App\current</c>
/// is treated as equal to its expanded absolute path — avoiding a
/// duplicate add or a missed removal. Surviving segments are preserved
/// verbatim (keeping their <c>%VAR%</c> form and original casing); only
/// our own entry is added or removed.</para>
///
/// <para><see cref="Add"/> and <see cref="Remove"/> return <c>null</c>
/// when nothing changed, letting the caller skip a redundant write and
/// environment-change broadcast. <see cref="Remove"/> returns an empty
/// string (not <c>null</c>) when it removed the only entry.</para>
/// </remarks>
public static class PathListEditor
{
    /// <summary>
    /// True when <paramref name="directory"/> is already present in
    /// <paramref name="raw"/> (whole-segment, case-insensitive, separator-
    /// and environment-variable-tolerant).
    /// </summary>
    public static bool Contains(string? raw, string directory)
    {
        if (string.IsNullOrEmpty(raw)) return false;
        var target = (directory ?? string.Empty).Trim();
        if (target.Length == 0) return false;

        var targetNormalized = Normalize(target);
        var targetExpanded = Normalize(SafeExpand(target));

        foreach (var segment in raw.Split(';'))
        {
            if (SegmentMatches(segment, targetNormalized, targetExpanded)) return true;
        }
        return false;
    }

    /// <summary>
    /// Returns <paramref name="raw"/> with <paramref name="directory"/>
    /// appended, or <c>null</c> if it was already present or the input
    /// directory is blank.
    /// </summary>
    public static string? Add(string? raw, string directory)
    {
        var target = (directory ?? string.Empty).Trim();
        if (target.Length == 0) return null;
        if (Contains(raw, target)) return null;

        var entry = Normalize(target);
        if (string.IsNullOrWhiteSpace(raw)) return entry;

        var trimmedEnd = raw.TrimEnd();
        if (trimmedEnd.Length == 0) return entry;

        return trimmedEnd.EndsWith(";", StringComparison.Ordinal)
            ? trimmedEnd + entry
            : trimmedEnd + ";" + entry;
    }

    /// <summary>
    /// Returns <paramref name="raw"/> with every segment matching
    /// <paramref name="directory"/> removed, or <c>null</c> if no segment
    /// matched. Returns an empty string when the matched segment was the
    /// only one. Non-matching segments are preserved verbatim.
    /// </summary>
    public static string? Remove(string? raw, string directory)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var target = (directory ?? string.Empty).Trim();
        if (target.Length == 0) return null;

        var targetNormalized = Normalize(target);
        var targetExpanded = Normalize(SafeExpand(target));

        var survivors = new List<string>();
        bool removedAny = false;
        foreach (var segment in raw.Split(';'))
        {
            if (SegmentMatches(segment, targetNormalized, targetExpanded))
            {
                removedAny = true;
                continue;
            }
            survivors.Add(segment);
        }

        return removedAny ? string.Join(";", survivors) : null;
    }

    private static bool SegmentMatches(string segment, string targetNormalized, string targetExpanded)
    {
        var segmentNormalized = Normalize(segment);
        if (segmentNormalized.Length == 0) return false;
        if (string.Equals(segmentNormalized, targetNormalized, StringComparison.OrdinalIgnoreCase)) return true;

        var segmentExpanded = Normalize(SafeExpand(segment));
        return string.Equals(segmentExpanded, targetExpanded, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Trims surrounding whitespace and a trailing directory separator,
    /// while preserving a bare drive root (<c>C:\</c> stays <c>C:\</c>,
    /// not <c>C:</c>).
    /// </summary>
    private static string Normalize(string segment)
    {
        var value = segment.Trim();
        if (value.Length == 0) return string.Empty;

        int end = value.Length;
        while (end > 0 && (value[end - 1] == '\\' || value[end - 1] == '/')) end--;
        if (end == 0) return string.Empty;

        // Keep one separator after a drive letter so "C:\" doesn't collapse
        // to "C:" (which would change its meaning).
        if (end >= 2 && value[end - 1] == ':') return value.Substring(0, end) + "\\";

        return value.Substring(0, end);
    }

    private static string SafeExpand(string value)
    {
        try { return Environment.ExpandEnvironmentVariables(value); }
        catch { return value; }
    }
}
