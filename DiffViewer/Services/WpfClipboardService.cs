using System.Windows;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="IClipboardService"/>: thin wrapper around
/// <see cref="Clipboard.SetText(string)"/>. Wrapped in a try/catch
/// because the Win32 clipboard can be locked by another process (the
/// classic "Cannot open clipboard" race); when it is, swallow the
/// exception and leave the clipboard unchanged rather than crashing
/// the app — a failed copy is a UX papercut, a crash is not.
/// </summary>
internal sealed class WpfClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        try
        {
            Clipboard.SetText(text ?? string.Empty);
        }
        catch
        {
            // Best-effort: clipboard may be locked by another process.
        }
    }
}
