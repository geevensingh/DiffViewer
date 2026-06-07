using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Rendering;

namespace DiffViewer.ViewModels;

/// <summary>
/// Markdown rendered-diff sibling view-model. Set on
/// <see cref="DiffPaneViewModel.MarkdownDiff"/> when the currently loaded
/// entry is a <c>.md</c> / <c>.markdown</c> file, after the blob text has
/// been read. Holds the rendered <see cref="FlowDocument"/> shown by
/// <c>MarkdownDiffView</c>.
///
/// <para><b>Threading:</b> the constructor calls
/// <see cref="MarkdownDiffRenderer.Render"/>, which produces a
/// dispatcher-affine <see cref="FlowDocument"/>. Construct this VM on
/// the UI thread (the existing text-dispatch path's <c>ContinueWith</c>
/// is on <see cref="TaskScheduler.FromCurrentSynchronizationContext"/>,
/// which is the correct seam). The blob read itself can stay on a
/// background thread; only the <see cref="FlowDocument"/> assembly
/// needs the dispatcher.</para>
///
/// <para>The VM is immutable from the caller's perspective: it's built
/// once with the old / new text and never mutated. A subsequent reload
/// produces a brand-new VM instance, mirroring how
/// <see cref="ImageDiffViewModel"/> is replaced wholesale rather than
/// updated in place.</para>
/// </summary>
public sealed partial class MarkdownDiffViewModel : ObservableObject
{
    /// <summary>
    /// Rendered diff document. Bound directly into
    /// <see cref="System.Windows.Controls.FlowDocumentScrollViewer.Document"/>
    /// by the view.
    /// </summary>
    public FlowDocument Document { get; }

    public MarkdownDiffViewModel(string leftText, string rightText)
    {
        Document = MarkdownDiffRenderer.Render(leftText ?? string.Empty, rightText ?? string.Empty);
    }
}
