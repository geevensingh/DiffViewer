using DiffViewer.Models;
using DiffViewer.Rendering;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Rendering;

public class DiffHighlightMapTests
{
    private readonly DiffService _diff = new();

    [Fact]
    public void FromHunks_EmptyHunkList_ReturnsEmptyMap()
    {
        var map = DiffHighlightMap.FromHunks(
            Array.Empty<DiffHunk>(),
            _diff,
            enableIntraLine: false,
            ignoreWhitespace: false);

        map.LeftLines.Should().BeEmpty();
        map.RightLines.Should().BeEmpty();
    }

    [Fact]
    public void FromHunks_PureInsert_OnlyMarksRightSide()
    {
        var hunks = _diff.ComputeDiff("", "alpha\nbeta", new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: false, ignoreWhitespace: false);

        map.LeftLines.Should().BeEmpty();
        map.RightLines.Should().HaveCount(2);
        map.RightLines[1].Kind.Should().Be(DiffLineKind.Inserted);
        map.RightLines[2].Kind.Should().Be(DiffLineKind.Inserted);
    }

    [Fact]
    public void FromHunks_PureDelete_OnlyMarksLeftSide()
    {
        var hunks = _diff.ComputeDiff("alpha\nbeta", "", new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: false, ignoreWhitespace: false);

        map.RightLines.Should().BeEmpty();
        map.LeftLines.Should().HaveCount(2);
        map.LeftLines[1].Kind.Should().Be(DiffLineKind.Deleted);
        map.LeftLines[2].Kind.Should().Be(DiffLineKind.Deleted);
    }

    [Fact]
    public void FromHunks_IntraLineDisabled_PairedDeleteInsert_RendersAsDeleteAndInsert()
    {
        // Intra-line off ⇒ no pairing at all. Both sides go red+green,
        // never yellow, because yellow without intra-line spans gives
        // the user no signal about where the change is.
        var hunks = _diff.ComputeDiff("alpha\nbeta\ngamma", "alpha\nBETA\ngamma", new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: false, ignoreWhitespace: false);

        map.LeftLines.Should().ContainKey(2);
        map.LeftLines[2].Kind.Should().Be(DiffLineKind.Deleted);
        map.LeftLines[2].IntraLineSpans.Should().BeNull();
        map.RightLines.Should().ContainKey(2);
        map.RightLines[2].Kind.Should().Be(DiffLineKind.Inserted);
        map.RightLines[2].IntraLineSpans.Should().BeNull();
    }

    [Fact]
    public void FromHunks_IntraLineEnabled_PopulatesSpansForModifiedLines()
    {
        var hunks = _diff.ComputeDiff("hello world", "hello WORLD", new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: true, ignoreWhitespace: false);

        map.LeftLines.Should().ContainKey(1);
        map.RightLines.Should().ContainKey(1);
        map.LeftLines[1].IntraLineSpans.Should().NotBeNull().And.NotBeEmpty();
        map.RightLines[1].IntraLineSpans.Should().NotBeNull().And.NotBeEmpty();
    }

    [Fact]
    public void FromHunks_IntraLineDisabled_NeverEmitsModified()
    {
        // Intra-line off ⇒ FromHunks must never produce yellow, regardless
        // of how viable the positional pair would be. Yellow without
        // inner red/green spans gives the user a "changed" signal with no
        // indication of where. "hello world" / "hello WORLD" is a
        // high-similarity pair (would be Modified under intra-line on)
        // that here stays as a plain Deleted+Inserted pair.
        var hunks = _diff.ComputeDiff("hello world", "hello WORLD", new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: false, ignoreWhitespace: false);

        map.LeftLines[1].Kind.Should().Be(DiffLineKind.Deleted);
        map.RightLines[1].Kind.Should().Be(DiffLineKind.Inserted);
        map.LeftLines[1].IntraLineSpans.Should().BeNull();
        map.RightLines[1].IntraLineSpans.Should().BeNull();
        map.LeftLines.Values.Should().NotContain(h => h.Kind == DiffLineKind.Modified);
        map.RightLines.Values.Should().NotContain(h => h.Kind == DiffLineKind.Modified);
    }

    [Fact]
    public void FromHunks_UnequalDeletesAndInserts_PairsThenSpills()
    {
        // Validates the pairing-then-spill walk under intra-line ON: D=3,
        // I=1 in this block ('a','b','c' deletes vs single 'e' insert,
        // shared 'd' is context). The 'a'/'e' positional pair fails
        // IsPairingViable (zero overlap on single-token sides) and demotes
        // to unpaired Deleted/Inserted, then 'b' and 'c' spill as Deleted.
        var hunks = _diff.ComputeDiff("a\nb\nc\nd", "e\nd", new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: true, ignoreWhitespace: false);

        map.LeftLines[1].Kind.Should().Be(DiffLineKind.Deleted);
        map.LeftLines[2].Kind.Should().Be(DiffLineKind.Deleted);
        map.LeftLines[3].Kind.Should().Be(DiffLineKind.Deleted);
        map.RightLines[1].Kind.Should().Be(DiffLineKind.Inserted);
    }

    [Fact]
    public void FromHunks_UnequalDeletesAndInserts_IntraLineDisabled_AllUnpaired()
    {
        // Same input as above but with intra-line off: no pairing happens
        // at all, so the structure of which lines are Deleted vs Inserted
        // is the same as the demote path — every delete is Deleted, every
        // insert is Inserted. No Modified anywhere.
        var hunks = _diff.ComputeDiff("a\nb\nc\nd", "e\nd", new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: false, ignoreWhitespace: false);

        map.LeftLines[1].Kind.Should().Be(DiffLineKind.Deleted);
        map.LeftLines[2].Kind.Should().Be(DiffLineKind.Deleted);
        map.LeftLines[3].Kind.Should().Be(DiffLineKind.Deleted);
        map.RightLines[1].Kind.Should().Be(DiffLineKind.Inserted);
        map.LeftLines.Values.Should().NotContain(h => h.Kind == DiffLineKind.Modified);
        map.RightLines.Values.Should().NotContain(h => h.Kind == DiffLineKind.Modified);
    }

    [Fact]
    public void FromHunks_IntraLineSpans_ColumnsAreLineRelative()
    {
        var hunks = _diff.ComputeDiff("alpha beta", "alpha gamma", new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: true, ignoreWhitespace: false);

        var leftSpans = map.LeftLines[1].IntraLineSpans!;
        var rightSpans = map.RightLines[1].IntraLineSpans!;

        leftSpans.All(s => s.StartColumn >= 6).Should().BeTrue();
        rightSpans.All(s => s.StartColumn >= 6).Should().BeTrue();
        leftSpans.All(s => s.Kind == IntraLineSpanKind.Deleted).Should().BeTrue();
        rightSpans.All(s => s.Kind == IntraLineSpanKind.Inserted).Should().BeTrue();
    }

    [Fact]
    public void FromHunks_IntraLineEnabled_LowSimilarityPair_DemotesToUnpairedDeleteAndInsert()
    {
        // Two paired lines that share only whitespace and a handful of
        // delimiters ("//", parens, "the"/"so") fail the pairing-viability
        // policy. FromHunks demotes both sides to unpaired Deleted /
        // Inserted so the rendered red/green signal matches the
        // algorithm's "these lines aren't really paired" conclusion —
        // instead of yellow-on-both-sides with no character-level signal,
        // which is the bug this fix addresses.
        const string oldLine = "// rect(s) are present so the user can find the marker the";
        const string newLine = "// (left rect + ribbon + right rect) as a single polygon so it";
        var hunks = _diff.ComputeDiff(oldLine, newLine, new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: true, ignoreWhitespace: false);

        // Demoted to unpaired Deleted (left) / Inserted (right) — no yellow.
        map.LeftLines[1].Kind.Should().Be(DiffLineKind.Deleted);
        map.RightLines[1].Kind.Should().Be(DiffLineKind.Inserted);
        // Demote path uses null spans (IntraLineColorizer treats null and
        // empty equivalently).
        map.LeftLines[1].IntraLineSpans.Should().BeNull();
        map.RightLines[1].IntraLineSpans.Should().BeNull();
    }

    [Fact]
    public void FromHunks_IntraLineDisabled_LowSimilarityPair_RendersAsDeleteAndInsert()
    {
        // Mirror of the LowSimilarityPair_DemotesToUnpairedDeleteAndInsert
        // test above, but with intra-line off. The viability check is
        // skipped entirely (no pairing happens) so the outcome is the same
        // unpaired Deleted/Inserted shape — yellow without inner spans is
        // unhelpful no matter how the result would have scored.
        const string oldLine = "// rect(s) are present so the user can find the marker the";
        const string newLine = "// (left rect + ribbon + right rect) as a single polygon so it";
        var hunks = _diff.ComputeDiff(oldLine, newLine, new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: false, ignoreWhitespace: false);

        map.LeftLines[1].Kind.Should().Be(DiffLineKind.Deleted);
        map.RightLines[1].Kind.Should().Be(DiffLineKind.Inserted);
        map.LeftLines[1].IntraLineSpans.Should().BeNull();
        map.RightLines[1].IntraLineSpans.Should().BeNull();
    }

    [Fact]
    public void FromHunks_IntraLineEnabled_LowSimilarityWithUnpairedDeleteSpill_DemotesPairedAndKeepsSpill()
    {
        // Synthesized hunk with D=3, I=1 so we control the pairing exactly.
        // The single insert pairs positionally with the first delete, that
        // pair fails the similarity check and demotes to Deleted/Inserted,
        // and the remaining two deletes spill as Deleted unchanged.
        var hunk = new DiffHunk(
            OldStartLine: 1, OldLineCount: 3,
            NewStartLine: 1, NewLineCount: 1,
            Lines: new[]
            {
                new DiffLine(DiffLineKind.Deleted,  OldLineNumber: 1, NewLineNumber: null, Text: "alpha original"),
                new DiffLine(DiffLineKind.Deleted,  OldLineNumber: 2, NewLineNumber: null, Text: "beta original"),
                new DiffLine(DiffLineKind.Deleted,  OldLineNumber: 3, NewLineNumber: null, Text: "gamma original"),
                new DiffLine(DiffLineKind.Inserted, OldLineNumber: null, NewLineNumber: 1, Text: "completely different new line"),
            },
            FunctionContext: null);

        var map = DiffHighlightMap.FromHunks(new[] { hunk }, _diff, enableIntraLine: true, ignoreWhitespace: false);

        // Pair (line 1 left, line 1 right) demoted.
        map.LeftLines[1].Kind.Should().Be(DiffLineKind.Deleted);
        map.LeftLines[1].IntraLineSpans.Should().BeNull();
        map.RightLines[1].Kind.Should().Be(DiffLineKind.Inserted);
        map.RightLines[1].IntraLineSpans.Should().BeNull();
        // Spill: lines 2 and 3 on the left stay Deleted unchanged.
        map.LeftLines[2].Kind.Should().Be(DiffLineKind.Deleted);
        map.LeftLines[3].Kind.Should().Be(DiffLineKind.Deleted);
    }

    [Fact]
    public void FromHunks_IntraLineEnabled_LowSimilarityWithUnpairedInsertSpill_DemotesPairedAndKeepsSpill()
    {
        // Mirror of the delete-spill case: D=1, I=3. The single delete
        // pairs positionally with the first insert, that pair demotes,
        // and the remaining two inserts spill as Inserted unchanged.
        var hunk = new DiffHunk(
            OldStartLine: 1, OldLineCount: 1,
            NewStartLine: 1, NewLineCount: 3,
            Lines: new[]
            {
                new DiffLine(DiffLineKind.Deleted,  OldLineNumber: 1, NewLineNumber: null, Text: "alpha original"),
                new DiffLine(DiffLineKind.Inserted, OldLineNumber: null, NewLineNumber: 1, Text: "completely different new line"),
                new DiffLine(DiffLineKind.Inserted, OldLineNumber: null, NewLineNumber: 2, Text: "second new line"),
                new DiffLine(DiffLineKind.Inserted, OldLineNumber: null, NewLineNumber: 3, Text: "third new line"),
            },
            FunctionContext: null);

        var map = DiffHighlightMap.FromHunks(new[] { hunk }, _diff, enableIntraLine: true, ignoreWhitespace: false);

        // Pair (line 1 left, line 1 right) demoted.
        map.LeftLines[1].Kind.Should().Be(DiffLineKind.Deleted);
        map.LeftLines[1].IntraLineSpans.Should().BeNull();
        map.RightLines[1].Kind.Should().Be(DiffLineKind.Inserted);
        map.RightLines[1].IntraLineSpans.Should().BeNull();
        // Spill: lines 2 and 3 on the right stay Inserted unchanged.
        map.RightLines[2].Kind.Should().Be(DiffLineKind.Inserted);
        map.RightLines[3].Kind.Should().Be(DiffLineKind.Inserted);
    }

    [Fact]
    public void FromHunks_IntraLineEnabled_MultiPairBlockWithMixedSimilarity_DemotesOnlyFailingPairs()
    {
        // Synthesized D=3 / I=3 block. Pair 0 (line 1) and pair 2 (line 3)
        // share most of their content and pass the similarity check; pair 1
        // (line 2) shares no real content and fails. The fix demotes ONLY
        // the failing pair — not the whole block — so passing pairs keep
        // their Modified stamp + intra-line spans. This pins the per-pair
        // (not per-block) demote behavior.
        var hunk = new DiffHunk(
            OldStartLine: 1, OldLineCount: 3,
            NewStartLine: 1, NewLineCount: 3,
            Lines: new[]
            {
                new DiffLine(DiffLineKind.Deleted,  OldLineNumber: 1, NewLineNumber: null, Text: "foo bar baz"),
                new DiffLine(DiffLineKind.Deleted,  OldLineNumber: 2, NewLineNumber: null, Text: "port = 8080"),
                new DiffLine(DiffLineKind.Deleted,  OldLineNumber: 3, NewLineNumber: null, Text: "alpha beta gamma"),
                new DiffLine(DiffLineKind.Inserted, OldLineNumber: null, NewLineNumber: 1, Text: "foo XYZ baz"),
                new DiffLine(DiffLineKind.Inserted, OldLineNumber: null, NewLineNumber: 2, Text: "// removed port config entirely"),
                new DiffLine(DiffLineKind.Inserted, OldLineNumber: null, NewLineNumber: 3, Text: "alpha beta delta"),
            },
            FunctionContext: null);

        var map = DiffHighlightMap.FromHunks(new[] { hunk }, _diff, enableIntraLine: true, ignoreWhitespace: false);

        // Pair 0 (lines 1) — high similarity, stays Modified with spans.
        map.LeftLines[1].Kind.Should().Be(DiffLineKind.Modified);
        map.LeftLines[1].IntraLineSpans.Should().NotBeNull().And.NotBeEmpty();
        map.RightLines[1].Kind.Should().Be(DiffLineKind.Modified);
        map.RightLines[1].IntraLineSpans.Should().NotBeNull().And.NotBeEmpty();

        // Pair 1 (lines 2) — low similarity, demoted.
        map.LeftLines[2].Kind.Should().Be(DiffLineKind.Deleted);
        map.LeftLines[2].IntraLineSpans.Should().BeNull();
        map.RightLines[2].Kind.Should().Be(DiffLineKind.Inserted);
        map.RightLines[2].IntraLineSpans.Should().BeNull();

        // Pair 2 (lines 3) — high similarity, stays Modified with spans.
        map.LeftLines[3].Kind.Should().Be(DiffLineKind.Modified);
        map.LeftLines[3].IntraLineSpans.Should().NotBeNull().And.NotBeEmpty();
        map.RightLines[3].Kind.Should().Be(DiffLineKind.Modified);
        map.RightLines[3].IntraLineSpans.Should().NotBeNull().And.NotBeEmpty();
    }

    [Fact]
    public void FromHunks_IntraLineEnabled_SingleTokenCaseChange_DemotesToUnpairedDeleteAndInsert()
    {
        // Per the design principle (show the differences honestly, don't
        // pass judgment about which differences deserve attention), short
        // single-token edits with zero token overlap demote uniformly. The
        // case-only edit "beta" -> "BETA" produces matched=0, oldTokens=1,
        // newTokens=1, ratio=0 — the fix demotes rather than holding
        // ground for short tokens. A future case-insensitive comparison
        // option would change `beta` ≡ `BETA` upstream of the similarity
        // check; that's the right place to address it, not a downstream
        // paternalism gate here.
        var hunks = _diff.ComputeDiff("beta", "BETA", new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: true, ignoreWhitespace: false);

        map.LeftLines[1].Kind.Should().Be(DiffLineKind.Deleted);
        map.LeftLines[1].IntraLineSpans.Should().BeNull();
        map.RightLines[1].Kind.Should().Be(DiffLineKind.Inserted);
        map.RightLines[1].IntraLineSpans.Should().BeNull();
    }

    [Fact]
    public void FromHunks_IntraLineEnabled_HighSimilarityPair_KeepsSpans()
    {
        // Two paired lines that share most of their content (identical
        // indentation, identical return keyword, only the value differs)
        // should keep intra-line spans so the user sees the change at a
        // glance.
        var hunks = _diff.ComputeDiff(
            "        return null;",
            "        return Empty;",
            new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: true, ignoreWhitespace: false);

        map.LeftLines[1].IntraLineSpans.Should().NotBeNull().And.NotBeEmpty();
        map.RightLines[1].IntraLineSpans.Should().NotBeNull().And.NotBeEmpty();
    }

    [Fact]
    public void FromHunks_IntraLineEnabled_OldFullyContainedInNew_KeepsSpans()
    {
        // The old line's content is fully preserved in the new line — only
        // a trailing comment was appended. Intra-line should fire AND only
        // the truly appended content (after the original line ends) should
        // be highlighted on the new side. The chunker merges `";` (old)
        // and `";  ` (new) into different delimiter chunks; without
        // post-processing the boundary leaks into the highlight.
        const string oldLine = "                toVersion = \"v9\";";
        const string newLine = "                toVersion = \"v9\";  // a long appended comment that is much longer than the original line";
        var hunks = _diff.ComputeDiff(oldLine, newLine, new DiffOptions()).Hunks;

        var map = DiffHighlightMap.FromHunks(hunks, _diff, enableIntraLine: true, ignoreWhitespace: false);

        var rightSpans = map.RightLines[1].IntraLineSpans;
        rightSpans.Should().NotBeNull().And.NotBeEmpty();
        // The first inserted span must start at or after the end of the
        // shared old-line content — anything earlier means the chunker
        // boundary leaked into the highlight.
        rightSpans!.Min(s => s.StartColumn).Should().BeGreaterThanOrEqualTo(oldLine.Length);
        // And the highlight must extend to the very end of the new line.
        rightSpans!.Max(s => s.EndColumn).Should().Be(newLine.Length);

        // Old side has nothing actually deleted.
        map.LeftLines[1].IntraLineSpans.Should().NotBeNull();
        map.LeftLines[1].IntraLineSpans!.Should().BeEmpty();
    }
}
