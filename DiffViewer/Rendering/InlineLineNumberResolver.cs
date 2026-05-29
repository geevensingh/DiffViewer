namespace DiffViewer.Rendering;

/// <summary>
/// Pure-logic helpers for <see cref="InlineLineNumberMargin"/>.
///
/// <para>The inline diff document interleaves rows from the left (old)
/// and right (new) files into a single buffer. AvalonEdit's built-in
/// <see cref="ICSharpCode.AvalonEdit.Editing.LineNumberMargin"/>
/// numbers those rows sequentially (1, 2, 3 ...) — but each row
/// actually originated from a specific position in either the old or
/// new file, and the user expects the gutter to show that source
/// position, not the inline-buffer row index. The view-model exposes
/// a <c>(OldLine, NewLine)</c> pair per row via
/// <c>DiffPaneViewModel.InlineLineToSourceLines</c>; this resolver
/// turns that pair into the number the gutter renders.</para>
///
/// <para>Rule: <c>NewLine ?? OldLine</c>. Context rows and insertion
/// rows render their new-file line number; pure-deletion rows render
/// their old-file line number; rows that have neither (would only
/// happen for synthetic padding rows the builder does not currently
/// emit) render blank.</para>
///
/// <para>By design a paired delete + insert block may produce two
/// adjacent rows with the same displayed number (the old-file line of
/// the delete and the new-file line of the insert frequently agree
/// when the file's pre/post lengths agree up to that point). The
/// resolver does not deduplicate — the row's red/green tint is what
/// distinguishes the sides, and the gutter is telling the user where
/// each row came from in its own source file.</para>
/// </summary>
internal static class InlineLineNumberResolver
{
    /// <summary>
    /// Resolves the gutter number for a 1-based inline-document line.
    /// Returns <c>null</c> when the row is out of range or has no
    /// source mapping on either side.
    /// </summary>
    public static int? Resolve(
        IReadOnlyList<(int? OldLine, int? NewLine)> sourceLines,
        int docLine)
    {
        ArgumentNullException.ThrowIfNull(sourceLines);

        int index = docLine - 1;
        if (index < 0 || index >= sourceLines.Count) return null;

        var (oldLine, newLine) = sourceLines[index];
        return newLine ?? oldLine;
    }

    /// <summary>
    /// Largest line number any row in <paramref name="sourceLines"/>
    /// could display. Used by the margin's
    /// <c>MeasureOverride</c> to reserve enough width for the widest
    /// possible string. Returns 0 for an empty input.
    /// </summary>
    public static int MaxDisplayedValue(
        IReadOnlyList<(int? OldLine, int? NewLine)> sourceLines)
    {
        ArgumentNullException.ThrowIfNull(sourceLines);

        int max = 0;
        foreach (var (oldLine, newLine) in sourceLines)
        {
            int candidate = newLine ?? oldLine ?? 0;
            if (candidate > max) max = candidate;
        }
        return max;
    }
}
