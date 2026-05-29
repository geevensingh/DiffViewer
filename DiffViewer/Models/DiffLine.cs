namespace DiffViewer.Models;

/// <summary>
/// One line in a diff hunk. <see cref="OldLineNumber"/> / <see cref="NewLineNumber"/>
/// are 1-based line numbers in the original buffers; either is <c>null</c> for
/// pure inserts/deletes respectively.
/// </summary>
public sealed record DiffLine(
    DiffLineKind Kind,
    int? OldLineNumber,
    int? NewLineNumber,
    string Text);

public enum DiffLineKind
{
    Context,
    Inserted,
    Deleted,
    /// <summary>
    /// Line that exists on both sides with intra-line modifications.
    ///
    /// <para>Produced only by <see cref="Rendering.DiffHighlightMap.FromHunks"/>
    /// when intra-line diff is enabled AND the pair clears the
    /// pairing-viability policy (<c>DiffHighlightMap.IsPairingViable</c>).
    /// Both conditions are required:</para>
    /// <list type="bullet">
    /// <item><description>Intra-line disabled ⇒ no pairing at all; every
    /// change is <see cref="Deleted"/>+<see cref="Inserted"/>. Yellow without
    /// inner red/green spans gives the user a "changed" signal with no
    /// indication of <em>where</em>, so the renderer is never asked to
    /// produce that state.</description></item>
    /// <item><description>Intra-line enabled but pair non-viable ⇒
    /// demoted to <see cref="Deleted"/>+<see cref="Inserted"/> for the same
    /// reason — the spans would highlight essentially everything, which is
    /// indistinguishable from a plain delete+insert.</description></item>
    /// </list>
    ///
    /// <para><see cref="Services.IDiffService"/> never emits
    /// <see cref="Modified"/> on raw <see cref="DiffLine"/>s — every change
    /// is expressed as a <see cref="Deleted"/>+<see cref="Inserted"/> pair.
    /// Defensive <c>case DiffLineKind.Modified</c> arms in code paths that
    /// consume raw DiffPlex output (HunkOverviewBar, UnifiedDiffFormatter)
    /// are dead today by that contract.</para>
    /// </summary>
    Modified,
}
