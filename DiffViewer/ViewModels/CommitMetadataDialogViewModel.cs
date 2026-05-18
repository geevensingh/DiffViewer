using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.Utility;

namespace DiffViewer.ViewModels;

/// <summary>
/// Backs the modal that opens when the user clicks a commit-metadata
/// header row. Shows the full author / date / SHA / message body and
/// owns the "Copy SHA" command.
///
/// <para>Built by <see cref="CommitMetadataPanelViewModel.ShowDetailsCommand"/>
/// from the same <see cref="CommitMetadata"/> the row was constructed
/// with — no second LibGit2Sharp lookup happens at click time.</para>
/// </summary>
public sealed partial class CommitMetadataDialogViewModel : ObservableObject
{
    private readonly IClipboardService? _clipboard;

    public string SideLabel { get; }
    public string FullSha { get; }
    public string ShortSha { get; }
    public string AuthorName { get; }
    public string AuthorEmail { get; }

    /// <summary>"Name &lt;email&gt;" — what reviewers expect to see.</summary>
    public string AuthorDisplay { get; }

    public string Subject { get; }
    public string Body { get; }

    /// <summary>True when <see cref="Body"/> is empty — the View binds
    /// to this to hide the body section rather than render an empty
    /// scroll region.</summary>
    public bool HasBody { get; }

    public DateTimeOffset AuthorDate { get; }
    public string AbsoluteDate { get; }
    public string RelativeDate { get; }

    public CommitMetadataDialogViewModel(
        CommitMetadata metadata,
        IClipboardService? clipboard = null,
        string sideLabel = "",
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        _clipboard = clipboard;

        SideLabel = sideLabel ?? string.Empty;
        FullSha = metadata.Sha;
        ShortSha = metadata.ShortSha;
        AuthorName = metadata.AuthorName;
        AuthorEmail = metadata.AuthorEmail;
        AuthorDisplay = string.IsNullOrEmpty(metadata.AuthorEmail)
            ? metadata.AuthorName
            : $"{metadata.AuthorName} <{metadata.AuthorEmail}>";
        Subject = metadata.MessageSubject;
        Body = metadata.MessageBody;
        HasBody = !string.IsNullOrEmpty(metadata.MessageBody);
        AuthorDate = metadata.AuthorDate;
        AbsoluteDate = metadata.AuthorDate.ToString(
            "yyyy-MM-dd HH:mm:ss zzz",
            CultureInfo.InvariantCulture);
        RelativeDate = RelativeDateFormatter.Format(metadata.AuthorDate, now ?? DateTimeOffset.Now);
    }

    /// <summary>Copy the full 40-char SHA to the clipboard. No-op when no
    /// <see cref="IClipboardService"/> was injected (headless tests).</summary>
    [RelayCommand]
    private void CopySha()
    {
        _clipboard?.SetText(FullSha);
    }
}
