using System.Text;
using DiffViewer.Models;

namespace DiffViewer.Rendering;

/// <summary>
/// Builds the document that the inline-mode editor renders: the full
/// right-side file with hunks woven in. Lines are emitted verbatim, with
/// no <c>@@</c> headers and no <c>+</c>/<c>-</c>/space prefix character.
/// Added / removed / modified lines are signalled exclusively by per-line
/// background tints (<see cref="InlineDiffBackgroundRenderer"/>) and
/// intra-line word spans (<see cref="IntraLineColorizer"/>), so columns
/// line up with the underlying file and the inline view matches the
/// side-by-side view's "show the file, mark diffs by color" model.
/// Returns the rendered text plus a per-line <see cref="LineHighlight"/>
/// (kind + optional intra-line spans) for the renderer + colorizer.
/// </summary>
public static class InlineDiffBuilder
{
    /// <summary>
    /// Inline-mode document plus everything callers need to project inline
    /// output lines back onto the source files.
    /// </summary>
    /// <param name="Text">The rendered inline document.</param>
    /// <param name="LineHighlights">
    /// Per-line highlight info (kind + intra-line spans); only present for
    /// non-context lines.
    /// </param>
    /// <param name="LineToSourceLines">
    /// Per-inline-output-line mapping back to the underlying old / new buffers.
    /// Index 0 ↔ inline line 1. Each entry is a <c>(OldLine, NewLine)</c>
    /// pair of 1-based source line numbers; either can be <c>null</c>
    /// (a pure-delete output line has <c>NewLine == null</c>, a pure-insert
    /// has <c>OldLine == null</c>). The viewport indicator uses this to
    /// project the editor's visible window onto the two-column hunk bar.
    /// </param>
    public sealed record InlineDocument(
        string Text,
        IReadOnlyDictionary<int, LineHighlight> LineHighlights,
        IReadOnlyList<(int? OldLine, int? NewLine)> LineToSourceLines);

    private static readonly InlineDocument _empty =
        new(string.Empty,
            new Dictionary<int, LineHighlight>(),
            Array.Empty<(int? OldLine, int? NewLine)>());

    /// <summary>
    /// The empty inline document. Returned when there's no file selected,
    /// a placeholder is showing, or the diff is otherwise unavailable.
    /// </summary>
    public static InlineDocument Empty => _empty;

    /// <summary>
    /// Build an inline document showing the <em>full</em> file with hunks
    /// woven in. Every line — both inside and outside hunks — is emitted
    /// <em>verbatim</em>, with no <c>+</c>/<c>-</c>/space prefix character.
    /// The user sees the file as-is; added / removed / modified lines are
    /// tinted via the inline background renderer (full-line red / green /
    /// yellow) and word-level intra-line spans are overlaid by the
    /// intra-line colorizer. Same channel as side-by-side mode — both
    /// views look like the file with diffs coloured rather than a unified-
    /// diff text dump.
    ///
    /// <para><paramref name="sideVisibility"/> controls which side(s) of
    /// the diff appear in the rendered document:
    /// <list type="bullet">
    ///   <item><description><see cref="DiffSideVisibility.Both"/> — full
    ///   unified weave (right-side file with deleted lines interleaved in
    ///   red); the default and historically the only behaviour.</description></item>
    ///   <item><description><see cref="DiffSideVisibility.LeftOnly"/> —
    ///   left-side file emitted verbatim with deletions tinted red;
    ///   insertions hidden.</description></item>
    ///   <item><description><see cref="DiffSideVisibility.RightOnly"/> —
    ///   right-side file emitted verbatim with insertions tinted green;
    ///   deletions hidden.</description></item>
    /// </list>
    /// In the two single-side modes the output equals the underlying
    /// source file in line order, so AvalonEdit's synthetic 1..N line
    /// numbers line up with the source line numbers users expect.</para>
    ///
    /// <para><paramref name="map"/> supplies the per-line intra-line spans
    /// computed by <see cref="DiffHighlightMap.FromHunks"/>; pass
    /// <see cref="DiffHighlightMap.Empty"/> for tests that don't care about
    /// spans (lines will still get a <see cref="LineHighlight"/> with the
    /// correct kind, just no spans).</para>
    ///
    /// <para>Used by <see cref="ViewModels.DiffPaneViewModel"/> in inline mode so the
    /// user sees the whole file with diffs highlighted, not just the
    /// 3-line-context hunks. Side-by-side mode is unaffected — it already
    /// shows the full blobs in two editors.</para>
    /// </summary>
    public static InlineDocument BuildFullFile(
        string left, string right, IReadOnlyList<DiffHunk> hunks, DiffHighlightMap map,
        DiffSideVisibility sideVisibility = DiffSideVisibility.Both)
    {
        return sideVisibility switch
        {
            DiffSideVisibility.LeftOnly => BuildLeftOnly(left, hunks, map),
            DiffSideVisibility.RightOnly => BuildRightOnly(right, hunks, map),
            _ => BuildBoth(left, right, hunks, map),
        };
    }

    private static InlineDocument BuildBoth(
        string left, string right, IReadOnlyList<DiffHunk> hunks, DiffHighlightMap map)
    {
        var leftLines = SplitLines(left);
        var rightLines = SplitLines(right);

        // No diff at all: emit the right-side blob verbatim, no prefixes,
        // no highlights. With no hunks the two sides are byte-identical,
        // so each output line maps to itself on both sides.
        if (hunks.Count == 0)
        {
            var identity = new List<(int? OldLine, int? NewLine)>(rightLines.Count);
            for (int i = 1; i <= rightLines.Count; i++)
            {
                identity.Add((i, i));
            }
            return new InlineDocument(right, new Dictionary<int, LineHighlight>(), identity);
        }

        var sb = new StringBuilder();
        var lineHighlights = new Dictionary<int, LineHighlight>();
        var lineToSourceLines = new List<(int? OldLine, int? NewLine)>();
        int currentOutputLine = 1;
        int oldCursor = 1; // 1-based next-unread line of left file
        int newCursor = 1; // 1-based next-unread line of right file

        for (int h = 0; h < hunks.Count; h++)
        {
            var hunk = hunks[h];

            // Emit unchanged context lines BEFORE this hunk by walking the
            // right (new) file from newCursor up to (but not including)
            // hunk.NewStartLine. Use the right side as the source of truth
            // for context — outside hunks the two sides are byte-identical.
            int hunkNewStart = hunk.NewStartLine > 0 ? hunk.NewStartLine : newCursor;
            for (int i = newCursor; i < hunkNewStart && i <= rightLines.Count; i++)
            {
                sb.Append(rightLines[i - 1]).Append('\n');
                // Outside hunks the two sides are byte-identical, so a
                // relative offset on the new side maps 1:1 onto the old side.
                lineToSourceLines.Add((oldCursor + (i - newCursor), i));
                currentOutputLine++;
            }

            // Emit the hunk content verbatim — no +/-/space prefix character.
            // Each line keeps the column positions it has in the underlying
            // file, so context lines around the diff align visually with
            // lines emitted from outside the hunk (which are also verbatim).
            // Added / removed / modified lines are signalled to the user
            // exclusively by the InlineDiffBackgroundRenderer's per-line
            // tint and the IntraLineColorizer's word-level spans — i.e. the
            // same channel side-by-side mode uses, keeping the two views
            // visually consistent.
            foreach (var line in hunk.Lines)
            {
                sb.Append(line.Text).Append('\n');
                if (line.Kind != DiffLineKind.Context)
                {
                    lineHighlights[currentOutputLine] = BuildHighlight(line, map);
                }
                // DiffLine already carries the per-side line numbers: both
                // set for Context/Modified, OldLineNumber=null for Inserted,
                // NewLineNumber=null for Deleted. That's exactly the shape
                // the viewport indicator's "nearest non-null" lookup wants.
                lineToSourceLines.Add((line.OldLineNumber, line.NewLineNumber));
                currentOutputLine++;
            }

            // Advance cursors past the consumed regions on each side.
            oldCursor = (hunk.OldStartLine > 0 ? hunk.OldStartLine : oldCursor) + hunk.OldLineCount;
            newCursor = hunkNewStart + hunk.NewLineCount;
        }

        // Tail: emit any remaining unchanged lines after the last hunk.
        for (int i = newCursor; i <= rightLines.Count; i++)
        {
            sb.Append(rightLines[i - 1]).Append('\n');
            lineToSourceLines.Add((oldCursor + (i - newCursor), i));
            currentOutputLine++;
        }

        return new InlineDocument(sb.ToString(), lineHighlights, lineToSourceLines);
    }

    /// <summary>
    /// LeftOnly variant: emit the <em>left</em> file verbatim with deleted
    /// lines tinted; inserted lines from the hunks are suppressed entirely.
    /// Output line N corresponds to left-file line N, so the editor's
    /// synthetic line numbers match the user's expectation of "looking at
    /// the old file".
    /// </summary>
    private static InlineDocument BuildLeftOnly(
        string left, IReadOnlyList<DiffHunk> hunks, DiffHighlightMap map)
    {
        var leftLines = SplitLines(left);

        if (hunks.Count == 0)
        {
            var identity = new List<(int? OldLine, int? NewLine)>(leftLines.Count);
            for (int i = 1; i <= leftLines.Count; i++)
            {
                identity.Add((i, i));
            }
            return new InlineDocument(left, new Dictionary<int, LineHighlight>(), identity);
        }

        var sb = new StringBuilder();
        var lineHighlights = new Dictionary<int, LineHighlight>();
        var lineToSourceLines = new List<(int? OldLine, int? NewLine)>();
        int currentOutputLine = 1;
        int oldCursor = 1;
        int newCursor = 1;

        for (int h = 0; h < hunks.Count; h++)
        {
            var hunk = hunks[h];

            // Pre-hunk context: walk the LEFT file from oldCursor up to
            // (but not including) hunk.OldStartLine. Outside hunks the two
            // sides are identical, so we can recover the matching right-side
            // line number from newCursor's offset.
            int hunkOldStart = hunk.OldStartLine > 0 ? hunk.OldStartLine : oldCursor;
            for (int i = oldCursor; i < hunkOldStart && i <= leftLines.Count; i++)
            {
                sb.Append(leftLines[i - 1]).Append('\n');
                lineToSourceLines.Add((i, newCursor + (i - oldCursor)));
                currentOutputLine++;
            }

            // Hunk body: keep Context + Deleted, drop Inserted. The kept
            // lines (in hunk order) reconstruct the left file's slice for
            // this hunk verbatim, so output stays in left-file line order.
            foreach (var line in hunk.Lines)
            {
                if (line.Kind == DiffLineKind.Inserted) continue;
                sb.Append(line.Text).Append('\n');
                if (line.Kind != DiffLineKind.Context)
                {
                    lineHighlights[currentOutputLine] = BuildHighlight(line, map);
                }
                lineToSourceLines.Add((line.OldLineNumber, line.NewLineNumber));
                currentOutputLine++;
            }

            oldCursor = hunkOldStart + hunk.OldLineCount;
            newCursor = (hunk.NewStartLine > 0 ? hunk.NewStartLine : newCursor) + hunk.NewLineCount;
        }

        for (int i = oldCursor; i <= leftLines.Count; i++)
        {
            sb.Append(leftLines[i - 1]).Append('\n');
            lineToSourceLines.Add((i, newCursor + (i - oldCursor)));
            currentOutputLine++;
        }

        return new InlineDocument(sb.ToString(), lineHighlights, lineToSourceLines);
    }

    /// <summary>
    /// RightOnly variant: emit the <em>right</em> file verbatim with
    /// inserted lines tinted; deleted lines from the hunks are suppressed
    /// entirely. Output line N corresponds to right-file line N.
    /// </summary>
    private static InlineDocument BuildRightOnly(
        string right, IReadOnlyList<DiffHunk> hunks, DiffHighlightMap map)
    {
        var rightLines = SplitLines(right);

        if (hunks.Count == 0)
        {
            var identity = new List<(int? OldLine, int? NewLine)>(rightLines.Count);
            for (int i = 1; i <= rightLines.Count; i++)
            {
                identity.Add((i, i));
            }
            return new InlineDocument(right, new Dictionary<int, LineHighlight>(), identity);
        }

        var sb = new StringBuilder();
        var lineHighlights = new Dictionary<int, LineHighlight>();
        var lineToSourceLines = new List<(int? OldLine, int? NewLine)>();
        int currentOutputLine = 1;
        int oldCursor = 1;
        int newCursor = 1;

        for (int h = 0; h < hunks.Count; h++)
        {
            var hunk = hunks[h];

            int hunkNewStart = hunk.NewStartLine > 0 ? hunk.NewStartLine : newCursor;
            for (int i = newCursor; i < hunkNewStart && i <= rightLines.Count; i++)
            {
                sb.Append(rightLines[i - 1]).Append('\n');
                lineToSourceLines.Add((oldCursor + (i - newCursor), i));
                currentOutputLine++;
            }

            foreach (var line in hunk.Lines)
            {
                if (line.Kind == DiffLineKind.Deleted) continue;
                sb.Append(line.Text).Append('\n');
                if (line.Kind != DiffLineKind.Context)
                {
                    lineHighlights[currentOutputLine] = BuildHighlight(line, map);
                }
                lineToSourceLines.Add((line.OldLineNumber, line.NewLineNumber));
                currentOutputLine++;
            }

            oldCursor = (hunk.OldStartLine > 0 ? hunk.OldStartLine : oldCursor) + hunk.OldLineCount;
            newCursor = hunkNewStart + hunk.NewLineCount;
        }

        for (int i = newCursor; i <= rightLines.Count; i++)
        {
            sb.Append(rightLines[i - 1]).Append('\n');
            lineToSourceLines.Add((oldCursor + (i - newCursor), i));
            currentOutputLine++;
        }

        return new InlineDocument(sb.ToString(), lineHighlights, lineToSourceLines);
    }

    /// <summary>
    /// Look up the intra-line spans for <paramref name="line"/> in <paramref name="map"/>
    /// (keyed by Old/NewLineNumber) and pack them with the line's kind into a
    /// <see cref="LineHighlight"/>. The kind on the returned highlight stays
    /// Deleted / Inserted (not Modified, which is what the map stamps for
    /// high-similarity paired lines) so the inline background renderer keeps
    /// tinting red/green rather than the side-by-side modified yellow.
    ///
    /// <para>After the demote fix in
    /// <see cref="DiffHighlightMap.FromHunks"/>, low-similarity pairs already
    /// arrive on the map side as <see cref="DiffLineKind.Deleted"/> /
    /// <see cref="DiffLineKind.Inserted"/>; this override only matters for
    /// high-similarity pairs (which the map stamps as
    /// <see cref="DiffLineKind.Modified"/>). Removing the override would
    /// silently re-introduce yellow on inline mode for those pairs.</para>
    ///
    /// <para>Spans are returned unchanged: <see cref="BuildFullFile"/> emits
    /// each line verbatim with no prefix character, so the colorizer's
    /// <c>lineStart + StartColumn</c> arithmetic lands directly on the
    /// changed characters.</para>
    /// </summary>
    private static LineHighlight BuildHighlight(DiffLine line, DiffHighlightMap map)
    {
        IReadOnlyList<IntraLineSpan>? spans = null;
        switch (line.Kind)
        {
            case DiffLineKind.Deleted:
                if (line.OldLineNumber is int oldLn &&
                    map.LeftLines.TryGetValue(oldLn, out var leftHl))
                {
                    spans = leftHl.IntraLineSpans;
                }
                break;
            case DiffLineKind.Inserted:
                if (line.NewLineNumber is int newLn &&
                    map.RightLines.TryGetValue(newLn, out var rightHl))
                {
                    spans = rightHl.IntraLineSpans;
                }
                break;
        }
        return new LineHighlight(line.Kind, spans);
    }

    private static List<string> SplitLines(string text)
    {
        // Preserve mixed-EOL inputs by splitting on the canonical break and
        // stripping any trailing CR. We don't preserve original line endings
        // here — the inline view emits LF — because AvalonEdit normalises
        // anyway and the diff highlighting works off line numbers.
        if (text.Length == 0) return new List<string>();
        var raw = text.Split('\n');
        var result = new List<string>(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            var s = raw[i];
            if (s.Length > 0 && s[^1] == '\r') s = s[..^1];
            // Drop the synthetic empty trailing element produced by a final '\n'.
            if (i == raw.Length - 1 && s.Length == 0) break;
            result.Add(s);
        }
        return result;
    }
}
