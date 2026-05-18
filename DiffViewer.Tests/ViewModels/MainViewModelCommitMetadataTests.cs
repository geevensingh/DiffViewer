using System.IO;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.Utility;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

/// <summary>
/// Integration coverage for the <see cref="MainViewModel"/> ↔
/// <see cref="CommitMetadataPanelViewModel"/> wiring across the three
/// supported launch contexts.
///
/// <para>Uses an in-test fake of <see cref="IRepositoryService"/> with
/// a dictionary-backed <c>GetCommitMetadata</c>; this avoids hitting
/// LibGit2Sharp / disk and lets each test pin the exact metadata it
/// wants the side to resolve to.</para>
/// </summary>
public class MainViewModelCommitMetadataTests : IDisposable
{
    private readonly string _repoRoot;

    public MainViewModelCommitMetadataTests()
    {
        _repoRoot = Path.Combine(Path.GetTempPath(), "DiffViewerCmd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repoRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_repoRoot, recursive: true); } catch { /* best effort */ }
    }

    private static CommitMetadata Meta(string sha = "1111111111111111111111111111111111111111", string subject = "subj") =>
        new(
            Sha: sha,
            ShortSha: sha[..7],
            AuthorName: "Geeven",
            AuthorEmail: "g@example.com",
            AuthorDate: new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero),
            MessageSubject: subject,
            MessageBody: string.Empty);

    private MainViewModel BuildVm(
        DiffSide left,
        DiffSide right,
        FakeRepo repo)
    {
        repo.Shape_ = new RepositoryShape(
            RepoRoot: _repoRoot,
            WorkingDirectory: _repoRoot,
            GitDir: Path.Combine(_repoRoot, ".git"),
            IsBare: false, IsHeadUnborn: false,
            IsSparseCheckout: false, IsPartialClone: false,
            HasInProgressOperation: false);

        return new MainViewModel(
            repository: repo,
            left: left,
            right: right);
    }

    [Fact]
    public void WorkingTreeVsHead_PopulatesOnlyRightPanel()
    {
        var repo = new FakeRepo();
        var head = Meta(subject: "head subject");
        repo.CommitMetadataByRef["HEAD"] = head;

        using var vm = BuildVm(new DiffSide.WorkingTree(), new DiffSide.CommitIsh("HEAD"), repo);

        vm.LeftCommitPanel.Should().BeNull();
        vm.RightCommitPanel.Should().NotBeNull();
        vm.RightCommitPanel!.SideLabel.Should().Be("Right");
        vm.RightCommitPanel.Subject.Should().Be("head subject");
    }

    [Fact]
    public void WorkingTreeVsCommit_PopulatesOnlyRightPanel()
    {
        var repo = new FakeRepo();
        repo.CommitMetadataByRef["v1.0"] = Meta(subject: "tagged commit");

        using var vm = BuildVm(new DiffSide.WorkingTree(), new DiffSide.CommitIsh("v1.0"), repo);

        vm.LeftCommitPanel.Should().BeNull();
        vm.RightCommitPanel.Should().NotBeNull();
        vm.RightCommitPanel!.Subject.Should().Be("tagged commit");
    }

    [Fact]
    public void CommitVsCommit_PopulatesBothPanels()
    {
        var repo = new FakeRepo();
        repo.CommitMetadataByRef["base"] = Meta(
            sha: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            subject: "base commit");
        repo.CommitMetadataByRef["feature"] = Meta(
            sha: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            subject: "feature commit");

        using var vm = BuildVm(new DiffSide.CommitIsh("base"), new DiffSide.CommitIsh("feature"), repo);

        vm.LeftCommitPanel.Should().NotBeNull();
        vm.LeftCommitPanel!.SideLabel.Should().Be("Left");
        vm.LeftCommitPanel.ShortSha.Should().Be("aaaaaaa");
        vm.LeftCommitPanel.Subject.Should().Be("base commit");

        vm.RightCommitPanel.Should().NotBeNull();
        vm.RightCommitPanel!.SideLabel.Should().Be("Right");
        vm.RightCommitPanel.ShortSha.Should().Be("bbbbbbb");
        vm.RightCommitPanel.Subject.Should().Be("feature commit");
    }

    [Fact]
    public void UnresolvedRef_LeavesPanelNull()
    {
        // The fake returns null for refs not in the dictionary — mirrors
        // production behavior when LibGit2Sharp can't resolve.
        var repo = new FakeRepo();

        using var vm = BuildVm(new DiffSide.WorkingTree(), new DiffSide.CommitIsh("ghost"), repo);

        vm.RightCommitPanel.Should().BeNull();
    }

    [Fact]
    public void ShowDetailsCommand_InvokesMainViewModelHandler()
    {
        var repo = new FakeRepo();
        repo.CommitMetadataByRef["HEAD"] = Meta(subject: "dialog test");
        using var vm = BuildVm(new DiffSide.WorkingTree(), new DiffSide.CommitIsh("HEAD"), repo);

        CommitMetadataDialogViewModel? dialogSeen = null;
        vm.ShowCommitMetadataHandler = d => dialogSeen = d;

        vm.RightCommitPanel!.ShowDetailsCommand.Execute(null);

        dialogSeen.Should().NotBeNull();
        dialogSeen!.SideLabel.Should().Be("Right");
        dialogSeen.Subject.Should().Be("dialog test");
    }

    [Fact]
    public void ShowDetailsCommand_WithoutHandler_IsNoOp()
    {
        var repo = new FakeRepo();
        repo.CommitMetadataByRef["HEAD"] = Meta();
        using var vm = BuildVm(new DiffSide.WorkingTree(), new DiffSide.CommitIsh("HEAD"), repo);

        // Handler intentionally left null.
        var act = () => vm.RightCommitPanel!.ShowDetailsCommand.Execute(null);
        act.Should().NotThrow();
    }

    // ----- Minimal IRepositoryService fake (commit-metadata focused) -----

    private sealed class FakeRepo : IRepositoryService
    {
        public RepositoryShape Shape_ { get; set; } = new(@"C:\repo", @"C:\repo", @"C:\repo\.git", false, false, false, false, false);
        public RepositoryShape Shape => Shape_;

#pragma warning disable CS0067
        public event EventHandler<ChangeListUpdatedEventArgs>? ChangeListUpdated;
        public event EventHandler<RepositoryLostEventArgs>? RepositoryLost;
#pragma warning restore CS0067

        public Dictionary<string, CommitMetadata> CommitMetadataByRef { get; } = new();

        public string? ResolveCommitIsh(string reference) =>
            CommitMetadataByRef.TryGetValue(reference, out var m) ? m.Sha : null;

        public CommitMetadata? GetCommitMetadata(string commitIsh) =>
            CommitMetadataByRef.TryGetValue(commitIsh, out var m) ? m : null;

        public bool ValidateRevisions(string leftRef, string rightRef) => true;

        public IReadOnlyList<FileChange> CurrentChanges => Array.Empty<FileChange>();
        public IReadOnlyList<FileChange> EnumerateChanges(DiffSide left, DiffSide right) =>
            Array.Empty<FileChange>();

        public BlobContent ReadSide(FileChange change, ChangeSide side) =>
            new(Array.Empty<byte>(), System.Text.Encoding.UTF8, string.Empty,
                IsBinary: false, IsLfsPointer: false);

        public BlobIdentity? ProbeSideIdentity(FileChange change, ChangeSide side) => BlobIdentity.Empty;

        public void RefreshIndex() { }
        public FileChange? TryResolveCurrent(string path, WorkingTreeLayer layer) => null;
        public bool TryReopen() => true;

        public (IReadOnlyList<FileChange> Snapshot, IDisposable Subscription) SnapshotAndSubscribe(
            EventHandler<ChangeListUpdatedEventArgs> handler) =>
            (Array.Empty<FileChange>(), new NoopDisposable());

        public bool IsPathIgnored(string repoRelativeForwardSlashPath) => false;
        public void Dispose() { }

        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
