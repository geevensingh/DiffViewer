namespace DiffViewer.Services;

/// <summary>
/// Seam over <see cref="System.Windows.Clipboard"/>'s static API so
/// view-models can be unit-tested without spinning up a WPF dispatcher
/// (and without touching the user's real clipboard).
///
/// <para>Production implementation: <see cref="WpfClipboardService"/>.
/// Tests substitute a recording fake that captures the last-set text.
/// View-models accept this as an optional dependency; a null clipboard
/// makes the copy command a safe no-op (matching the
/// <c>ConfirmHandler</c> / <c>ToastHandler</c> null-handler convention
/// already established on <see cref="ViewModels.MainViewModel"/>).</para>
/// </summary>
public interface IClipboardService
{
    /// <summary>Write <paramref name="text"/> to the system clipboard.</summary>
    void SetText(string text);
}
