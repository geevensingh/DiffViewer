using DiffViewer.Models;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public class FileEntryViewModelTests
{
    private static FileChange Modified(string path, string? oldPath = null) =>
        new(
            Path: path,
            OldPath: oldPath,
            Status: oldPath is null ? Models.FileStatus.Modified : Models.FileStatus.Renamed,
            ConflictCode: null,
            Layer: WorkingTreeLayer.Unstaged,
            LeftBlobSha: null, RightBlobSha: null,
            IsBinary: false,
            LeftFileSizeBytes: null, RightFileSizeBytes: null,
            IsLfsPointer: false, IsSparseNotCheckedOut: false,
            OldMode: 0, NewMode: 0);

    [Fact]
    public void Paths_Are_BackslashSeparated_OnWindows()
    {
        var e = new FileEntryViewModel(Modified("src/foo/bar.cs"), @"C:\repo");

        e.RepoRelativePath.Should().Be(@"src\foo\bar.cs");
        e.FullPath.Should().Be(@"C:\repo\src\foo\bar.cs");
        e.FileName.Should().Be("bar.cs");
        e.DirectoryPath.Should().Be(@"src\foo");
    }

    [Fact]
    public void ApplyDisplayMode_SwitchesDisplayPath()
    {
        var e = new FileEntryViewModel(Modified("src/foo.cs"), @"C:\repo");

        e.ApplyDisplayMode(FileListDisplayMode.FullPath);
        e.DisplayPath.Should().Be(@"C:\repo\src\foo.cs");

        e.ApplyDisplayMode(FileListDisplayMode.RepoRelative);
        e.DisplayPath.Should().Be(@"src\foo.cs");

        e.ApplyDisplayMode(FileListDisplayMode.GroupedByDirectory);
        e.DisplayPath.Should().Be("foo.cs");
    }

    [Fact]
    public void RenameDescriptor_PopulatedForRenames_EmptyOtherwise()
    {
        var renamed = new FileEntryViewModel(Modified("new.cs", oldPath: "old.cs"), @"C:\repo");
        renamed.RenameDescriptor.Should().Contain("old.cs");

        var modified = new FileEntryViewModel(Modified("a.cs"), @"C:\repo");
        modified.RenameDescriptor.Should().BeEmpty();
    }

    [Fact]
    public void IsWhitespaceOnly_TracksHasVisibleDifferencesNegation()
    {
        var e = new FileEntryViewModel(Modified("a.cs"), @"C:\repo");
        e.IsWhitespaceOnly.Should().BeFalse(); // null == not yet known, treated as not flagged

        e.HasVisibleDifferences = true;
        e.IsWhitespaceOnly.Should().BeFalse();

        e.HasVisibleDifferences = false;
        e.IsWhitespaceOnly.Should().BeTrue();
    }

    [Fact]
    public void IsDimmed_TrueWhenViewedOrWhitespaceOnly_FalseWhenNeither()
    {
        var e = new FileEntryViewModel(Modified("a.cs"), @"C:\repo");

        // Neither input set yet.
        e.IsDimmed.Should().BeFalse();

        // Whitespace-only alone dims.
        e.HasVisibleDifferences = false;
        e.IsDimmed.Should().BeTrue();

        // Adding viewed on top still dims.
        e.IsViewed = true;
        e.IsDimmed.Should().BeTrue();

        // Clear whitespace-only; viewed alone keeps it dimmed.
        e.HasVisibleDifferences = true;
        e.IsDimmed.Should().BeTrue();

        // Clear viewed too: undimmed.
        e.IsViewed = false;
        e.IsDimmed.Should().BeFalse();
    }

    [Fact]
    public void IsDimmed_RaisesPropertyChanged_OnEitherInput()
    {
        var e = new FileEntryViewModel(Modified("a.cs"), @"C:\repo");
        var raised = new List<string?>();
        e.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        e.IsViewed = true;
        raised.Should().Contain(nameof(FileEntryViewModel.IsDimmed));

        raised.Clear();
        e.HasVisibleDifferences = false;
        raised.Should().Contain(nameof(FileEntryViewModel.IsDimmed));
    }

    [Fact]
    public void NormalizedPathForFilter_UsesForwardSlashes()
    {
        var e = new FileEntryViewModel(Modified("src/foo/bar.cs"), @"C:\repo");

        e.NormalizedPathForFilter.Should().Be("src/foo/bar.cs");
    }

    // ---- Whole-file write-action eligibility ----
    //
    // Mirrors per-hunk eligibility on DiffPaneViewModel one level up:
    //   * Stage:   Untracked or Unstaged (wider than per-hunk on purpose;
    //              untracked files have no hunks to right-click in the diff
    //              pane, so whole-file Stage is the only UI path to add them).
    //   * Unstage: Staged only.
    //   * Revert:  Unstaged only (destructive `git restore -- path`).

    private static FileChange Row(WorkingTreeLayer layer, FileStatus status = Models.FileStatus.Modified) =>
        new(
            Path: "x.cs",
            OldPath: null,
            Status: status,
            ConflictCode: null,
            Layer: layer,
            LeftBlobSha: null, RightBlobSha: null,
            IsBinary: false,
            LeftFileSizeBytes: null, RightFileSizeBytes: null,
            IsLfsPointer: false, IsSparseNotCheckedOut: false,
            OldMode: 0, NewMode: 0);

    [Theory]
    [InlineData(WorkingTreeLayer.Untracked, true)]
    [InlineData(WorkingTreeLayer.Unstaged, true)]
    [InlineData(WorkingTreeLayer.Staged, false)]
    [InlineData(WorkingTreeLayer.Conflicted, false)]
    [InlineData(WorkingTreeLayer.CommittedSinceCommit, false)]
    [InlineData(WorkingTreeLayer.None, false)]
    public void CanStageWholeFile_TrueFor_UnstagedAndUntracked_FalseOtherwise(WorkingTreeLayer layer, bool expected)
    {
        new FileEntryViewModel(Row(layer), @"C:\repo").CanStageWholeFile.Should().Be(expected);
    }

    [Theory]
    [InlineData(WorkingTreeLayer.Staged, true)]
    [InlineData(WorkingTreeLayer.Untracked, false)]
    [InlineData(WorkingTreeLayer.Unstaged, false)]
    [InlineData(WorkingTreeLayer.Conflicted, false)]
    [InlineData(WorkingTreeLayer.CommittedSinceCommit, false)]
    [InlineData(WorkingTreeLayer.None, false)]
    public void CanUnstageWholeFile_TrueFor_StagedOnly_FalseOtherwise(WorkingTreeLayer layer, bool expected)
    {
        new FileEntryViewModel(Row(layer), @"C:\repo").CanUnstageWholeFile.Should().Be(expected);
    }

    [Theory]
    [InlineData(WorkingTreeLayer.Unstaged, true)]
    [InlineData(WorkingTreeLayer.Staged, false)]
    [InlineData(WorkingTreeLayer.Untracked, false)]
    [InlineData(WorkingTreeLayer.Conflicted, false)]
    [InlineData(WorkingTreeLayer.CommittedSinceCommit, false)]
    [InlineData(WorkingTreeLayer.None, false)]
    public void CanRevertWholeFile_TrueFor_UnstagedOnly_FalseOtherwise(WorkingTreeLayer layer, bool expected)
    {
        new FileEntryViewModel(Row(layer), @"C:\repo").CanRevertWholeFile.Should().Be(expected);
    }

    [Theory]
    [InlineData(WorkingTreeLayer.Untracked, true)]    // Stage
    [InlineData(WorkingTreeLayer.Unstaged, true)]     // Stage + Revert
    [InlineData(WorkingTreeLayer.Staged, true)]       // Unstage
    [InlineData(WorkingTreeLayer.Conflicted, false)]
    [InlineData(WorkingTreeLayer.CommittedSinceCommit, false)]
    [InlineData(WorkingTreeLayer.None, false)]
    public void HasFileWriteAction_TrueWhen_AnyOfTheThreeAreTrue(WorkingTreeLayer layer, bool expected)
    {
        new FileEntryViewModel(Row(layer), @"C:\repo").HasFileWriteAction.Should().Be(expected);
    }
}
