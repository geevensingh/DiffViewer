using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

/// <summary>
/// Coverage for <see cref="CommitMetadataDialogViewModel"/>: the modal
/// dialog that opens when a user clicks a commit-metadata header row.
/// </summary>
public class CommitMetadataDialogViewModelTests
{
    private static CommitMetadata SampleMetadata(
        string sha = "fedcba0987654321fedcba0987654321fedcba09",
        string author = "Geeven Singh",
        string email = "geeven@example.com",
        string subject = "Show commit metadata in header rows",
        string body = "Long-form body text.\nWith multiple lines.\n\nAnd a paragraph break.",
        DateTimeOffset? authorDate = null,
        string? friendlyName = null) =>
        new(
            Sha: sha,
            ShortSha: sha[..7],
            AuthorName: author,
            AuthorEmail: email,
            AuthorDate: authorDate ?? new DateTimeOffset(2026, 2, 20, 14, 0, 0, TimeSpan.FromHours(-8)),
            MessageSubject: subject,
            MessageBody: body,
            FriendlyName: friendlyName);

    private sealed class RecordingClipboard : IClipboardService
    {
        public string? LastText { get; private set; }
        public int CallCount { get; private set; }
        public string? NextText { get; set; }
        public void SetText(string text) { LastText = text; CallCount++; }
        public bool TryGetText([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? text)
        {
            text = NextText;
            return text is not null;
        }
    }

    [Fact]
    public void Properties_PopulateFromMetadata()
    {
        var metadata = SampleMetadata();

        var vm = new CommitMetadataDialogViewModel(
            metadata,
            sideLabel: "Right",
            now: new DateTimeOffset(2026, 2, 21, 14, 0, 0, TimeSpan.FromHours(-8)));

        vm.SideLabel.Should().Be("Right");
        vm.FullSha.Should().Be(metadata.Sha);
        vm.ShortSha.Should().Be(metadata.ShortSha);
        vm.AuthorName.Should().Be("Geeven Singh");
        vm.AuthorEmail.Should().Be("geeven@example.com");
        vm.AuthorDisplay.Should().Be("Geeven Singh <geeven@example.com>");
        vm.Subject.Should().Be(metadata.MessageSubject);
        vm.Body.Should().Be(metadata.MessageBody);
        vm.HasBody.Should().BeTrue();
        vm.AuthorDate.Should().Be(metadata.AuthorDate);
        vm.AbsoluteDate.Should().Be("2026-02-20 14:00:00 -08:00");
        vm.RelativeDate.Should().Be("yesterday");
        vm.FriendlyName.Should().BeNull();
        vm.HasFriendlyName.Should().BeFalse();
    }

    [Fact]
    public void FriendlyName_FlowsThrough_WhenMetadataCarriesOne()
    {
        var metadata = SampleMetadata(friendlyName: "v0.4.0");

        var vm = new CommitMetadataDialogViewModel(metadata);

        vm.FriendlyName.Should().Be("v0.4.0");
        vm.HasFriendlyName.Should().BeTrue();
    }

    [Fact]
    public void HasFriendlyName_IsFalse_ForEmptyString()
    {
        var metadata = SampleMetadata(friendlyName: "");

        var vm = new CommitMetadataDialogViewModel(metadata);

        vm.FriendlyName.Should().Be("");
        vm.HasFriendlyName.Should().BeFalse();
    }

    [Fact]
    public void EmptyEmail_AuthorDisplay_FallsBackToNameOnly()
    {
        var metadata = SampleMetadata(email: string.Empty);

        var vm = new CommitMetadataDialogViewModel(metadata);

        vm.AuthorDisplay.Should().Be("Geeven Singh");
    }

    [Fact]
    public void EmptyBody_HasBodyIsFalse()
    {
        var metadata = SampleMetadata(body: string.Empty);

        var vm = new CommitMetadataDialogViewModel(metadata);

        vm.HasBody.Should().BeFalse();
        vm.Body.Should().BeEmpty();
    }

    [Fact]
    public void CopyShaCommand_InvokesClipboard_WithFullSha()
    {
        var metadata = SampleMetadata();
        var clipboard = new RecordingClipboard();

        var vm = new CommitMetadataDialogViewModel(metadata, clipboard);
        vm.CopyShaCommand.Execute(null);

        clipboard.CallCount.Should().Be(1);
        clipboard.LastText.Should().Be(metadata.Sha);
    }

    [Fact]
    public void CopyShaCommand_NoClipboard_IsNoOp()
    {
        var vm = new CommitMetadataDialogViewModel(SampleMetadata());

        var act = () => vm.CopyShaCommand.Execute(null);
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_RejectsNullMetadata()
    {
        var act = () => new CommitMetadataDialogViewModel(metadata: null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
