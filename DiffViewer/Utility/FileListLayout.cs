namespace DiffViewer.Utility;

/// <summary>
/// Layout constants and helpers for the left pane that holds the recents
/// bar and the file list. The pane's width is persisted in
/// <see cref="DiffViewer.Models.AppSettings.FileListPaneWidthPixels"/>;
/// these constants are the single source of truth for the min / max
/// bounds, shared between the XAML's <c>MinWidth</c> attribute and the
/// code-behind that loads / saves the value.
/// </summary>
public static class FileListLayout
{
    /// <summary>
    /// Lower bound for the file-list pane width in device-independent
    /// pixels. Below this, the recents dropdown and file list become
    /// unusably narrow (text truncated past the point of identifying a
    /// file). XAML's <c>MinWidth</c> on the file-list ColumnDefinition
    /// binds to this constant via <c>{x:Static}</c>.
    /// </summary>
    public const double MinFileListPaneWidthPixels = 200.0;

    /// <summary>
    /// Upper bound for the file-list pane width in device-independent
    /// pixels. Defensive ceiling: prevents a tampered settings.json or
    /// a settings file carried over from a much wider monitor from
    /// rendering the diff pane invisibly narrow. 2000 px clears all
    /// common monitor widths through 4K while still leaving the diff
    /// pane usable on smaller displays.
    /// </summary>
    public const double MaxFileListPaneWidthPixels = 2000.0;

    /// <summary>
    /// Default file-list pane width in device-independent pixels;
    /// matches the historical hardcoded XAML value before persistence
    /// was added so first-launch behaviour is unchanged. Also the
    /// fallback for NaN / Infinity / negative input.
    /// </summary>
    public const double DefaultFileListPaneWidthPixels = 320.0;

    /// <summary>
    /// Clamp <paramref name="width"/> into the supported range. NaN,
    /// Infinity, and negative inputs return
    /// <see cref="DefaultFileListPaneWidthPixels"/>; in-range values
    /// pass through unchanged.
    /// </summary>
    public static double ClampWidth(double width)
    {
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
        {
            return DefaultFileListPaneWidthPixels;
        }
        if (width < MinFileListPaneWidthPixels) return MinFileListPaneWidthPixels;
        if (width > MaxFileListPaneWidthPixels) return MaxFileListPaneWidthPixels;
        return width;
    }
}
