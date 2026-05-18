using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.Utility;

namespace DiffViewer.ViewModels;

/// <summary>
/// Backs the compact "this side is commit X" header row that renders
/// at the top of the file-list column for any <see cref="DiffSide"/>
/// pointing at a commit. The whole row is a click target; clicking
/// opens a <see cref="CommitMetadataDialogViewModel"/>-driven modal
/// with the full author / date / message.
///
/// <para><b>Only constructed for commit sides.</b> Working-tree sides
/// leave the corresponding property on <see cref="MainViewModel"/>
/// null, so the row collapses to nothing in the View. This VM has no
/// "working tree" rendering branch — that's enforced at the
/// construction site, not at runtime via a flag.</para>
///
/// <para><b>Relative-date snapshot.</b> <see cref="RelativeDate"/> is
/// computed once at construction. It will not "tick" while the app is
/// open — opening the modal recomputes against a fresh "now", but the
/// row keeps the value it was built with. Acceptable v1 trade-off:
/// most sessions are short enough that "5 minutes ago" doesn't go
/// stale before the next context switch.</para>
/// </summary>
public sealed partial class CommitMetadataPanelViewModel : ObservableObject
{
    private readonly CommitMetadata _metadata;
    private readonly IClipboardService? _clipboard;
    private readonly Action<CommitMetadataDialogViewModel>? _showDetails;

    /// <summary>Side identifier ("Left" / "Right"). Renders in the row badge.</summary>
    public string SideLabel { get; }

    /// <summary>The underlying metadata record — exposed so the dialog VM
    /// (built on-the-fly by <see cref="ShowDetailsCommand"/>) can read
    /// it without a second service call.</summary>
    public CommitMetadata Metadata => _metadata;

    /// <summary>Full SHA (40 chars).</summary>
    public string FullSha => _metadata.Sha;

    /// <summary>Short SHA (7 chars) — the badge in the row.</summary>
    public string ShortSha => _metadata.ShortSha;

    /// <summary>Author display name (no email) — rendered in the row.</summary>
    public string AuthorName => _metadata.AuthorName;

    /// <summary>The "X minutes/hours/days ago" string. Computed once at construction.</summary>
    public string RelativeDate { get; }

    /// <summary>
    /// ISO-style absolute date in the commit's own timezone, used as
    /// the tooltip on the row's relative date. Format:
    /// <c>yyyy-MM-dd HH:mm:ss zzz</c> — invariant culture so the
    /// renderer is deterministic across locales.
    /// </summary>
    public string AbsoluteDate { get; }

    /// <summary>Single-line subject — truncated by the View's TextTrimming.</summary>
    public string Subject => _metadata.MessageSubject;

    public CommitMetadataPanelViewModel(
        string sideLabel,
        CommitMetadata metadata,
        IClipboardService? clipboard = null,
        Action<CommitMetadataDialogViewModel>? showDetailsHandler = null,
        DateTimeOffset? now = null)
    {
        SideLabel = sideLabel ?? throw new ArgumentNullException(nameof(sideLabel));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _clipboard = clipboard;
        _showDetails = showDetailsHandler;

        var nowMoment = now ?? DateTimeOffset.Now;
        RelativeDate = RelativeDateFormatter.Format(_metadata.AuthorDate, nowMoment);
        AbsoluteDate = _metadata.AuthorDate.ToString(
            "yyyy-MM-dd HH:mm:ss zzz",
            CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Build the dialog VM and hand it to the registered handler. If
    /// no handler is wired (headless tests), this is a no-op — the
    /// View layer owns the actual modal-show plumbing.
    /// </summary>
    [RelayCommand]
    private void ShowDetails()
    {
        if (_showDetails is null) return;
        var dialog = new CommitMetadataDialogViewModel(_metadata, _clipboard, SideLabel);
        _showDetails(dialog);
    }
}
