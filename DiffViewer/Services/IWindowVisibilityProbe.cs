namespace DiffViewer.Services;

/// <summary>
/// Probes whether the main window is currently visible to the user.
/// The <see cref="IPullRequestWatcher"/> uses this to pause polling
/// when the window is minimized or hidden — saving GitHub API quota
/// and battery while the user has the app stashed in the background.
///
/// <para>"Visible" is intentionally fuzzy: a minimized window, a
/// hidden window, and a window whose <see cref="System.Windows.Window.IsVisible"/>
/// flips to <c>false</c> all count as "not visible". A window that's
/// merely covered by another app stays visible — the cost of tracking
/// real occlusion is much higher than the benefit, and an unfocused
/// window is still doing work the user might glance at.</para>
///
/// <para>Implementations are thread-safe to read but
/// <see cref="VisibilityChanged"/> may fire on any thread; subscribers
/// are responsible for marshalling work they care about back to the
/// UI thread.</para>
/// </summary>
public interface IWindowVisibilityProbe
{
    /// <summary>True when the main window is currently visible to the user.</summary>
    bool IsVisible { get; }

    /// <summary>
    /// Raised when <see cref="IsVisible"/> transitions. Sender is the
    /// probe instance; args carry no payload — re-read
    /// <see cref="IsVisible"/> for the new value.
    /// </summary>
    event EventHandler? VisibilityChanged;
}
