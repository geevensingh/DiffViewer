namespace DiffViewer.Models;

/// <summary>
/// Image-diff layout mode for <see cref="DiffViewer.ViewModels.ImageDiffViewModel"/>.
/// Surfaced as a 3-button radio group in the diff toolbar when the
/// current file is dispatched to the image pane (issue #9).
///
/// <para><b>Side-by-side</b> — two image panes, fit-to-frame with aspect
/// preserved. Best for big differences or mismatched dimensions.</para>
///
/// <para><b>Swipe</b> — single composite canvas with a draggable
/// divider; the left side of the divider shows the LEFT image at the
/// same position, the right side shows the RIGHT image. Best for
/// pixel-aligned subtle differences.</para>
///
/// <para><b>Onion-skin</b> — both images stacked at the same fitted
/// position with an opacity slider that blends left ↔ right. Best for
/// spotting positional shifts and color casts.</para>
///
/// <para>A fourth "Difference" mode (per-pixel |new - old|) was
/// considered and dropped for v1 — see the plan for issue #9.</para>
/// </summary>
public enum ImageDiffMode
{
    SideBySide,
    Swipe,
    OnionSkin,
}
