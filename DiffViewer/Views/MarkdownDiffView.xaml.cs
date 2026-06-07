using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace DiffViewer.Views;

/// <summary>
/// Code-behind for <see cref="MarkdownDiffView"/>. The view is mostly
/// XAML wiring (a <see cref="FlowDocumentScrollViewer"/> bound to
/// <see cref="DiffViewer.ViewModels.MarkdownDiffViewModel.Document"/>),
/// plus the one piece of imperative work WPF demands: routing
/// <see cref="Hyperlink.RequestNavigate"/> to the default browser.
///
/// <para><b>Why the handler is needed.</b>
/// <see cref="Hyperlink"/> with <see cref="Hyperlink.NavigateUri"/> set
/// only auto-navigates when the document is hosted in a
/// <see cref="System.Windows.Navigation.NavigationWindow"/> or
/// <see cref="System.Windows.Controls.Frame"/>. A plain
/// <see cref="FlowDocumentScrollViewer"/> raises the
/// <see cref="Hyperlink.RequestNavigateEvent"/> routed event but doesn't
/// act on it, so clicks would look interactive (cursor changes, link is
/// underlined) and do nothing. The
/// <see cref="MarkdownDiffRenderer"/> emits real <see cref="Hyperlink"/>
/// elements (including the orange URL-changed variant whose tooltip
/// shows both old and new URLs), so navigation has to work for the
/// rendered view to be useful.</para>
/// </summary>
public partial class MarkdownDiffView : UserControl
{
    public MarkdownDiffView()
    {
        InitializeComponent();

        // Bubbled routed event — attaching at the UserControl level
        // catches every Hyperlink in the hosted FlowDocument without
        // needing per-link wiring.
        AddHandler(Hyperlink.RequestNavigateEvent,
            new RequestNavigateEventHandler(OnHyperlinkRequestNavigate));
    }

    private static void OnHyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        // Mirrors BrowserNotifyUpdateService.OpenUrlInDefaultBrowser:
        // UseShellExecute = true lets the OS resolve http(s) URIs to the
        // user's default browser. Best-effort: a malformed URI or shell
        // failure swallows quietly rather than crashing the diff pane.
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch
        {
            // intentional: nothing useful to do here
        }
        e.Handled = true;
    }
}
