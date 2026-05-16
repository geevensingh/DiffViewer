namespace DiffViewer.Models;

/// <summary>
/// Which side(s) of the side-by-side diff view are visible. Driven by
/// the toolbar's Left / Both / Right radio-style toggle group.
/// <see cref="Both"/> is the default and the historical (single-mode)
/// behavior; the other two values collapse one side's column to width 0
/// so the visible side fills the available space.
/// </summary>
/// <remarks>
/// Only applies in side-by-side layout. The toolbar disables the
/// toggle group when the user switches to inline mode.
/// </remarks>
public enum DiffSideVisibility
{
    /// <summary>Both sides visible (the default).</summary>
    Both,

    /// <summary>Only the left (old / pre-change) side is visible.</summary>
    LeftOnly,

    /// <summary>Only the right (new / post-change) side is visible.</summary>
    RightOnly,
}
