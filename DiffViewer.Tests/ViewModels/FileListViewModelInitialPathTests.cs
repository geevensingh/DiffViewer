using DiffViewer.Models;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

/// <summary>
/// Tests for the <c>preferredInitialPath</c> parameter on
/// <see cref="FileListViewModel.LoadFromChanges"/>, added so the CLI
/// <c>--file</c> flag (issue #5) can pre-select a file on cold launch.
/// </summary>
public class FileListViewModelInitialPathTests
{
    private const string RepoRoot = @"C:\repo";

    private static FileChange ModifiedChange(string repoRelForwardSlash, WorkingTreeLayer layer = WorkingTreeLayer.Unstaged) =>
        new(Path: repoRelForwardSlash, OldPath: null,
            Status: FileStatus.Modified, ConflictCode: null,
            Layer: layer,
            LeftBlobSha: "aaaaaaa", RightBlobSha: "bbbbbbb",
            IsBinary: false,
            LeftFileSizeBytes: null, RightFileSizeBytes: null,
            IsLfsPointer: false, IsSparseNotCheckedOut: false,
            OldMode: 33188, NewMode: 33188);

    private static IReadOnlyList<FileChange> SampleChanges() => new[]
    {
        ModifiedChange("src/foo/a.cs"),
        ModifiedChange("src/bar/b.cs"),
        ModifiedChange("docs/README.md"),
    };

    [Fact]
    public void LoadFromChanges_PreferredInitialPath_SelectsMatchingEntry()
    {
        var vm = new FileListViewModel();

        vm.LoadFromChanges(
            SampleChanges(), RepoRoot, isCommitVsCommit: false,
            preferredInitialPath: @"src\bar\b.cs");

        vm.SelectedEntry.Should().NotBeNull();
        vm.SelectedEntry!.RepoRelativePath.Should().Be(@"src\bar\b.cs");
    }

    [Fact]
    public void LoadFromChanges_PreferredInitialPath_IsCaseInsensitive()
    {
        var vm = new FileListViewModel();

        vm.LoadFromChanges(
            SampleChanges(), RepoRoot, isCommitVsCommit: false,
            preferredInitialPath: @"SRC\FOO\A.CS");

        vm.SelectedEntry.Should().NotBeNull();
        vm.SelectedEntry!.RepoRelativePath.Should().Be(@"src\foo\a.cs");
    }

    [Fact]
    public void LoadFromChanges_PreferredInitialPath_Unmatched_LeavesSelectionNull()
    {
        var vm = new FileListViewModel();

        vm.LoadFromChanges(
            SampleChanges(), RepoRoot, isCommitVsCommit: false,
            preferredInitialPath: @"does\not\exist.cs");

        vm.FlatEntries.Should().HaveCount(3);
        vm.SelectedEntry.Should().BeNull();
    }

    [Fact]
    public void LoadFromChanges_PreferredInitialPath_Null_LeavesSelectionNull()
    {
        var vm = new FileListViewModel();

        vm.LoadFromChanges(
            SampleChanges(), RepoRoot, isCommitVsCommit: false,
            preferredInitialPath: null);

        vm.FlatEntries.Should().HaveCount(3);
        vm.SelectedEntry.Should().BeNull();
    }

    [Fact]
    public void LoadFromChanges_PreferredInitialPath_Empty_LeavesSelectionNull()
    {
        var vm = new FileListViewModel();

        vm.LoadFromChanges(
            SampleChanges(), RepoRoot, isCommitVsCommit: false,
            preferredInitialPath: "");

        vm.SelectedEntry.Should().BeNull();
    }

    [Fact]
    public void LoadFromChanges_PreferredInitialPath_IgnoredWhenPriorSelectionExists()
    {
        // Subsequent reload (prior selection present) — preferredInitialPath
        // must not override. The prior-selection-restore branch wins.
        var vm = new FileListViewModel();

        // First load with no preferred path; explicitly select an entry.
        vm.LoadFromChanges(SampleChanges(), RepoRoot, isCommitVsCommit: false);
        vm.SelectedEntry = vm.FlatEntries.First(e => e.RepoRelativePath == @"docs\README.md");
        vm.SelectedEntry!.RepoRelativePath.Should().Be(@"docs\README.md");

        // Second load: caller still passes preferredInitialPath, but the
        // prior selection (README.md) must be restored, not the preferred.
        vm.LoadFromChanges(
            SampleChanges(), RepoRoot, isCommitVsCommit: false,
            preferredInitialPath: @"src\foo\a.cs");

        vm.SelectedEntry.Should().NotBeNull();
        vm.SelectedEntry!.RepoRelativePath.Should().Be(@"docs\README.md");
    }

    [Fact]
    public void LoadFromChanges_PreferredInitialPath_WorksInCommitVsCommitLayout()
    {
        // Commit-vs-commit uses the flat "Changes" section. Selection logic
        // should still resolve preferredInitialPath against FlatEntries.
        var vm = new FileListViewModel();

        var changes = new[]
        {
            ModifiedChange("src/x.cs", WorkingTreeLayer.None),
            ModifiedChange("src/y.cs", WorkingTreeLayer.None),
        };

        vm.LoadFromChanges(
            changes, RepoRoot, isCommitVsCommit: true,
            preferredInitialPath: @"src\y.cs");

        vm.SelectedEntry.Should().NotBeNull();
        vm.SelectedEntry!.RepoRelativePath.Should().Be(@"src\y.cs");
    }
}
