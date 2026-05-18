using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

/// <summary>
/// Coverage for <see cref="CommitMetadataPanelViewModel"/>: the compact
/// header row backing the click-to-modal "show commit details" UX.
/// </summary>
public class CommitMetadataPanelViewModelTests
{
    private static CommitMetadata SampleMetadata(
        string sha = "abcdef1234567890abcdef1234567890abcdef12",
        string author = "Geeven Singh",
        string email = "geeven@example.com",
        string subject = "Add commit metadata panel",
        string body = "",
        DateTimeOffset? authorDate = null) =>
        new(
            Sha: sha,
            ShortSha: sha[..7],
            AuthorName: author,
            AuthorEmail: email,
            AuthorDate: authorDate ?? new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.FromHours(-8)),
            MessageSubject: subject,
            MessageBody: body);

    [Fact]
    public void Properties_PopulateFromMetadata()
    {
        var metadata = SampleMetadata();

        var vm = new CommitMetadataPanelViewModel(
            sideLabel: "Left",
            metadata: metadata,
            now: new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.FromHours(-8)));

        vm.SideLabel.Should().Be("Left");
        vm.ShortSha.Should().Be("abcdef1");
        vm.FullSha.Should().Be("abcdef1234567890abcdef1234567890abcdef12");
        vm.AuthorName.Should().Be("Geeven Singh");
        vm.Subject.Should().Be("Add commit metadata panel");
        vm.RelativeDate.Should().Be("1 hour ago");
        vm.AbsoluteDate.Should().Be("2026-01-15 09:30:00 -08:00");
        vm.Metadata.Should().Be(metadata);
    }

    [Fact]
    public void ShowDetailsCommand_InvokesHandler_WithDialogVm()
    {
        var metadata = SampleMetadata();
        CommitMetadataDialogViewModel? captured = null;

        var vm = new CommitMetadataPanelViewModel(
            sideLabel: "Right",
            metadata: metadata,
            clipboard: null,
            showDetailsHandler: d => captured = d);

        vm.ShowDetailsCommand.Execute(null);

        captured.Should().NotBeNull();
        captured!.SideLabel.Should().Be("Right");
        captured.FullSha.Should().Be(metadata.Sha);
        captured.Subject.Should().Be(metadata.MessageSubject);
    }

    [Fact]
    public void ShowDetailsCommand_NoHandler_IsNoOp()
    {
        var vm = new CommitMetadataPanelViewModel("Left", SampleMetadata());

        var act = () => vm.ShowDetailsCommand.Execute(null);
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_RejectsNullMetadata()
    {
        var act = () => new CommitMetadataPanelViewModel("Left", metadata: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_RejectsNullSideLabel()
    {
        var act = () => new CommitMetadataPanelViewModel(sideLabel: null!, SampleMetadata());
        act.Should().Throw<ArgumentNullException>();
    }
}
