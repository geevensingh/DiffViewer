using DiffViewer.Models;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public class DirectoryNodeViewModelTests
{
    private static FileEntryViewModel Entry(string repoRelPath)
    {
        var change = new FileChange(
            Path: repoRelPath,
            OldPath: null,
            Status: Models.FileStatus.Modified,
            ConflictCode: null,
            Layer: WorkingTreeLayer.Unstaged,
            LeftBlobSha: null, RightBlobSha: null,
            IsBinary: false,
            LeftFileSizeBytes: null, RightFileSizeBytes: null,
            IsLfsPointer: false, IsSparseNotCheckedOut: false,
            OldMode: 0, NewMode: 0);
        return new FileEntryViewModel(change, @"C:\repo");
    }

    [Fact]
    public void Build_GroupsFilesUnderTheirDirectories()
    {
        var roots = DirectoryNodeViewModel.Build(new[]
        {
            Entry("src/a.cs"),
            Entry("src/b.cs"),
            Entry("docs/readme.md"),
        }).ToList();

        var dirs = roots.OfType<DirectoryNodeViewModel>().ToList();
        dirs.Select(r => r.Label).Should().Equal("docs", "src");
        dirs[1].Files.Select(f => f.FileName).Should().Equal("a.cs", "b.cs");
    }

    [Fact]
    public void Build_CollapsesSingleChildChains()
    {
        var roots = DirectoryNodeViewModel.Build(new[]
        {
            Entry("a/b/c/leaf.cs"),
        }).ToList();

        roots.Should().ContainSingle();
        var dir = roots[0].Should().BeOfType<DirectoryNodeViewModel>().Subject;
        dir.Label.Should().Be(@"a\b\c");
        dir.Files.Should().ContainSingle();
        dir.Files[0].FileName.Should().Be("leaf.cs");
    }

    [Fact]
    public void Build_DoesNotCollapseAcrossDirectoriesWithFiles()
    {
        var roots = DirectoryNodeViewModel.Build(new[]
        {
            Entry("a/x.cs"),
            Entry("a/b/c/leaf.cs"),
        }).ToList();

        roots.Should().ContainSingle();
        var dir = roots[0].Should().BeOfType<DirectoryNodeViewModel>().Subject;
        dir.Label.Should().Be("a");
        dir.Files.Select(f => f.FileName).Should().Contain("x.cs");
        dir.Children.Should().ContainSingle();
        dir.Children[0].Label.Should().Be(@"b\c");
    }

    [Fact]
    public void Build_ReturnsRootFilesAsSiblingsOfRootDirectories()
    {
        var roots = DirectoryNodeViewModel.Build(new[]
        {
            Entry("README.md"),
            Entry("src/a.cs"),
        }).ToList();

        // Root files surface as bare FileEntryViewModel siblings of the
        // root directories — not wrapped in a synthetic empty-labelled
        // DirectoryNodeViewModel (which would render as an empty header
        // row in the unified TreeView).
        roots.Should().HaveCount(2);
        roots[0].Should().BeOfType<FileEntryViewModel>()
            .Which.FileName.Should().Be("README.md");
        roots[1].Should().BeOfType<DirectoryNodeViewModel>()
            .Which.Label.Should().Be("src");
    }

    [Fact]
    public void Build_PlacesRootFilesBeforeRootDirectories_SortedByFileName()
    {
        var roots = DirectoryNodeViewModel.Build(new[]
        {
            Entry("zeta.txt"),
            Entry("src/a.cs"),
            Entry("README.md"),
            Entry("docs/x.md"),
            Entry("package.json"),
        }).ToList();

        // Root files come first, alphabetically (case-insensitive), then root directories.
        roots.Should().HaveCount(5);
        roots.Take(3).Should().AllBeOfType<FileEntryViewModel>();
        roots.Take(3).Cast<FileEntryViewModel>().Select(f => f.FileName)
            .Should().Equal("package.json", "README.md", "zeta.txt");
        roots.Skip(3).Should().AllBeOfType<DirectoryNodeViewModel>();
        roots.Skip(3).Cast<DirectoryNodeViewModel>().Select(d => d.Label)
            .Should().Equal("docs", "src");
    }

    [Fact]
    public void Build_ChildrenAndFiles_YieldsChildrenFirstThenFiles()
    {
        var roots = DirectoryNodeViewModel.Build(new[]
        {
            Entry("a/x.cs"),
            Entry("a/b/c/leaf.cs"),
        }).ToList();

        var dir = roots[0].Should().BeOfType<DirectoryNodeViewModel>().Subject;
        var combined = dir.ChildrenAndFiles.ToList();
        combined.Should().HaveCount(2);
        combined[0].Should().BeOfType<DirectoryNodeViewModel>();
        combined[1].Should().BeOfType<FileEntryViewModel>();
    }

    [Fact]
    public void Build_DefaultsAllNodesToExpanded()
    {
        var roots = DirectoryNodeViewModel.Build(new[]
        {
            Entry("a/b/x.cs"),
            Entry("a/b/y.cs"),
        }).ToList();

        roots[0].Should().BeOfType<DirectoryNodeViewModel>()
            .Which.IsExpanded.Should().BeTrue();
    }

    [Fact]
    public void Build_WithStore_CollapsedStateSurvivesRebuild()
    {
        var store = new DirectoryExpansionStore();

        var first = DirectoryNodeViewModel.Build(
            new[] { Entry("src/a.cs"), Entry("docs/readme.md") },
            sectionKey: "Unstaged",
            store: store).OfType<DirectoryNodeViewModel>().ToList();

        // User collapses "src".
        var src = first.Single(r => r.Label == "src");
        src.IsExpanded = false;

        // Simulate a watcher-fired reload: a new file appears, sections rebuild.
        var second = DirectoryNodeViewModel.Build(
            new[] { Entry("src/a.cs"), Entry("docs/readme.md"), Entry("src/c.cs") },
            sectionKey: "Unstaged",
            store: store).OfType<DirectoryNodeViewModel>().ToList();

        second.Single(r => r.Label == "src").IsExpanded.Should().BeFalse();
        second.Single(r => r.Label == "docs").IsExpanded.Should().BeTrue();
    }

    [Fact]
    public void Build_WithStore_CollapsingNestedNodeSurvivesRebuild()
    {
        var store = new DirectoryExpansionStore();

        var first = DirectoryNodeViewModel.Build(
            new[] { Entry("a/x.cs"), Entry("a/b/c/leaf.cs") },
            sectionKey: "Unstaged",
            store: store).OfType<DirectoryNodeViewModel>().ToList();

        // User collapses the chained "b\c" child of "a".
        first[0].Children.Single().IsExpanded = false;

        var second = DirectoryNodeViewModel.Build(
            new[] { Entry("a/x.cs"), Entry("a/b/c/leaf.cs"), Entry("a/b/c/another.cs") },
            sectionKey: "Unstaged",
            store: store).OfType<DirectoryNodeViewModel>().ToList();

        second[0].Children.Single().IsExpanded.Should().BeFalse();
    }

    [Fact]
    public void Build_WithStore_DifferentSectionsHaveIndependentExpansionState()
    {
        var store = new DirectoryExpansionStore();

        var staged = DirectoryNodeViewModel.Build(
            new[] { Entry("src/a.cs") },
            sectionKey: "Staged",
            store: store).OfType<DirectoryNodeViewModel>().ToList();
        staged.Single().IsExpanded = false;

        var unstaged = DirectoryNodeViewModel.Build(
            new[] { Entry("src/b.cs") },
            sectionKey: "Unstaged",
            store: store).OfType<DirectoryNodeViewModel>().ToList();

        unstaged.Single().IsExpanded.Should().BeTrue();
    }
}
