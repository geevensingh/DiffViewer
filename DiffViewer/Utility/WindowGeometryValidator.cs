using DiffViewer.Models;

namespace DiffViewer.Utility;

/// <summary>
/// Pure helper that decides whether a saved <see cref="WindowStateSnapshot"/>
/// can be safely applied to <see cref="System.Windows.Window"/> on this
/// launch. The check is intentionally framework-free (takes only the
/// virtual-screen rect as primitives) so it is fully unit-testable
/// without spinning up WPF.
///
/// <para>The validator answers exactly one question: "given the saved
/// geometry and the current virtual screen, is the window guaranteed to
/// be reachable by the user?" If not, the caller should fall back to
/// the built-in defaults. The saved snapshot is <i>not</i> cleared on a
/// failed validation — the user may simply have unplugged a monitor;
/// next launch with it re-attached should restore correctly.</para>
/// </summary>
internal static class WindowGeometryValidator
{
    /// <summary>
    /// Minimum horizontal and vertical overlap, in DIPs, between the
    /// saved window rect and the virtual screen for the geometry to be
    /// considered reachable.
    /// </summary>
    public const double MinVisibleSize = 100.0;

    /// <summary>
    /// Approximate title-bar height in DIPs. The top strip of the
    /// window of this height must intersect the virtual screen, or the
    /// user cannot grab the window to move it.
    /// </summary>
    public const double TitleBarStripHeight = 30.0;

    /// <summary>
    /// Minimum window size that we will attempt to apply. Saved values
    /// below this are treated as garbage and rejected.
    /// </summary>
    public const double MinWindowDimension = 100.0;

    /// <summary>
    /// Resolve a saved <paramref name="snapshot"/> against the current
    /// virtual screen. Returns the snapshot unchanged if it is safe to
    /// apply, or <c>null</c> if the caller should fall back to defaults.
    /// </summary>
    /// <param name="snapshot">The saved state, or <c>null</c> for no saved state.</param>
    /// <param name="virtualScreenLeft">
    /// <see cref="System.Windows.SystemParameters.VirtualScreenLeft"/>.
    /// Bounding-box origin X of all attached monitors.
    /// </param>
    /// <param name="virtualScreenTop">
    /// <see cref="System.Windows.SystemParameters.VirtualScreenTop"/>.
    /// Bounding-box origin Y of all attached monitors.
    /// </param>
    /// <param name="virtualScreenWidth">
    /// <see cref="System.Windows.SystemParameters.VirtualScreenWidth"/>.
    /// </param>
    /// <param name="virtualScreenHeight">
    /// <see cref="System.Windows.SystemParameters.VirtualScreenHeight"/>.
    /// </param>
    public static WindowStateSnapshot? Resolve(
        WindowStateSnapshot? snapshot,
        double virtualScreenLeft,
        double virtualScreenTop,
        double virtualScreenWidth,
        double virtualScreenHeight)
    {
        if (snapshot is null) return null;

        if (!IsFinite(snapshot.Left) || !IsFinite(snapshot.Top) ||
            !IsFinite(snapshot.Width) || !IsFinite(snapshot.Height))
        {
            return null;
        }

        if (snapshot.Width < MinWindowDimension || snapshot.Height < MinWindowDimension)
        {
            return null;
        }

        if (virtualScreenWidth <= 0 || virtualScreenHeight <= 0)
        {
            // No usable virtual screen reported - bail out safely.
            return null;
        }

        double winLeft = snapshot.Left;
        double winTop = snapshot.Top;
        double winRight = winLeft + snapshot.Width;
        double winBottom = winTop + snapshot.Height;

        double vsRight = virtualScreenLeft + virtualScreenWidth;
        double vsBottom = virtualScreenTop + virtualScreenHeight;

        double overlapWidth = Math.Min(winRight, vsRight) - Math.Max(winLeft, virtualScreenLeft);
        double overlapHeight = Math.Min(winBottom, vsBottom) - Math.Max(winTop, virtualScreenTop);

        if (overlapWidth < MinVisibleSize || overlapHeight < MinVisibleSize)
        {
            return null;
        }

        // Title-bar strip (top TitleBarStripHeight DIPs of the window)
        // must intersect the virtual screen so the user can grab it.
        double titleTop = winTop;
        double titleBottom = winTop + TitleBarStripHeight;
        double titleVerticalOverlap = Math.Min(titleBottom, vsBottom) - Math.Max(titleTop, virtualScreenTop);
        double titleHorizontalOverlap = Math.Min(winRight, vsRight) - Math.Max(winLeft, virtualScreenLeft);
        if (titleVerticalOverlap <= 0 || titleHorizontalOverlap <= 0)
        {
            return null;
        }

        return snapshot;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
