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
            //
            // Block-walk the hunk in delete-then-insert groups (mirroring
            // DiffHighlightMap.FromHunks) so we can identify positionally
            // paired (delete, insert) lines and suppress the redundant side
            // of an asymmetric Modified pair. "Redundant" here means a
            // Modified line whose own intra-line spans are empty — i.e. an
            // all-yellow row that contributes no information the partner
            // line doesn't already show via its span overlay (e.g. a pure
            // intra-line insertion: the left row would be all-yellow with
            // nothing to highlight, while the right row carries the green
            // insert span). Skipping happens only when the partner has
            // spans; symmetric no-spans pairs (e.g. whitespace-only diffs
            // with Ignore-WS on) keep both sides so the yellow row still
            // signals "something changed here, even if I can't show you
            // exactly where". Unpaired extras (more deletes than inserts,
            // or vice versa) always emit — they have no partner that could
            // make them redundant.
            int j = 0;
            while (j < hunk.Lines.Count)
            {
                var first = hunk.Lines[j];
                if (first.Kind == DiffLineKind.Context)
                {
                    EmitHunkLine(first, map, sb, lineHighlights, lineToSourceLines, ref currentOutputLine);
                    j++;
                    continue;
                }

                int blockStart = j;
                while (j < hunk.Lines.Count && hunk.Lines[j].Kind == DiffLineKind.Deleted) j++;
                int deletedEnd = j;
                while (j < hunk.Lines.Count && hunk.Lines[j].Kind == DiffLineKind.Inserted) j++;
                int insertedEnd = j;

                int deletedCount = deletedEnd - blockStart;
                int insertedCount = insertedEnd - deletedEnd;
                int paired = Math.Min(deletedCount, insertedCount);

                // Pre-resolve highlights for the paired slice so we can
                // decide which side to suppress in each pair without
                // re-resolving when we later emit. Unpaired extras fall
                // through to the map-resolving emit path.
                var delHighlights = new LineHighlight?[paired];
                var insHighlights = new LineHighlight?[paired];
                var skipDel = new bool[paired];
                var skipIns = new bool[paired];

                for (int k = 0; k < paired; k++)
                {
                    var delHl = BuildHighlight(hunk.Lines[blockStart + k], map);
                    var insHl = BuildHighlight(hunk.Lines[deletedEnd + k], map);
                    delHighlights[k] = delHl;
                    insHighlights[k] = insHl;

                    bool delAllYellow = IsModifiedNoSpans(delHl);
                    bool insAllYellow = IsModifiedNoSpans(insHl);

                    skipDel[k] = delAllYellow && !insAllYellow;
                    skipIns[k] = insAllYellow && !delAllYellow;
                }

                // Emit deletes in hunk order, skipping the suppressed side of
                // any pair. Hunk order = "all deletes, then all inserts" — keep
                // that grouping so multi-line edits read top-to-bottom like a
                // unified diff (rather than interleaved by pair).
                for (int k = 0; k < deletedCount; k++)
                {
                    if (k < paired && skipDel[k]) continue;
                    var del = hunk.Lines[blockStart + k];
                    if (k < paired)
                    {
                        EmitHunkLine(del, delHighlights[k]!, sb, lineHighlights, lineToSourceLines, ref currentOutputLine);
                    }
                    else
                    {
                        EmitHunkLine(del, map, sb, lineHighlights, lineToSourceLines, ref currentOutputLine);
                    }
                }

                for (int k = 0; k < insertedCount; k++)
                {
                    if (k < paired && skipIns[k]) continue;
                    var ins = hunk.Lines[deletedEnd + k];
                    if (k < paired)
                    {
                        EmitHunkLine(ins, insHighlights[k]!, sb, lineHighlights, lineToSourceLines, ref currentOutputLine);
                    }
                    else
                    {
                        EmitHunkLine(ins, map, sb, lineHighlights, lineToSourceLines, ref currentOutputLine);
                    }
                }
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
    /// Pull the per-side <see cref="LineHighlight"/> for <paramref name="line"/>
    /// out of <paramref name="map"/> (keyed by Old/NewLineNumber) and use it
    /// verbatim — same <see cref="LineHighlight.Kind"/> and same
    /// <see cref="LineHighlight.IntraLineSpans"/> the side-by-side renderer
    /// would draw.
    ///
    /// <para>The map is the authority on Kind. In particular, a line that
    /// was paired by <see cref="DiffHighlightMap.FromHunks"/> is stamped
    /// <see cref="DiffLineKind.Modified"/> on <em>both</em> sides even when
    /// the intra-line diff yielded zero spans on one side (e.g. a pure
    /// insertion of "Any, " inside "from typing import Optional" gives the
    /// left side no Deleted spans but the line is still part of a Modified
    /// pair). Deriving the kind from <c>spans.Count &gt; 0</c> here would
    /// drop that side back to <see cref="DiffLineKind.Deleted"/> /
    /// <see cref="DiffLineKind.Inserted"/> and the
    /// <see cref="LineBackgroundBrushSelector"/> would paint a strong
    /// red/green row instead of the soft yellow the side-by-side view
    /// shows — a visible inconsistency between the two modes.</para>
    ///
    /// <para>When the line is absent from the map (intra-line disabled, or
    /// the map was simply not built for this diff), fall back to
    /// <c>line.Kind</c> with no spans — the row gets the strong full-line
    /// red/green tint, which is the "this whole line is what changed"
    /// signal when there's no Modified-pair information to refine it.</para>
    ///
    /// <para>Span columns are returned unchanged: <see cref="BuildFullFile"/>
    /// emits each line verbatim with no prefix character, so the
    /// colorizer's <c>lineStart + StartColumn</c> arithmetic lands directly
    /// on the changed characters.</para>
    /// </summary>
    private static LineHighlight BuildHighlight(DiffLine line, DiffHighlightMap map)
    {
        switch (line.Kind)
        {
            case DiffLineKind.Deleted:
                if (line.OldLineNumber is int oldLn &&
                    map.LeftLines.TryGetValue(oldLn, out var leftHl))
                {
                    return leftHl;
                }
                break;
            case DiffLineKind.Inserted:
                if (line.NewLineNumber is int newLn &&
                    map.RightLines.TryGetValue(newLn, out var rightHl))
                {
                    return rightHl;
                }
                break;
        }

        return new LineHighlight(line.Kind, null);
    }

    /// <summary>
    /// Append <paramref name="line"/>'s text to <paramref name="sb"/>, record its
    /// per-line highlight (if non-Context), and push its source-line tuple
    /// onto <paramref name="lineToSourceLines"/>. Single-line emission used
    /// by <see cref="BuildBoth"/> when walking hunk blocks.
    /// </summary>
    private static void EmitHunkLine(
        DiffLine line, DiffHighlightMap map,
        StringBuilder sb, Dictionary<int, LineHighlight> lineHighlights,
        List<(int? OldLine, int? NewLine)> lineToSourceLines, ref int currentOutputLine)
    {
        sb.Append(line.Text).Append('\n');
        if (line.Kind != DiffLineKind.Context)
        {
            lineHighlights[currentOutputLine] = BuildHighlight(line, map);
        }
        // DiffLine already carries the per-side line numbers: both set for
        // Context/Modified, OldLineNumber=null for Inserted, NewLineNumber=null
        // for Deleted. That's exactly the shape the viewport indicator's
        // "nearest non-null" lookup wants.
        lineToSourceLines.Add((line.OldLineNumber, line.NewLineNumber));
        currentOutputLine++;
    }

    /// <summary>
    /// Same as the map-resolving overload, but takes a pre-resolved
    /// <see cref="LineHighlight"/> for the line — used in the paired-block
    /// path of <see cref="BuildBoth"/>, where we already had to resolve
    /// both sides' highlights to decide whether to suppress one.
    /// </summary>
    private static void EmitHunkLine(
        DiffLine line, LineHighlight highlight,
        StringBuilder sb, Dictionary<int, LineHighlight> lineHighlights,
        List<(int? OldLine, int? NewLine)> lineToSourceLines, ref int currentOutputLine)
    {
        sb.Append(line.Text).Append('\n');
        // Paired sides are always Deleted/Inserted (Context never enters the
        // block walk), so the highlight always belongs in the map.
        lineHighlights[currentOutputLine] = highlight;
        lineToSourceLines.Add((line.OldLineNumber, line.NewLineNumber));
        currentOutputLine++;
    }

    /// <summary>
    /// True when <paramref name="highlight"/> describes a paired Modified
    /// line whose own intra-line spans contribute nothing — i.e. an
    /// all-yellow row. Used to detect the redundant side of an asymmetric
    /// Modified pair in inline + Both mode (the partner line carries the
    /// substantive span overlay; this side would only show "something
    /// changed somewhere" with no visible where).
    /// </summary>
    private static bool IsModifiedNoSpans(LineHighlight highlight)
    {
        return highlight.Kind == DiffLineKind.Modified
            && (highlight.IntraLineSpans is null || highlight.IntraLineSpans.Count == 0);
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
