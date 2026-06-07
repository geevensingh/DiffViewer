using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using DiffViewer.Rendering;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Rendering;

/// <summary>
/// Black-box tests for <see cref="MarkdownDiffRenderer"/>. The renderer
/// produces a WPF <see cref="FlowDocument"/>, so every test that touches
/// the output needs an STA apartment via <c>[StaFact]</c> — <see cref="FlowDocument"/>
/// and its descendants (<see cref="Paragraph"/>, <see cref="Run"/>,
/// <see cref="Hyperlink"/>) are dispatcher-affine.
///
/// <para>Tests assert structure of the output (block kinds, inline runs,
/// brushes, text content) without reaching into the renderer's private
/// types — <c>InternalsVisibleTo</c> doesn't expose <c>private</c>
/// nested members anyway, and the contract under test is the public
/// shape of the <see cref="FlowDocument"/>.</para>
///
/// <para>Sample matrix mirrors the 10 pairs from the spike's
/// <c>samples/</c> folder so the integration's behavior on each
/// caveat-demonstration case stays in sync with the spike's verdict.</para>
/// </summary>
public class MarkdownDiffRendererTests
{
    private static readonly Color DeletedBgColor = Color.FromRgb(0xFF, 0xE0, 0xE0);
    private static readonly Color InsertedBgColor = Color.FromRgb(0xE0, 0xFF, 0xE0);
    private static readonly Color UrlChangedFgColor = Color.FromRgb(0xCC, 0x66, 0x00);

    // ---------- baseline ----------

    [StaFact]
    public void Render_EmptyOldEmptyNew_ProducesEmptyDocument()
    {
        var doc = MarkdownDiffRenderer.Render("", "");
        doc.Should().NotBeNull();
        doc.Blocks.Should().BeEmpty();
    }

    [StaFact]
    public void Render_IdenticalInputs_ProducesNoDiffDecorations()
    {
        const string md = "# Title\n\nA paragraph.\n";

        var doc = MarkdownDiffRenderer.Render(md, md);

        // Should produce a heading + paragraph (plus possibly a trailing
        // empty Paragraph from the trailing newline; not the contract
        // under test). What IS the contract: nothing should be tinted.
        foreach (var p in doc.Blocks.OfType<Paragraph>())
        {
            ColorOf(p.Background).Should().NotBe(DeletedBgColor);
            ColorOf(p.Background).Should().NotBe(InsertedBgColor);
        }
        foreach (var run in AllRuns(doc))
        {
            ColorOf(run.Background).Should().NotBe(DeletedBgColor);
            ColorOf(run.Background).Should().NotBe(InsertedBgColor);
        }
    }

    // ---------- happy path ----------

    [StaFact]
    public void Render_HappyPath_ProducesHeadingPlusParagraphPlusList()
    {
        // Mirrors spike sample 01.
        const string oldText = "# Title\n\nThe quick brown fox.\n\n- Alpha\n- Beta\n";
        const string newText = "# Title v2\n\nThe quick brown ferret.\n\n- Alpha\n- Beta\n- Gamma\n";

        var doc = MarkdownDiffRenderer.Render(oldText, newText);

        doc.Blocks.OfType<Paragraph>().Should().NotBeEmpty(
            "the rendered output should include at least one block per source block");

        // A new list item appended => somewhere in the document, either a
        // full-block-tinted paragraph (Insert path) OR an insert-tinted
        // Run (token path via short-block exemption) should appear.
        bool anyInsertSignal =
            doc.Blocks.OfType<Paragraph>().Any(p => ColorOf(p.Background) == InsertedBgColor)
            || AllRuns(doc).Any(r => ColorOf(r.Background) == InsertedBgColor);

        anyInsertSignal.Should().BeTrue(
            "the new list item should appear with the insert tint somewhere in the output");
    }

    // ---------- caveat 1 fix: inline formatting preserved across diff ----------

    [StaFact]
    public void Render_InlineBoldEdit_PreservesBoldOnUnchangedTokens()
    {
        // Word-level edit inside a paragraph with bold text.
        const string oldText = "Read the **important warning** below.\n";
        const string newText = "Read the **critical warning** below.\n";

        var doc = MarkdownDiffRenderer.Render(oldText, newText);

        // "warning" survives unchanged; the bold MUST be preserved on it.
        var warningRun = AllRuns(doc).FirstOrDefault(r => r.Text == "warning");
        warningRun.Should().NotBeNull();
        warningRun!.FontWeight.Should().Be(FontWeights.Bold,
            "the v2 token pipeline preserves inline formatting on unchanged sub-runs");
    }

    [StaFact]
    public void Render_InlineCodeEdit_PreservesMonospaceOnChangedToken()
    {
        // Mirrors spike sample 03 idea: edit inside an inline-code span.
        const string oldText = "Use `git status` to inspect.\n";
        const string newText = "Use `git status --short` to inspect.\n";

        var doc = MarkdownDiffRenderer.Render(oldText, newText);

        // The unchanged "git" token should retain monospace (Consolas).
        var gitRun = AllRuns(doc).FirstOrDefault(r => r.Text == "git");
        gitRun.Should().NotBeNull();
        gitRun!.FontFamily.Source.Should().Be("Consolas",
            "inline-code formatting carries through the token diff");
    }

    // ---------- link rendering ----------

    [StaFact]
    public void Render_LinkTextChange_RendersAsHyperlink()
    {
        const string oldText = "See the [home page](https://example.com).\n";
        const string newText = "See the [homepage](https://example.com).\n";

        var doc = MarkdownDiffRenderer.Render(oldText, newText);

        var hyperlinks = AllInlines(doc).OfType<Hyperlink>().ToList();
        hyperlinks.Should().NotBeEmpty("link should render as a Hyperlink, not a plain Run");
    }

    [StaFact]
    public void Render_LinkUrlOnlyChange_TintsHyperlinkOrange()
    {
        // Same link text, different URL -> orange tint per the spike's
        // "honest about metadata changes" policy.
        const string oldText = "See the [docs](https://example.com/v1).\n";
        const string newText = "See the [docs](https://example.com/v2).\n";

        var doc = MarkdownDiffRenderer.Render(oldText, newText);

        var orange = AllInlines(doc).OfType<Hyperlink>().FirstOrDefault(h =>
            h.Foreground is SolidColorBrush b && b.Color == UrlChangedFgColor);

        orange.Should().NotBeNull(
            "URL-only change should surface as the orange-tinted Hyperlink, not silently disappear");
        orange!.ToolTip.Should().NotBeNull("tooltip should disclose both URLs");
        orange.ToolTip!.ToString().Should().Contain("v1").And.Contain("v2");
    }

    // ---------- caveat 2 fix: nested list per-item diff ----------

    [StaFact]
    public void Render_NestedListInnerItemEdit_DiffsAtItemGranularity()
    {
        // Outer items "Vegetables" / "Fruits" identical; inner edit only.
        const string oldText = "- Vegetables\n  - Carrots\n  - Spinach\n- Fruits\n  - Apples\n";
        const string newText = "- Vegetables\n  - Carrots\n  - Broccoli\n- Fruits\n  - Apples\n";

        var doc = MarkdownDiffRenderer.Render(oldText, newText);

        // "Carrots", "Apples" (and ideally "Vegetables", "Fruits") survive
        // unchanged: their runs should NOT be tinted.
        bool carrotsClean = AllRuns(doc).Where(r => r.Text == "Carrots").All(IsClean);
        bool applesClean = AllRuns(doc).Where(r => r.Text == "Apples").All(IsClean);
        bool vegetablesClean = AllRuns(doc).Where(r => r.Text == "Vegetables").All(IsClean);
        bool fruitsClean = AllRuns(doc).Where(r => r.Text == "Fruits").All(IsClean);

        carrotsClean.Should().BeTrue("a sibling item should not be marked as changed when only Spinach->Broccoli was edited");
        applesClean.Should().BeTrue("the unedited fruit should not be marked as changed");
        vegetablesClean.Should().BeTrue("the outer item should not be marked as changed when only an inner item was edited");
        fruitsClean.Should().BeTrue("the second outer item should not be marked as changed");

        // The actual edit IS visible somewhere.
        bool spinachStruck = AllRuns(doc).Any(r =>
            r.Text == "Spinach" && r.Background is SolidColorBrush b && b.Color == DeletedBgColor);
        bool broccoliInserted = AllRuns(doc).Any(r =>
            r.Text == "Broccoli" && r.Background is SolidColorBrush b && b.Color == InsertedBgColor);

        spinachStruck.Should().BeTrue("the deleted item should carry the delete tint");
        broccoliInserted.Should().BeTrue("the inserted item should carry the insert tint");
    }

    // ---------- known limitation: tables fall back to plain text ----------

    [StaFact]
    public void Render_TableCellChange_FallsBackToPlainTextBlock()
    {
        const string oldText = "| H |\n|---|\n| a |\n";
        const string newText = "| H |\n|---|\n| b |\n";

        var doc = MarkdownDiffRenderer.Render(oldText, newText);

        // Plain-text fallback means no WPF Table element in the output.
        doc.Blocks.OfType<Table>().Should().BeEmpty(
            "tables intentionally render as plain-text Paragraphs in this version");
    }

    // ---------- caveat 3c fix: similarity gate prevents word-diff noise ----------

    [StaFact]
    public void Render_HeavyRewriteWithNoSharedWords_FallsBackToTwoFullBlockTints()
    {
        // Same heading anchors the position; the paragraph has zero token
        // overlap so similarity gate (MinReplaceSimilarity = 0.30) should
        // refuse to coalesce into a Replace.
        const string oldText = "## Lookups\n\nUse indexes on frequently queried columns to speed up lookups.\n";
        const string newText = "## Lookups\n\nMaterialized views shine when aggregations rarely change.\n";

        var doc = MarkdownDiffRenderer.Render(oldText, newText);

        // Similarity-gate fallback => the deleted paragraph and the inserted
        // paragraph each render as a SEPARATE block with the tint on the
        // PARAGRAPH (not on its descendant Runs).
        int redParagraphs = doc.Blocks.OfType<Paragraph>().Count(p =>
            ColorOf(p.Background) == DeletedBgColor);
        int greenParagraphs = doc.Blocks.OfType<Paragraph>().Count(p =>
            ColorOf(p.Background) == InsertedBgColor);

        redParagraphs.Should().BeGreaterOrEqualTo(1,
            "the deleted paragraph should be a single full-block red tint, not interleaved word fragments");
        greenParagraphs.Should().BeGreaterOrEqualTo(1,
            "the inserted paragraph should be a single full-block green tint, not interleaved word fragments");

        // And there should be NO single paragraph that contains both a
        // deleted-Run and an inserted-Run — that would mean the
        // coalesce-into-Replace happened anyway.
        bool anyMixedParagraph = doc.Blocks.OfType<Paragraph>().Any(p =>
        {
            var runs = AllRunsIn(p).ToList();
            bool hasDeleteRun = runs.Any(r => ColorOf(r.Background) == DeletedBgColor);
            bool hasInsertRun = runs.Any(r => ColorOf(r.Background) == InsertedBgColor);
            return hasDeleteRun && hasInsertRun;
        });
        anyMixedParagraph.Should().BeFalse(
            "similarity gate should keep delete and insert in SEPARATE paragraphs for zero-overlap rewrites");
    }

    [StaFact]
    public void Render_SmallListItemReplace_StaysCoalescedViaShortBlockExemption()
    {
        // "Spinach" vs "Broccoli" share zero tokens but are below the
        // short-block-exemption threshold, so they should still coalesce
        // into a single tidy inline Replace.
        const string oldText = "- Spinach\n";
        const string newText = "- Broccoli\n";

        var doc = MarkdownDiffRenderer.Render(oldText, newText);

        // Output is one list item paragraph with one strike Run + one
        // insert Run (in addition to the bullet prefix Run).
        doc.Blocks.OfType<Paragraph>().Should().HaveCount(1,
            "short-block exemption keeps a single-token Replace as one inline line, not split into two list rows");

        var runs = AllRunsIn(doc.Blocks.OfType<Paragraph>().Single()).ToList();
        runs.Any(r => r.Text == "Spinach" && r.Background is SolidColorBrush b && b.Color == DeletedBgColor)
            .Should().BeTrue();
        runs.Any(r => r.Text == "Broccoli" && r.Background is SolidColorBrush b && b.Color == InsertedBgColor)
            .Should().BeTrue();
    }

    // ---------- caveat 3a known limitation: move detection ----------

    [StaFact]
    public void Render_ReorderedSections_RenderAsSeparateDeleteAndInsert()
    {
        // Three sections, last two swapped. No move-detection in LCS, so
        // this renders as delete + insert separated by the unchanged
        // middle. Test pins this behavior so a future move-detection
        // change is intentional, not silent.
        const string oldText = "# T\n\n## A\nFirst.\n\n## B\nSecond.\n\n## C\nThird.\n";
        const string newText = "# T\n\n## A\nFirst.\n\n## C\nThird.\n\n## B\nSecond.\n";

        var doc = MarkdownDiffRenderer.Render(oldText, newText);

        // Full-block tints from Delete/Insert ops land on the Paragraph,
        // not on its descendant Runs.
        bool anyDeleteParagraph = doc.Blocks.OfType<Paragraph>()
            .Any(p => ColorOf(p.Background) == DeletedBgColor);
        bool anyInsertParagraph = doc.Blocks.OfType<Paragraph>()
            .Any(p => ColorOf(p.Background) == InsertedBgColor);

        anyDeleteParagraph.Should().BeTrue(
            "moved section's old position should produce at least one full-block-tinted delete paragraph");
        anyInsertParagraph.Should().BeTrue(
            "moved section's new position should produce at least one full-block-tinted insert paragraph");
    }

    // ---------- helpers ----------

    private static IEnumerable<Run> AllRuns(FlowDocument doc) =>
        doc.Blocks.SelectMany(AllRunsIn);

    private static IEnumerable<Run> AllRunsIn(Block block)
    {
        if (block is Paragraph p)
            return AllInlinesUnder(p.Inlines).OfType<Run>();
        if (block is Section s)
            return s.Blocks.SelectMany(AllRunsIn);
        return Array.Empty<Run>();
    }

    private static IEnumerable<Inline> AllInlines(FlowDocument doc) =>
        doc.Blocks.SelectMany(AllInlinesIn);

    private static IEnumerable<Inline> AllInlinesIn(Block block)
    {
        if (block is Paragraph p) return AllInlinesUnder(p.Inlines);
        if (block is Section s) return s.Blocks.SelectMany(AllInlinesIn);
        return Array.Empty<Inline>();
    }

    private static IEnumerable<Inline> AllInlinesUnder(InlineCollection inlines)
    {
        foreach (var inl in inlines)
        {
            yield return inl;
            if (inl is Span span)
            {
                foreach (var child in AllInlinesUnder(span.Inlines))
                    yield return child;
            }
        }
    }

    private static Color? ColorOf(Brush? brush) =>
        brush is SolidColorBrush scb ? scb.Color : (Color?)null;

    private static bool IsClean(Run run) =>
        run.Background is null
        || (ColorOf(run.Background) != DeletedBgColor
            && ColorOf(run.Background) != InsertedBgColor);
}
