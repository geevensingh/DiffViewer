using System.Windows.Media;
using DiffViewer.Models;

namespace DiffViewer.Rendering;

/// <summary>
/// Resolves the per-line background brush both
/// <see cref="DiffBackgroundRenderer"/> (side-by-side) and
/// <see cref="InlineDiffBackgroundRenderer"/> (inline) paint.
///
/// <para>The rule is uniform across both modes: <em>any line with no
/// intra-line spans gets the strong intra-line brush; lines with spans
/// get the soft modified tint and the colorizer paints the strong
/// spans on top of it</em>. By contract, <see cref="DiffHighlightMap"/>
/// emits <see cref="DiffLineKind.Modified"/> only when intra-line
/// analysis produced spans, and emits <see cref="DiffLineKind.Deleted"/>
/// / <see cref="DiffLineKind.Inserted"/> with null spans in every other
/// case (pure delete/insert, intra-line off, demote path). The
/// selector keys off <see cref="DiffLineKind"/> alone because that
/// contract makes spans-vs-no-spans equivalent to
/// Modified-vs-Deleted/Inserted.</para>
///
/// <para>Why strong-when-no-spans: the strong brush says "this whole
/// rectangle is what changed"; the soft brush only makes sense when
/// the colorizer is about to overlay strong sub-spans that narrow the
/// claim ("here within the line"). Painting soft without overlay leaves
/// the user with a faint "something changed" signal with no indication
/// of where — which is the bug this rule fixes for the no-spans cases.</para>
/// </summary>
internal static class LineBackgroundBrushSelector
{
    public static Brush Pick(DiffSide side, DiffLineKind kind, DiffColorScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);

        return (side, kind) switch
        {
            // Strong full-line tints for pure inserts/deletes and for
            // paired-but-no-spans (intra-line off or demote path).
            (DiffSide.Left, DiffLineKind.Deleted) => scheme.RemovedIntraLineBackground,
            (DiffSide.Right, DiffLineKind.Inserted) => scheme.AddedIntraLineBackground,
            (DiffSide.Inline, DiffLineKind.Deleted) => scheme.RemovedIntraLineBackground,
            (DiffSide.Inline, DiffLineKind.Inserted) => scheme.AddedIntraLineBackground,

            // Soft yellow for Modified — the colorizer paints strong
            // red/green spans on top so the yellow shows through only in
            // the unchanged-within-the-line gaps.
            (_, DiffLineKind.Modified) => scheme.ModifiedLineBackground,

            // Context lines and anything else: no tint.
            _ => Brushes.Transparent,
        };
    }
}
