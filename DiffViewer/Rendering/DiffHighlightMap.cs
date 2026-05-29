using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.Rendering;

/// <summary>
/// Per-side mapping from 1-based document line number to
/// <see cref="LineHighlight"/>. Built once per diff computation and handed
/// to the background renderer + intra-line colorizer attached to each
/// AvalonEdit <c>TextEditor</c>.
/// </summary>
public sealed class DiffHighlightMap
{
    /// <summary>Line highlights for the left (old) document.</summary>
    public IReadOnlyDictionary<int, LineHighlight> LeftLines { get; }

    /// <summary>Line highlights for the right (new) document.</summary>
    public IReadOnlyDictionary<int, LineHighlight> RightLines { get; }

    public static DiffHighlightMap Empty { get; } = new(
        new Dictionary<int, LineHighlight>(),
        new Dictionary<int, LineHighlight>());

    public DiffHighlightMap(
        IReadOnlyDictionary<int, LineHighlight> leftLines,
        IReadOnlyDictionary<int, LineHighlight> rightLines)
    {
        LeftLines = leftLines;
        RightLines = rightLines;
    }

    /// <summary>
    /// Walk a hunk model and produce highlight maps for both sides.
    ///
    /// <para>When <paramref name="enableIntraLine"/> is true, blocks with
    /// both deletes and inserts get word-level spans paired up
    /// <c>min(D, I)</c> lines at a time. If the pairing-viability policy
    /// (<see cref="IsPairingViable"/>) rejects a positional pair, both
    /// sides are demoted to unpaired <see cref="DiffLineKind.Deleted"/> /
    /// <see cref="DiffLineKind.Inserted"/> so the rendered signal matches
    /// the algorithm's "these lines aren't really paired" conclusion.</para>
    ///
    /// <para>When <paramref name="enableIntraLine"/> is false, no pairing
    /// happens at all — every delete becomes <see cref="DiffLineKind.Deleted"/>
    /// and every insert becomes <see cref="DiffLineKind.Inserted"/>.
    /// <see cref="DiffLineKind.Modified"/> (yellow) is never emitted on
    /// the intra-line-off path because there would be no inner red/green
    /// span to justify the yellow tint — the user would see a "changed"
    /// signal with no indication of <em>where</em> the change is. See the
    /// canonical doc on <see cref="DiffLineKind.Modified"/>.</para>
    /// </summary>
    public static DiffHighlightMap FromHunks(
        IReadOnlyList<DiffHunk> hunks,
        IDiffService diffService,
        bool enableIntraLine,
        bool ignoreWhitespace)
    {
        if (hunks.Count == 0) return Empty;

        var left = new Dictionary<int, LineHighlight>();
        var right = new Dictionary<int, LineHighlight>();

        foreach (var hunk in hunks)
        {
            // Walk the hunk's lines splitting them into adjacent
            // delete-then-insert blocks so we can pair them for intra-line.
            int i = 0;
            while (i < hunk.Lines.Count)
            {
                var line = hunk.Lines[i];
                if (line.Kind == DiffLineKind.Context)
                {
                    i++;
                    continue;
                }

                int blockStart = i;
                while (i < hunk.Lines.Count && hunk.Lines[i].Kind == DiffLineKind.Deleted) i++;
                int deletedEnd = i;
                while (i < hunk.Lines.Count && hunk.Lines[i].Kind == DiffLineKind.Inserted) i++;
                int insertedEnd = i;

                int deletedCount = deletedEnd - blockStart;
                int insertedCount = insertedEnd - deletedEnd;
                // Intra-line off: never pair. Modified (yellow) requires a
                // visible intra-line span to justify the tint; without
                // spans, yellow gives the user no "where did it change"
                // signal. Falling through to the spill loops below emits
                // every delete as Deleted and every insert as Inserted
                // (red/green), which is what the user can actually act on.
                int paired = enableIntraLine ? Math.Min(deletedCount, insertedCount) : 0;

                // Paired lines: try intra-line spans, demote pairs the
                // similarity policy rejects to unpaired Deleted / Inserted.
                // See the canonical doc on DiffLineKind.Modified.
                for (int k = 0; k < paired; k++)
                {
                    var del = hunk.Lines[blockStart + k];
                    var ins = hunk.Lines[deletedEnd + k];

                    IReadOnlyList<IntraLineSpan>? leftSpans = null;
                    IReadOnlyList<IntraLineSpan>? rightSpans = null;
                    if (enableIntraLine)
                    {
                        if (!TryPairWithIntraLineSpans(
                                diffService, del.Text, ins.Text, ignoreWhitespace,
                                out var ls, out var rs))
                        {
                            // Pairing-viability policy rejected the pair:
                            // demote both sides to unpaired Deleted /
                            // Inserted so the rendered signal matches the
                            // algorithm's conclusion.
                            if (del.OldLineNumber is int oldLnD)
                            {
                                left[oldLnD] = new LineHighlight(DiffLineKind.Deleted, null);
                            }
                            if (ins.NewLineNumber is int newLnD)
                            {
                                right[newLnD] = new LineHighlight(DiffLineKind.Inserted, null);
                            }
                            continue;
                        }
                        leftSpans = ls;
                        rightSpans = rs;
                    }

                    if (del.OldLineNumber is int oldLn)
                    {
                        left[oldLn] = new LineHighlight(DiffLineKind.Modified, leftSpans);
                    }
                    if (ins.NewLineNumber is int newLn)
                    {
                        right[newLn] = new LineHighlight(DiffLineKind.Modified, rightSpans);
                    }
                }

                // Unpaired deletes (extra removed lines).
                for (int k = paired; k < deletedCount; k++)
                {
                    var del = hunk.Lines[blockStart + k];
                    if (del.OldLineNumber is int oldLn)
                    {
                        left[oldLn] = new LineHighlight(DiffLineKind.Deleted, null);
                    }
                }

                // Unpaired inserts (extra added lines).
                for (int k = paired; k < insertedCount; k++)
                {
                    var ins = hunk.Lines[deletedEnd + k];
                    if (ins.NewLineNumber is int newLn)
                    {
                        right[newLn] = new LineHighlight(DiffLineKind.Inserted, null);
                    }
                }
            }
        }

        return new DiffHighlightMap(left, right);
    }

    /// <summary>
    /// Try to compute intra-line span highlights for a positionally paired
    /// (delete, insert) line pair, gated by the pairing-viability policy in
    /// <see cref="IsPairingViable"/>.
    ///
    /// <para>Suppresses spans for low-similarity line pairs: when two paired
    /// lines share only whitespace and a handful of stray delimiters, the
    /// token diff "matches" those incidentally and the result is a noisy
    /// near-total highlight of both sides. We compute a similarity ratio
    /// over non-whitespace tokens and bail out below
    /// <see cref="MinPairingSimilarity"/>.</para>
    /// </summary>
    /// <returns>
    /// <c>true</c> if the pair is viable; <paramref name="leftSpans"/> and
    /// <paramref name="rightSpans"/> are populated with the intra-line
    /// highlights to draw on a <see cref="DiffLineKind.Modified"/>-stamped
    /// row. <c>false</c> if the similarity policy rejected the pairing;
    /// caller should demote both sides to unpaired
    /// <see cref="DiffLineKind.Deleted"/> / <see cref="DiffLineKind.Inserted"/>.
    /// Throws if the underlying <paramref name="diffService"/> throws
    /// (failure mode is policy, not exception).
    /// </returns>
    private static bool TryPairWithIntraLineSpans(
        IDiffService diffService,
        string oldLine,
        string newLine,
        bool ignoreWhitespace,
        out IReadOnlyList<IntraLineSpan> leftSpans,
        out IReadOnlyList<IntraLineSpan> rightSpans)
    {
        var pieces = diffService.ComputeIntraLineDiff(oldLine, newLine, ignoreWhitespace);

        if (!IsPairingViable(pieces))
        {
            leftSpans = Array.Empty<IntraLineSpan>();
            rightSpans = Array.Empty<IntraLineSpan>();
            return false;
        }

        var leftList = new List<IntraLineSpan>();
        var rightList = new List<IntraLineSpan>();

        int leftCol = 0;
        int rightCol = 0;

        foreach (var piece in pieces)
        {
            int len = piece.Text.Length;
            switch (piece.Kind)
            {
                case IntraLinePieceKind.Unchanged:
                    leftCol += len;
                    rightCol += len;
                    break;
                case IntraLinePieceKind.Deleted:
                    if (len > 0)
                    {
                        leftList.Add(new IntraLineSpan(leftCol, leftCol + len, IntraLineSpanKind.Deleted));
                    }
                    leftCol += len;
                    break;
                case IntraLinePieceKind.Inserted:
                    if (len > 0)
                    {
                        rightList.Add(new IntraLineSpan(rightCol, rightCol + len, IntraLineSpanKind.Inserted));
                    }
                    rightCol += len;
                    break;
            }
        }

        leftSpans = leftList;
        rightSpans = rightList;
        return true;
    }

    /// <summary>
    /// Below this ratio of matched tokens to the smaller-or-equal side's
    /// non-whitespace tokens, the two paired lines are treated as unrelated.
    /// <see cref="FromHunks"/> demotes such pairs to unpaired Deleted /
    /// Inserted; the predicate's <c>false</c> return is a pairing-viability
    /// gate, not just a cosmetic span-suppression knob.
    /// </summary>
    private const double MinPairingSimilarity = 0.5;

    /// <summary>
    /// Decides whether a positionally paired (delete, insert) pair is
    /// viable based on token-level overlap from the intra-line diff.
    /// Returning <c>false</c> tells <see cref="FromHunks"/> to demote both
    /// sides of the pair to unpaired Deleted / Inserted; returning
    /// <c>true</c> keeps the <see cref="DiffLineKind.Modified"/> stamp and
    /// surfaces the intra-line spans.
    /// </summary>
    private static bool IsPairingViable(IReadOnlyList<IntraLinePiece> pieces)
    {
        // Count non-whitespace tokens per side and the number of matched
        // ones. Whitespace-only tokens shouldn't imply semantic similarity —
        // two unrelated lines often share leading indentation and inter-
        // word spaces, which inflates the ratio.
        int matched = 0;
        int oldTokens = 0;
        int newTokens = 0;
        foreach (var p in pieces)
        {
            if (string.IsNullOrWhiteSpace(p.Text)) continue;
            switch (p.Kind)
            {
                case IntraLinePieceKind.Unchanged:
                    matched++;
                    oldTokens++;
                    newTokens++;
                    break;
                case IntraLinePieceKind.Deleted:
                    oldTokens++;
                    break;
                case IntraLinePieceKind.Inserted:
                    newTokens++;
                    break;
            }
        }
        if (oldTokens == 0 && newTokens == 0) return true; // Nothing meaningful to compare.

        // Asymmetric: take the max of the two per-side match ratios. When
        // one line is fully contained in the other (pure addition or pure
        // removal of inline content), the contained side hits 1.0 even if
        // the longer side's ratio is low — and that's a case the user
        // wants to see highlighted.
        double oldRatio = oldTokens == 0 ? 1.0 : (double)matched / oldTokens;
        double newRatio = newTokens == 0 ? 1.0 : (double)matched / newTokens;
        return Math.Max(oldRatio, newRatio) >= MinPairingSimilarity;
    }
}
