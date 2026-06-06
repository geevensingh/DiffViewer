using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using MdContainerInline = Markdig.Syntax.Inlines.ContainerInline;
using MdTable = Markdig.Extensions.Tables.Table;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;

namespace DiffViewer.Rendering;

/// <summary>
/// Turns two raw markdown strings into a single <see cref="FlowDocument"/>
/// that renders both, with red/green decoration on the differences.
///
/// <para><b>Threading:</b> UI-thread only. <see cref="FlowDocument"/> and
/// its descendants (<see cref="Paragraph"/>, <see cref="Run"/>,
/// <see cref="Hyperlink"/>, etc.) are
/// <see cref="System.Windows.Threading.DispatcherObject"/>s with thread
/// affinity to the thread that created them. Construct the result on the
/// dispatcher and bind to it from the same dispatcher. The parsing /
/// diffing work is pure CPU and could be moved to a background thread in
/// the future, but the <see cref="FlowDocument"/> assembly itself must
/// run on the UI thread.</para>
///
/// <para>Pipeline:
/// <list type="number">
///   <item>Parse both blobs with Markdig.</item>
///   <item>Unfold each document into a flat sequence of block units
///   (top-level blocks, plus per-item entries for list items at any depth).</item>
///   <item>Hand-rolled LCS on the (kind, text) keys to produce a sequence
///   of Equal/Delete/Insert ops.</item>
///   <item>Coalesce adjacent same-kind Delete + Insert pairs into Replace
///   when token-LCS similarity passes the gate, so paragraphs with
///   inline-only edits show word-by-word diff instead of two red/green
///   block tints.</item>
///   <item>Render each op to one or more FlowDocument blocks. Replace ops
///   produce a single paragraph with token-level word-diff that preserves
///   inline formatting (bold/italic/code/hyperlinks) on unchanged
///   sub-runs.</item>
/// </list></para>
///
/// <para>Lifted verbatim from
/// <c>spikes/MarkdownDiffSpike/MarkdownDiffRenderer.cs</c> with only the
/// namespace and one missing <c>.Freeze()</c> call changed; see
/// <c>spikes/MarkdownDiffSpike/FINDINGS.md</c> on the spike branch for
/// the verdict and the four caveats (move detection, context-blind keys,
/// similarity threshold, table fallback).</para>
/// </summary>
internal static class MarkdownDiffRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static readonly Brush DeletedBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0xE0));
    private static readonly Brush InsertedBg = new SolidColorBrush(Color.FromRgb(0xE0, 0xFF, 0xE0));
    private static readonly Brush DeletedFg = new SolidColorBrush(Color.FromRgb(0xA0, 0x00, 0x00));
    private static readonly Brush InsertedFg = new SolidColorBrush(Color.FromRgb(0x00, 0x50, 0x00));
    private static readonly Brush CodeBg = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF4));
    private static readonly Brush HrBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
    private static readonly Brush QuoteBorder = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));

    static MarkdownDiffRenderer()
    {
        DeletedBg.Freeze(); InsertedBg.Freeze();
        DeletedFg.Freeze(); InsertedFg.Freeze();
        CodeBg.Freeze(); HrBrush.Freeze(); QuoteBorder.Freeze();
        UrlChangedFg.Freeze();
    }

    public static FlowDocument Render(string oldText, string newText)
    {
        var oldDoc = Markdown.Parse(oldText, Pipeline);
        var newDoc = Markdown.Parse(newText, Pipeline);

        var oldBlocks = Unfold(oldDoc);
        var newBlocks = Unfold(newDoc);

        var ops = Diff(oldBlocks, newBlocks);
        ops = CoalesceReplaces(ops);

        var fd = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            PagePadding = new Thickness(24, 16, 24, 16),
            ColumnWidth = double.PositiveInfinity,
        };

        foreach (var op in ops)
        {
            foreach (var block in RenderOp(op))
                fd.Blocks.Add(block);
        }

        return fd;
    }

    // ---------- unfolding ----------

    private enum BlockKind { Heading, Paragraph, Code, ListItem, Quote, ThematicBreak, Table, Unknown }

    /// <summary>
    /// One unit of diff. <see cref="Source"/> is the original Markdig block
    /// (for rendering); <see cref="Key"/> is the comparison key for LCS.
    /// For list items we capture extra ordering context so the renderer can
    /// reconstruct bullet/number prefixes.
    /// </summary>
    private sealed record DiffBlock(string Key, BlockKind Kind, MdBlock Source, ListContext? List = null);

    private sealed record ListContext(bool Ordered, int Index, int StartFrom, int Depth);

    private static List<DiffBlock> Unfold(MarkdownDocument doc)
    {
        var result = new List<DiffBlock>();
        foreach (var block in doc)
            UnfoldBlock(block, result);
        return result;
    }

    private static void UnfoldBlock(MdBlock block, List<DiffBlock> sink)
    {
        switch (block)
        {
            case HeadingBlock h:
                sink.Add(new DiffBlock(
                    Key: $"h{h.Level}|{Normalize(InlineText(h.Inline))}",
                    Kind: BlockKind.Heading,
                    Source: h));
                break;

            case ParagraphBlock p:
                sink.Add(new DiffBlock(
                    Key: $"p|{Normalize(InlineText(p.Inline))}",
                    Kind: BlockKind.Paragraph,
                    Source: p));
                break;

            case FencedCodeBlock fc:
                sink.Add(new DiffBlock(
                    Key: $"code|{fc.Info}|{Normalize(LeafContent(fc))}",
                    Kind: BlockKind.Code,
                    Source: fc));
                break;

            case CodeBlock cb:
                sink.Add(new DiffBlock(
                    Key: $"code||{Normalize(LeafContent(cb))}",
                    Kind: BlockKind.Code,
                    Source: cb));
                break;

            case ListBlock list:
                UnfoldList(list, depth: 0, sink);
                break;

            case QuoteBlock quote:
                sink.Add(new DiffBlock(
                    Key: $"q|{Normalize(BlockText(quote))}",
                    Kind: BlockKind.Quote,
                    Source: quote));
                break;

            case ThematicBreakBlock tb:
                sink.Add(new DiffBlock(Key: "hr", Kind: BlockKind.ThematicBreak, Source: tb));
                break;

            case MdTable table:
                sink.Add(new DiffBlock(
                    Key: $"table|{Normalize(BlockText(table))}",
                    Kind: BlockKind.Table,
                    Source: table));
                break;

            default:
                sink.Add(new DiffBlock(
                    Key: $"u|{block.GetType().Name}|{Normalize(BlockText(block))}",
                    Kind: BlockKind.Unknown,
                    Source: block));
                break;
        }
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool prevWs = false;
        foreach (char c in s)
        {
            bool ws = char.IsWhiteSpace(c);
            if (ws)
            {
                if (!prevWs) sb.Append(' ');
                prevWs = true;
            }
            else
            {
                sb.Append(c);
                prevWs = false;
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Unfolds a (possibly nested) ListBlock into a flat sequence of
    /// per-item DiffBlocks. Each item contributes one DiffBlock keyed on
    /// its own first-paragraph text (NOT its descendants). Nested ListBlock
    /// children produce their own DiffBlocks immediately after the parent,
    /// at depth+1. This lets the diff algorithm match inner and outer
    /// changes independently, instead of treating any inner edit as a
    /// whole-outer-item rewrite.
    /// </summary>
    private static void UnfoldList(ListBlock list, int depth, List<DiffBlock> sink)
    {
        int i = 0;
        int startFrom = list.IsOrdered && int.TryParse(list.OrderedStart, out int s) ? s : 1;
        foreach (var item in list)
        {
            if (item is not ListItemBlock lib) continue;
            var ctx = new ListContext(list.IsOrdered, i, startFrom, depth);

            // Own-text key uses only the first paragraph child, so a change
            // to a nested item does not invalidate the outer item's match.
            string ownText = Normalize(ItemOwnText(lib));
            sink.Add(new DiffBlock(
                Key: $"li|{(list.IsOrdered ? "o" : "u")}|d{depth}|{ownText}",
                Kind: BlockKind.ListItem,
                Source: lib,
                List: ctx));

            // Recurse into any nested ListBlocks. Other child block types
            // (extra paragraphs, code blocks, blockquotes inside an item)
            // are out of spike scope and silently dropped from the diff
            // view, matching the prior behaviour for multi-paragraph items.
            foreach (var child in lib)
            {
                if (child is ListBlock nested)
                    UnfoldList(nested, depth + 1, sink);
            }

            i++;
        }
    }

    private static string ItemOwnText(ListItemBlock lib)
    {
        var first = lib.OfType<ParagraphBlock>().FirstOrDefault();
        return first?.Inline is null ? string.Empty : InlineText(first.Inline);
    }

    // ---------- LCS diff ----------

    private abstract record Op;
    private sealed record EqualOp(DiffBlock Old, DiffBlock New) : Op;
    private sealed record DeleteOp(DiffBlock Old) : Op;
    private sealed record InsertOp(DiffBlock New) : Op;
    private sealed record ReplaceOp(DiffBlock Old, DiffBlock New) : Op;

    private static List<Op> Diff(List<DiffBlock> oldBlocks, List<DiffBlock> newBlocks)
    {
        int n = oldBlocks.Count, m = newBlocks.Count;

        // Standard dynamic-programming LCS table.
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                dp[i, j] = oldBlocks[i].Key == newBlocks[j].Key
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var ops = new List<Op>();
        int oi = 0, ni = 0;
        while (oi < n && ni < m)
        {
            if (oldBlocks[oi].Key == newBlocks[ni].Key)
            {
                ops.Add(new EqualOp(oldBlocks[oi], newBlocks[ni]));
                oi++; ni++;
            }
            else if (dp[oi + 1, ni] >= dp[oi, ni + 1])
            {
                ops.Add(new DeleteOp(oldBlocks[oi]));
                oi++;
            }
            else
            {
                ops.Add(new InsertOp(newBlocks[ni]));
                ni++;
            }
        }
        while (oi < n) ops.Add(new DeleteOp(oldBlocks[oi++]));
        while (ni < m) ops.Add(new InsertOp(newBlocks[ni++]));
        return ops;
    }

    /// <summary>
    /// Walks the op stream and pairs adjacent runs of Delete+Insert of
    /// matching kind into Replace ops. Limited to paragraphs, headings,
    /// and list-items — the kinds where inline-level diff is meaningful.
    /// Excess on either side falls back to plain Delete / Insert.
    /// </summary>
    private static List<Op> CoalesceReplaces(List<Op> ops)
    {
        var output = new List<Op>(ops.Count);
        int i = 0;
        while (i < ops.Count)
        {
            int delStart = i;
            while (i < ops.Count && ops[i] is DeleteOp) i++;
            int delEnd = i;
            int insStart = i;
            while (i < ops.Count && ops[i] is InsertOp) i++;
            int insEnd = i;

            int delCount = delEnd - delStart;
            int insCount = insEnd - insStart;

            if (delCount == 0 && insCount == 0)
            {
                output.Add(ops[i]);
                i++;
                continue;
            }

            // Greedy: pair Delete[k] with Insert[k] if they share a kind
            // we care about AND have enough token overlap to make
            // word-diff readable. Below threshold, fall back to plain
            // Delete + Insert so the user sees a clean two-block "old
            // replaced with new" view instead of alternating red/green
            // word fragments. Anything left over from either run stays
            // as-is.
            int paired = 0;
            int maxPair = Math.Min(delCount, insCount);
            for (int k = 0; k < maxPair; k++)
            {
                var d = (DeleteOp)ops[delStart + k];
                var n = (InsertOp)ops[insStart + k];
                if (IsReplaceCandidate(d.Old, n.New) && PassesReplaceSimilarity(d.Old, n.New))
                {
                    output.Add(new ReplaceOp(d.Old, n.New));
                    paired = k + 1;
                }
                else
                {
                    break;
                }
            }
            for (int k = paired; k < delCount; k++) output.Add(ops[delStart + k]);
            for (int k = paired; k < insCount; k++) output.Add(ops[insStart + k]);
        }
        return output;
    }

    private static bool IsReplaceCandidate(DiffBlock oldBlock, DiffBlock newBlock)
    {
        if (oldBlock.Kind != newBlock.Kind) return false;
        return oldBlock.Kind is BlockKind.Paragraph or BlockKind.Heading or BlockKind.ListItem;
    }

    // ---------- replace similarity gate ----------

    /// <summary>
    /// Fraction of tokens (on the longer side) that must match between
    /// the two blocks for the coalesce step to produce a Replace pair.
    /// Below this, the pair stays as separate Delete + Insert ops so the
    /// renderer emits clean full-block tinted blocks instead of
    /// alternating red/green word fragments. 0.30 is a defensible
    /// starting point — high enough that nearly-rewritten paragraphs
    /// fall to full-block rendering, low enough that a paragraph with
    /// 1 word edited out of 4 still gets word-diff. Empirical tuning
    /// is a real-integration concern, not a spike one.
    /// </summary>
    private const double MinReplaceSimilarity = 0.30;

    /// <summary>
    /// Short blocks (max non-whitespace token count strictly less than
    /// this) bypass the similarity gate entirely and always coalesce.
    /// Justification: a Replace of two single-word list items
    /// ("Spinach" -> "Broccoli") renders as one tidy
    /// "[strike]Spinach Broccoli" line; splitting it into two list
    /// rows would lose the "this is one swap" framing for no visual
    /// gain. The noise the gate exists to prevent only kicks in once a
    /// block is long enough to make alternating fragments hard to
    /// read.
    /// </summary>
    private const int ShortBlockExemption = 5;

    private static bool PassesReplaceSimilarity(DiffBlock oldBlock, DiffBlock newBlock)
    {
        var oldTokens = TokenizeBlock(oldBlock).Where(t => !IsWhitespaceText(t.Text)).ToList();
        var newTokens = TokenizeBlock(newBlock).Where(t => !IsWhitespaceText(t.Text)).ToList();

        int maxLen = Math.Max(oldTokens.Count, newTokens.Count);
        if (maxLen < ShortBlockExemption) return true;

        // Empty (with the other side non-empty) means zero overlap on a
        // long block. Don't coalesce.
        if (oldTokens.Count == 0 || newTokens.Count == 0) return false;

        int lcs = ComputeTokenLcsLength(oldTokens, newTokens);
        return (double)lcs / maxLen >= MinReplaceSimilarity;
    }

    private static bool IsWhitespaceText(string s) =>
        !string.IsNullOrEmpty(s) && s.All(char.IsWhiteSpace);

    /// <summary>
    /// LCS length over a non-whitespace token sequence. Equality is the
    /// same (text + format) check the render-time token diff uses, so
    /// the similarity score and the render output stay consistent —
    /// the gate doesn't claim "match" for a pair the render will then
    /// fail to anchor.
    /// </summary>
    private static int ComputeTokenLcsLength(List<InlineToken> oldT, List<InlineToken> newT)
    {
        int n = oldT.Count, m = newT.Count;
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                dp[i, j] = TokensMatch(oldT[i], newT[j])
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }
        return dp[0, 0];
    }

    // ---------- rendering ----------

    private enum Mode { Normal, Deleted, Inserted }

    private static IEnumerable<WpfBlock> RenderOp(Op op) => op switch
    {
        // For text-eligible kinds, route Equal pairs through the token-diff
        // path too — that way URL-only changes inside an otherwise-Equal
        // hyperlink (text identical, URL differs) get the orange tint +
        // tooltip from BuildTokenInline, instead of silently rendering as
        // a normal blue hyperlink with the new URL.
        EqualOp e when IsTokenEligible(e.New.Kind) => RenderReplace(e.Old, e.New),
        EqualOp e => RenderBlockShape(e.New, Mode.Normal),
        DeleteOp d => RenderBlockShape(d.Old, Mode.Deleted),
        InsertOp i => RenderBlockShape(i.New, Mode.Inserted),
        ReplaceOp r => RenderReplace(r.Old, r.New),
        _ => Array.Empty<WpfBlock>(),
    };

    private static bool IsTokenEligible(BlockKind kind) =>
        kind is BlockKind.Paragraph or BlockKind.Heading or BlockKind.ListItem;

    private static IEnumerable<WpfBlock> RenderBlockShape(DiffBlock db, Mode mode)
    {
        switch (db.Kind)
        {
            case BlockKind.Heading:
            {
                var h = (HeadingBlock)db.Source;
                var headingPara = new Paragraph
                {
                    FontSize = HeadingSize(h.Level),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 12, 0, 6),
                };
                foreach (var inl in BuildInlinesFrom(h.Inline, mode))
                    headingPara.Inlines.Add(inl);
                yield return Decorate(headingPara, mode, fullBlock: true);
                break;
            }

            case BlockKind.Paragraph:
            {
                var p = (ParagraphBlock)db.Source;
                var paraPara = new Paragraph
                {
                    Margin = new Thickness(0, 4, 0, 4),
                };
                foreach (var inl in BuildInlinesFrom(p.Inline, mode))
                    paraPara.Inlines.Add(inl);
                yield return Decorate(paraPara, mode, fullBlock: true);
                break;
            }

            case BlockKind.Code:
            {
                string content = LeafContent((LeafBlock)db.Source);
                string? lang = db.Source is FencedCodeBlock fc ? fc.Info : null;
                var para = new Paragraph
                {
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Background = mode == Mode.Normal ? CodeBg : BackgroundFor(mode),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 6, 0, 6),
                };
                if (!string.IsNullOrEmpty(lang))
                {
                    var langRun = new Run("[" + lang + "]\n")
                    {
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    };
                    para.Inlines.Add(langRun);
                }
                var bodyRun = new Run(content);
                if (mode == Mode.Deleted) bodyRun.TextDecorations = TextDecorations.Strikethrough;
                para.Inlines.Add(bodyRun);
                yield return para;
                break;
            }

            case BlockKind.ListItem:
            {
                var lib = (ListItemBlock)db.Source;
                var ctx = db.List;
                string prefix = ctx is { Ordered: true }
                    ? $"{ctx.StartFrom + ctx.Index}. "
                    : "• ";

                var para = new Paragraph
                {
                    Margin = new Thickness(20 + (ctx?.Depth ?? 0) * 24, 2, 0, 2),
                    TextIndent = -16,
                };
                para.Inlines.Add(new Run(prefix) { FontWeight = FontWeights.Normal });

                // The DiffBlock keys/diffs against this item's OWN
                // first-paragraph text only (see UnfoldList). Nested
                // ListBlock children are emitted as separate DiffBlocks
                // and render below this one with their own depth context,
                // so we deliberately do not recurse into them here.
                ParagraphBlock? firstPara = lib.OfType<ParagraphBlock>().FirstOrDefault();
                if (firstPara is not null)
                {
                    foreach (var inl in BuildInlinesFrom(firstPara.Inline, mode))
                        para.Inlines.Add(inl);
                }
                yield return Decorate(para, mode, fullBlock: true);
                break;
            }

            case BlockKind.Quote:
            {
                var qb = (QuoteBlock)db.Source;
                foreach (var child in qb)
                {
                    if (child is ParagraphBlock cp)
                    {
                        var para = new Paragraph
                        {
                            BorderBrush = QuoteBorder,
                            BorderThickness = new Thickness(3, 0, 0, 0),
                            Padding = new Thickness(8, 2, 0, 2),
                            Margin = new Thickness(0, 4, 0, 4),
                            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                        };
                        foreach (var inl in BuildInlinesFrom(cp.Inline, mode))
                            para.Inlines.Add(inl);
                        yield return Decorate(para, mode, fullBlock: true);
                    }
                    else
                    {
                        // Nested non-paragraph content in a blockquote falls
                        // back to plain text — spike limitation.
                        yield return Decorate(new Paragraph(new Run(BlockText(child))), mode, fullBlock: true);
                    }
                }
                break;
            }

            case BlockKind.ThematicBreak:
            {
                yield return new BlockUIContainer(new System.Windows.Controls.Border
                {
                    BorderBrush = HrBrush,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Margin = new Thickness(0, 8, 0, 8),
                });
                break;
            }

            case BlockKind.Table:
            {
                // Plain-text fallback. Documented in FINDINGS.md.
                var para = new Paragraph
                {
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Margin = new Thickness(0, 6, 0, 6),
                };
                para.Inlines.Add(new Run(BlockText(db.Source)));
                yield return Decorate(para, mode, fullBlock: true);
                break;
            }

            default:
            {
                yield return Decorate(new Paragraph(new Run(BlockText(db.Source))), mode, fullBlock: true);
                break;
            }
        }
    }

    private static double HeadingSize(int level) => level switch
    {
        1 => 26, 2 => 22, 3 => 19, 4 => 17, 5 => 15, _ => 14,
    };

    private static WpfBlock Decorate(Paragraph p, Mode mode, bool fullBlock)
    {
        if (mode == Mode.Normal) return p;
        if (fullBlock)
        {
            p.Background = BackgroundFor(mode);
            if (mode == Mode.Deleted)
            {
                p.TextDecorations = TextDecorations.Strikethrough;
                p.Foreground = DeletedFg;
            }
            else
            {
                p.Foreground = InsertedFg;
            }
        }
        return p;
    }

    private static Brush BackgroundFor(Mode mode) =>
        mode == Mode.Deleted ? DeletedBg : InsertedBg;

    // ---------- inline (in-paragraph) rendering ----------

    private static IEnumerable<WpfInline> BuildInlinesFrom(MdContainerInline? container, Mode mode)
    {
        if (container is null) yield break;
        foreach (var inl in container)
        {
            foreach (var wpf in BuildInline(inl, mode))
                yield return wpf;
        }
    }

    private static IEnumerable<WpfInline> BuildInline(MdInline inline, Mode mode)
    {
        switch (inline)
        {
            case LiteralInline lit:
                yield return new Run(lit.Content.ToString());
                break;

            case EmphasisInline em:
            {
                var span = new Span();
                foreach (var child in em)
                    foreach (var wpf in BuildInline(child, mode))
                        span.Inlines.Add(wpf);
                // Markdig encodes ** as DelimiterCount==2; * as 1.
                if (em.DelimiterCount >= 2) span.FontWeight = FontWeights.Bold;
                else span.FontStyle = FontStyles.Italic;
                yield return span;
                break;
            }

            case CodeInline code:
                yield return new Run(code.Content)
                {
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Background = CodeBg,
                };
                break;

            case LinkInline link:
            {
                // Render link text. For URL-only changes (#04 sample),
                // visible text is unchanged — by design, we don't fake a
                // visible difference. Honest about what's rendered.
                var hyper = new Hyperlink
                {
                    NavigateUri = TryUri(link.Url),
                    ToolTip = link.Url,
                };
                foreach (var child in link)
                    foreach (var wpf in BuildInline(child, mode))
                        hyper.Inlines.Add(wpf);
                yield return hyper;
                break;
            }

            case LineBreakInline:
                yield return new LineBreak();
                break;

            case AutolinkInline auto:
                yield return new Hyperlink(new Run(auto.Url))
                {
                    NavigateUri = TryUri(auto.Url),
                };
                break;

            default:
                // Fall back to whatever text content the inline exposes.
                yield return new Run(inline is MdContainerInline ci ? InlineText(ci) : inline.ToString() ?? string.Empty);
                break;
        }
    }

    private static Uri? TryUri(string? url) =>
        !string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : null;

    // ---------- replace = format-preserving token-level inline diff ----------

    /// <summary>
    /// Renders a Replace pair as a single paragraph with token-level
    /// (word-granularity) diff. Improvement over the v1 flatten-to-text
    /// approach: each token carries its original inline formatting
    /// (bold / italic / inline-code / hyperlink), so unchanged sub-runs
    /// of a changed paragraph keep their original rendering. Hyperlink
    /// URL changes inside an otherwise-Equal token surface as an orange
    /// tint plus a tooltip — the rendered text is honestly unchanged,
    /// but the metadata difference is visible enough to notice.
    /// </summary>
    private static IEnumerable<WpfBlock> RenderReplace(DiffBlock oldBlock, DiffBlock newBlock)
    {
        var oldTokens = TokenizeBlock(oldBlock);
        var newTokens = TokenizeBlock(newBlock);
        var ops = DiffTokens(oldTokens, newTokens);
        var inlines = RenderTokenOps(ops).ToList();

        switch (oldBlock.Kind)
        {
            case BlockKind.Heading:
            {
                var h = (HeadingBlock)newBlock.Source;
                var para = new Paragraph
                {
                    FontSize = HeadingSize(h.Level),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 12, 0, 6),
                };
                foreach (var inl in inlines) para.Inlines.Add(inl);
                yield return para;
                break;
            }

            case BlockKind.ListItem:
            {
                var ctx = newBlock.List;
                string prefix = ctx is { Ordered: true }
                    ? $"{ctx.StartFrom + ctx.Index}. "
                    : "• ";
                var para = new Paragraph
                {
                    Margin = new Thickness(20 + (ctx?.Depth ?? 0) * 24, 2, 0, 2),
                    TextIndent = -16,
                };
                para.Inlines.Add(new Run(prefix));
                foreach (var inl in inlines) para.Inlines.Add(inl);
                yield return para;
                break;
            }

            default: // Paragraph
            {
                var para = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };
                foreach (var inl in inlines) para.Inlines.Add(inl);
                yield return para;
                break;
            }
        }
    }

    // ---------- tokenization ----------

    /// <summary>
    /// Inline-formatting bits a token carries through the diff. Stored as
    /// flags so a token can be (bold + italic + code) etc. Used both as
    /// part of the token-equality key and to drive render-time decoration.
    /// </summary>
    [Flags]
    private enum InlineFormat
    {
        None = 0,
        Bold = 1,
        Italic = 2,
        Code = 4,
    }

    /// <summary>
    /// One word (or one whitespace run) extracted from a paragraph's
    /// inline tree, plus the formatting context it was rendered under.
    /// <see cref="LinkUrl"/> is non-null when the token was inside a
    /// markdown link — see the URL-change handling in
    /// <see cref="BuildTokenInline"/>.
    /// </summary>
    private sealed record InlineToken(string Text, InlineFormat Format, string? LinkUrl);

    private static List<InlineToken> TokenizeBlock(DiffBlock db)
    {
        var tokens = new List<InlineToken>();
        switch (db.Source)
        {
            case LeafBlock lb when lb.Inline is not null:
                TokenizeInline(lb.Inline, InlineFormat.None, null, tokens);
                break;
            case ListItemBlock lib:
            {
                var first = lib.OfType<ParagraphBlock>().FirstOrDefault();
                if (first?.Inline is not null)
                    TokenizeInline(first.Inline, InlineFormat.None, null, tokens);
                break;
            }
        }
        return tokens;
    }

    private static void TokenizeInline(MdInline inline, InlineFormat format, string? linkUrl, List<InlineToken> sink)
    {
        switch (inline)
        {
            case LiteralInline lit:
                foreach (var word in SplitWords(lit.Content.ToString()))
                    sink.Add(new InlineToken(word, format, linkUrl));
                break;

            case CodeInline code:
                foreach (var word in SplitWords(code.Content))
                    sink.Add(new InlineToken(word, format | InlineFormat.Code, linkUrl));
                break;

            case EmphasisInline em:
            {
                // Markdig encodes ** (bold) as DelimiterCount==2; * (italic)
                // as 1. We OR into the inherited format so nested
                // bold+italic survives.
                var childFormat = format | (em.DelimiterCount >= 2 ? InlineFormat.Bold : InlineFormat.Italic);
                foreach (var child in em)
                    TokenizeInline(child, childFormat, linkUrl, sink);
                break;
            }

            case LinkInline link:
                foreach (var child in link)
                    TokenizeInline(child, format, link.Url, sink);
                break;

            case LineBreakInline:
                sink.Add(new InlineToken(" ", format, linkUrl));
                break;

            case AutolinkInline auto:
                sink.Add(new InlineToken(auto.Url, format, auto.Url));
                break;

            case MdContainerInline cont:
                foreach (var child in cont)
                    TokenizeInline(child, format, linkUrl, sink);
                break;
        }
    }

    /// <summary>
    /// Splits text into alternating runs of non-whitespace and whitespace.
    /// Round-tripping all runs concatenated reproduces the input exactly,
    /// so word-level diffing without losing space placement just works.
    /// Treats CLI-flag-style tokens ("--short") as one word, which is
    /// usually what the reader wants.
    /// </summary>
    private static IEnumerable<string> SplitWords(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        int start = 0;
        bool inWs = char.IsWhiteSpace(text[0]);
        for (int i = 1; i < text.Length; i++)
        {
            bool ws = char.IsWhiteSpace(text[i]);
            if (ws != inWs)
            {
                yield return text.Substring(start, i - start);
                start = i;
                inWs = ws;
            }
        }
        yield return text.Substring(start);
    }

    // ---------- token diff (LCS) ----------

    private abstract record TokenOp;
    private sealed record TokenEqual(InlineToken Old, InlineToken New) : TokenOp;
    private sealed record TokenDelete(InlineToken Token) : TokenOp;
    private sealed record TokenInsert(InlineToken Token) : TokenOp;

    private static List<TokenOp> DiffTokens(List<InlineToken> oldT, List<InlineToken> newT)
    {
        int n = oldT.Count, m = newT.Count;
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                dp[i, j] = TokensMatch(oldT[i], newT[j])
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var ops = new List<TokenOp>();
        int oi = 0, ni = 0;
        while (oi < n && ni < m)
        {
            if (TokensMatch(oldT[oi], newT[ni]))
            {
                ops.Add(new TokenEqual(oldT[oi], newT[ni]));
                oi++; ni++;
            }
            else if (dp[oi + 1, ni] >= dp[oi, ni + 1])
            {
                ops.Add(new TokenDelete(oldT[oi++]));
            }
            else
            {
                ops.Add(new TokenInsert(newT[ni++]));
            }
        }
        while (oi < n) ops.Add(new TokenDelete(oldT[oi++]));
        while (ni < m) ops.Add(new TokenInsert(newT[ni++]));
        return ops;
    }

    /// <summary>
    /// Equality used by the token LCS: text + formatting bits match, URL
    /// is ignored. A token whose only difference is its URL still pairs
    /// as Equal; the URL change is surfaced at render time (tint + tooltip)
    /// instead of as a delete+insert pair.
    /// </summary>
    private static bool TokensMatch(InlineToken a, InlineToken b) =>
        a.Text == b.Text && a.Format == b.Format;

    // ---------- token rendering ----------

    private static IEnumerable<WpfInline> RenderTokenOps(IEnumerable<TokenOp> ops)
    {
        foreach (var op in ops)
        {
            switch (op)
            {
                case TokenEqual eq:
                    yield return BuildTokenInline(eq.New, Mode.Normal, oldUrl: eq.Old.LinkUrl);
                    break;
                case TokenDelete del:
                    yield return BuildTokenInline(del.Token, Mode.Deleted, oldUrl: null);
                    break;
                case TokenInsert ins:
                    yield return BuildTokenInline(ins.Token, Mode.Inserted, oldUrl: null);
                    break;
            }
        }
    }

    private static readonly Brush UrlChangedFg = new SolidColorBrush(Color.FromRgb(0xCC, 0x66, 0x00));

    private static WpfInline BuildTokenInline(InlineToken t, Mode mode, string? oldUrl)
    {
        var run = new Run(t.Text);

        // Format-derived styling first; diff-derived styling layers on top.
        if ((t.Format & InlineFormat.Bold) != 0) run.FontWeight = FontWeights.Bold;
        if ((t.Format & InlineFormat.Italic) != 0) run.FontStyle = FontStyles.Italic;
        bool isCode = (t.Format & InlineFormat.Code) != 0;
        if (isCode)
        {
            run.FontFamily = new FontFamily("Consolas");
            run.FontSize = 12;
        }

        // Diff backgrounds overwrite the code background — the diff signal
        // is more important than the "this is inline code" cue, and the
        // monospace + bold still distinguish a deleted/inserted code token
        // from surrounding prose.
        if (mode == Mode.Deleted)
        {
            run.Background = DeletedBg;
            run.Foreground = DeletedFg;
            run.TextDecorations = TextDecorations.Strikethrough;
        }
        else if (mode == Mode.Inserted)
        {
            run.Background = InsertedBg;
            run.Foreground = InsertedFg;
        }
        else if (isCode)
        {
            run.Background = CodeBg;
        }

        if (t.LinkUrl is null) return run;

        var hyper = new Hyperlink(run) { NavigateUri = TryUri(t.LinkUrl) };
        bool urlChanged = oldUrl is not null && oldUrl != t.LinkUrl;
        if (urlChanged)
        {
            // Tint the link orange so URL-only changes are visible at a
            // glance, not buried in a tooltip. Honest disclosure that
            // something changed even though the rendered text didn't.
            hyper.Foreground = UrlChangedFg;
            hyper.ToolTip = $"URL changed.\nNow: {t.LinkUrl}\nWas: {oldUrl}";
        }
        else
        {
            hyper.ToolTip = t.LinkUrl;
        }
        return hyper;
    }

    // ---------- text extraction (still used by Unfold for block keys) ----------

    private static string InlineText(MdContainerInline? container)
    {
        if (container is null) return string.Empty;
        var sb = new StringBuilder();
        AppendInlineText(container, sb);
        return sb.ToString();
    }

    private static void AppendInlineText(MdInline inline, StringBuilder sb)
    {
        switch (inline)
        {
            case LiteralInline lit: sb.Append(lit.Content.ToString()); break;
            case CodeInline code: sb.Append(code.Content); break;
            case AutolinkInline auto: sb.Append(auto.Url); break;
            case LineBreakInline: sb.Append(' '); break;
            case MdContainerInline container:
                foreach (var child in container)
                    AppendInlineText(child, sb);
                break;
        }
    }

    private static string LeafContent(LeafBlock lb)
    {
        if (lb.Lines.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        for (int i = 0; i < lb.Lines.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(lb.Lines.Lines[i].ToString());
        }
        return sb.ToString();
    }

    private static string BlockText(MdBlock block)
    {
        var sb = new StringBuilder();
        AppendBlockText(block, sb);
        return sb.ToString().Trim();
    }

    private static void AppendBlockText(MdBlock block, StringBuilder sb)
    {
        switch (block)
        {
            case LeafBlock lb when lb.Inline is not null:
                AppendInlineText(lb.Inline, sb);
                sb.Append('\n');
                break;
            case LeafBlock lb:
                sb.Append(LeafContent(lb));
                sb.Append('\n');
                break;
            case ContainerBlock cb:
                foreach (var child in cb)
                    AppendBlockText(child, sb);
                break;
        }
    }
}
