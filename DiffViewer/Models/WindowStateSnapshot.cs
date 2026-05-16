namespace DiffViewer.Models;

/// <summary>
/// Persisted main-window geometry. <see cref="Left"/>/<see cref="Top"/>/
/// <see cref="Width"/>/<see cref="Height"/> are always the *restore*
/// bounds (i.e. the size/position the window will un-maximize to);
/// <see cref="IsMaximized"/> is set when the window was last shown in
/// the maximized state.
///
/// <para>Values are WPF device-independent pixels (DIPs). Left/Top can
/// be negative on multi-monitor setups where a secondary monitor is to
/// the left of, or above, the primary.</para>
///
/// <para>A <c>null</c> instance on <see cref="AppSettings.WindowState"/>
/// means "no saved state" — the window opens at the built-in defaults
/// (1200×800, <see cref="System.Windows.WindowStartupLocation.CenterScreen"/>).</para>
/// </summary>
public sealed record WindowStateSnapshot(
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsMaximized);
