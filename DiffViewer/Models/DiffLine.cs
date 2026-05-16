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
    /// for two lines that were paired positionally (via <c>min(D, I)</c>) AND
    /// that the pairing-viability policy
    /// (<c>DiffHighlightMap.IsPairingViable</c>) corroborated. When the
    /// similarity policy rejects a positional pair,
    /// <see cref="Rendering.DiffHighlightMap.FromHunks"/> demotes both sides
    /// to unpaired <see cref="Deleted"/> / <see cref="Inserted"/> instead, so
    /// the rendered red/green signal matches the algorithm's "these lines
    /// aren't really paired" conclusion.</para>
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
