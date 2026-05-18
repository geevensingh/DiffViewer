namespace DiffViewer.Utility;

/// <summary>
/// Single seam for the file-list filter box. Implements the v1 filter
/// syntax: case-insensitive substring match against the entry's
/// repo-relative path, with <c>\</c> ↔ <c>/</c> normalisation on both
/// sides so users can type either separator.
///
/// <para>The slash normalisation matters because <see cref="System.IO.Path"/>
/// renders backslash on Windows but git, paste-from-terminal, and many
/// muscle-memory paths use forward slash. With normalisation, typing
/// <c>foo/bar.cs</c> matches a stored <c>src\foo\bar.cs</c> the same way
/// typing <c>foo\bar.cs</c> would. Filenames on Windows can't legally
/// contain a backslash, so collapsing the two characters to one canonical
/// form is safe.</para>
///
/// <para>Centralising the predicate here keeps the door open for richer
/// syntax (glob, regex) later without touching the view-model. Today
/// callers should pre-normalise their target path via
/// <see cref="Normalize(string)"/> in their constructor (each entry's
/// path is constant for the entry's lifetime), then call
/// <see cref="MatchesNormalized(string, string)"/> at filter time to
/// avoid re-normalising the same string on every keystroke.</para>
/// </summary>
public static class FileListFilter
{
    /// <summary>
    /// Map <c>\</c> to <c>/</c> so the substring compare is
    /// slash-insensitive. Cheap; one allocation per call.
    /// </summary>
    public static string Normalize(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty : s.Replace('\\', '/');

    /// <summary>
    /// True when <paramref name="query"/> matches <paramref name="path"/>
    /// under the v1 filter rules. Empty or null query matches everything
    /// (i.e. the filter is inactive).
    /// </summary>
    public static bool Matches(string? path, string? query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        if (string.IsNullOrEmpty(path)) return false;
        return MatchesNormalized(Normalize(path), query);
    }

    /// <summary>
    /// Variant for callers that already cached a normalised path. Skips
    /// the per-call <see cref="Normalize"/> of the path and only
    /// normalises the (typically shorter) query string.
    /// </summary>
    public static bool MatchesNormalized(string normalizedPath, string? query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        if (string.IsNullOrEmpty(normalizedPath)) return false;
        return normalizedPath.Contains(
            Normalize(query),
            System.StringComparison.OrdinalIgnoreCase);
    }
}
