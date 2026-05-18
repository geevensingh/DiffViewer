using DiffViewer.Models;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public class FileListViewModelTests
{
    private static FileChange MakeChange(
        string path,
        Models.FileStatus status = Models.FileStatus.Modified,
        WorkingTreeLayer layer = WorkingTreeLayer.Unstaged,
        string? oldPath = null) =>
        new(
            Path: path,
            OldPath: oldPath,
            Status: status,
            ConflictCode: status == Models.FileStatus.Conflicted ? "UU" : null,
            Layer: layer,
            LeftBlobSha: null,
            RightBlobSha: null,
            IsBinary: false,
            LeftFileSizeBytes: null,
            RightFileSizeBytes: null,
            IsLfsPointer: false,
            IsSparseNotCheckedOut: false,
            OldMode: 0,
            NewMode: 0);

    [Fact]
    public void LoadFromChanges_FlatList_WhenCommitVsCommit()
    {
        var vm = new FileListViewModel();
        var changes = new[]
        {
            MakeChange("src/a.cs"),
            MakeChange("src/b.cs"),
        };

        vm.LoadFromChanges(changes, @"C:\repo", isCommitVsCommit: true);

        vm.IsFlatLayout.Should().BeTrue();
        vm.Sections.Should().HaveCount(1);
        vm.Sections[0].Header.Should().Be("Changes");
        vm.Sections[0].Entries.Should().HaveCount(2);
        vm.FlatEntries.Should().HaveCount(2);
    }

    [Fact]
    public void LoadFromChanges_GroupsByLayer_InCanonicalOrder()
    {
        var vm = new FileListViewModel();
        var changes = new[]
        {
            MakeChange("u.txt", Models.FileStatus.Untracked, WorkingTreeLayer.Untracked),
            MakeChange("s.cs", layer: WorkingTreeLayer.Staged),
            MakeChange("c.cs", Models.FileStatus.Conflicted, WorkingTreeLayer.Conflicted),
            MakeChange("w.cs", layer: WorkingTreeLayer.Unstaged),
            MakeChange("h.cs", layer: WorkingTreeLayer.CommittedSinceCommit),
        };

        vm.LoadFromChanges(changes, @"C:\repo", isCommitVsCommit: false);

        vm.IsFlatLayout.Should().BeFalse();
        vm.Sections.Select(s => s.Layer).Should().Equal(
            WorkingTreeLayer.Conflicted,
            WorkingTreeLayer.CommittedSinceCommit,
            WorkingTreeLayer.Staged,
            WorkingTreeLayer.Unstaged,
            WorkingTreeLayer.Untracked);
    }

    [Fact]
    public void LoadFromChanges_OmitsEmptySections()
    {
        var vm = new FileListViewModel();
        var changes = new[]
        {
            MakeChange("a.cs", layer: WorkingTreeLayer.Staged),
            MakeChange("b.cs", layer: WorkingTreeLayer.Staged),
        };

        vm.LoadFromChanges(changes, @"C:\repo", isCommitVsCommit: false);

        vm.Sections.Should().ContainSingle();
        vm.Sections[0].Layer.Should().Be(WorkingTreeLayer.Staged);
    }

    [Fact]
    public void LoadFromChanges_ResetsBetweenCalls()
    {
        var vm = new FileListViewModel();
        vm.LoadFromChanges(new[] { MakeChange("a.cs") }, @"C:\repo", isCommitVsCommit: false);
        vm.LoadFromChanges(Array.Empty<FileChange>(), @"C:\repo", isCommitVsCommit: false);

        vm.Sections.Should().BeEmpty();
        vm.FlatEntries.Should().BeEmpty();
    }

    [Fact]
    public void LoadFromChanges_PreservesSelection_WhenSameFileStillInList()
    {
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs"), MakeChange("src/b.cs") },
            @"C:\repo", isCommitVsCommit: false);
        vm.SelectedEntry = vm.FlatEntries.Single(e => e.Change.Path == "src/b.cs");

        // Same file list (e.g. a no-op file-system event). New VMs but the
        // selection should still resolve to "src/b.cs".
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs"), MakeChange("src/b.cs") },
            @"C:\repo", isCommitVsCommit: false);

        vm.SelectedEntry.Should().NotBeNull();
        vm.SelectedEntry!.Change.Path.Should().Be("src/b.cs");
        // The instance must be one of the new entries, not a dangling old ref.
        vm.FlatEntries.Should().Contain(vm.SelectedEntry);
    }

    [Fact]
    public void LoadFromChanges_PreservesSelection_AcrossUnrelatedAddRemove()
    {
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs"), MakeChange("src/b.cs") },
            @"C:\repo", isCommitVsCommit: false);
        vm.SelectedEntry = vm.FlatEntries.Single(e => e.Change.Path == "src/b.cs");

        // a.cs goes away, c.cs appears, but b.cs is still selected.
        vm.LoadFromChanges(
            new[] { MakeChange("src/b.cs"), MakeChange("src/c.cs") },
            @"C:\repo", isCommitVsCommit: false);

        vm.SelectedEntry.Should().NotBeNull();
        vm.SelectedEntry!.Change.Path.Should().Be("src/b.cs");
    }

    [Fact]
    public void LoadFromChanges_ClearsSelection_WhenSelectedFileFallsOut()
    {
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs"), MakeChange("src/b.cs") },
            @"C:\repo", isCommitVsCommit: false);
        vm.SelectedEntry = vm.FlatEntries.Single(e => e.Change.Path == "src/b.cs");

        // b.cs no longer in the list (e.g. user reverted it).
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs") },
            @"C:\repo", isCommitVsCommit: false);

        vm.SelectedEntry.Should().BeNull();
    }

    [Fact]
    public void LoadFromChanges_ClearsSelection_WhenFileMovesToDifferentLayer()
    {
        // The same path appearing under a different layer is a genuinely
        // different diff (e.g. an Unstaged modification became a Staged
        // modification once the user ran git add). Treat it as a fall-out
        // so the diff pane reloads with the new layer's content.
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs", layer: WorkingTreeLayer.Unstaged) },
            @"C:\repo", isCommitVsCommit: false);
        vm.SelectedEntry = vm.FlatEntries[0];

        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs", layer: WorkingTreeLayer.Staged) },
            @"C:\repo", isCommitVsCommit: false);

        vm.SelectedEntry.Should().BeNull();
    }

    [Fact]
    public void LoadFromChanges_LeavesSelectionNull_WhenNothingWasSelected()
    {
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs") },
            @"C:\repo", isCommitVsCommit: false);

        // No prior selection -> nothing to preserve, nothing to clear.
        vm.SelectedEntry.Should().BeNull();

        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs"), MakeChange("src/b.cs") },
            @"C:\repo", isCommitVsCommit: false);

        vm.SelectedEntry.Should().BeNull();
    }

    [Fact]
    public void LoadFromChanges_IsReloading_IsTrueDuringRebuild_AndFalseAfter()
    {
        // The IsReloading gate is what suppresses MainViewModel's reaction
        // to transient SelectedEntry changes during a rebuild. Verify the
        // flag is observable inside the FlatEntries.Reset notification --
        // that's the exact moment the WPF ListBox would write null back
        // via its TwoWay binding -- and is reset before any post-reload
        // consolidated PropertyChanged fires.
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs") },
            @"C:\repo", isCommitVsCommit: false);

        bool? isReloadingAtResetTime = null;
        bool? isReloadingAtFinalNotificationTime = null;
        vm.FlatEntries.CollectionChanged += (s, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset
                && isReloadingAtResetTime is null)
            {
                isReloadingAtResetTime = vm.IsReloading;
            }
        };
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FileListViewModel.SelectedEntry))
            {
                isReloadingAtFinalNotificationTime = vm.IsReloading;
            }
        };

        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs") },
            @"C:\repo", isCommitVsCommit: false);

        isReloadingAtResetTime.Should().Be(true,
            "the gate must be active while FlatEntries.Clear() fires - that's when " +
            "the bound ListBox would write null back to SelectedEntry");
        vm.IsReloading.Should().BeFalse(
            "the gate must be cleared by the time LoadFromChanges returns");
        if (isReloadingAtFinalNotificationTime is not null)
        {
            isReloadingAtFinalNotificationTime.Should().BeFalse(
                "any SelectedEntry PropertyChanged that consumers actually see must " +
                "fire AFTER the gate is cleared, not during the rebuild");
        }
    }

    [Fact]
    public void LoadFromChanges_FiresConsolidatedSelectedEntryNotification_AfterReloadCompletes()
    {
        // The consolidated PropertyChanged is the second half of the gate
        // mechanism: intermediates were suppressed, so we MUST fire once at
        // the end so MainViewModel can process the final state. Verify it
        // fires when the SelectedEntry reference changes across the
        // rebuild (the common "selection survived as a new VM instance"
        // case for a same-file refresh).
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs"), MakeChange("src/b.cs") },
            @"C:\repo", isCommitVsCommit: false);
        vm.SelectedEntry = vm.FlatEntries.Single(e => e.Change.Path == "src/b.cs");

        // Track SelectedEntry notifications that the consumer would
        // actually see (i.e. fired with IsReloading=false).
        int consumerVisibleSelectedEntryFires = 0;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FileListViewModel.SelectedEntry) && !vm.IsReloading)
            {
                consumerVisibleSelectedEntryFires++;
            }
        };

        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs"), MakeChange("src/b.cs") },
            @"C:\repo", isCommitVsCommit: false);

        consumerVisibleSelectedEntryFires.Should().Be(1,
            "exactly one consolidated SelectedEntry notification must fire after the " +
            "rebuild completes - intermediates are suppressed by the IsReloading gate");
        vm.SelectedEntry!.Change.Path.Should().Be("src/b.cs",
            "the final state should be the restored selection on the new VM instance");
    }

    [Fact]
    public void LoadFromChanges_DoesNotFireSelectedEntryNotification_WhenSelectionUnchanged()
    {
        // Optimisation guard: if there was no prior selection and there is
        // no new selection, firing the consolidated PropertyChanged would
        // trigger an unnecessary placeholder reload in MainViewModel. Skip
        // the fire when the reference didn't change across the rebuild.
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs") },
            @"C:\repo", isCommitVsCommit: false);

        // No selection at all (priorSelectedEntry == null).
        vm.SelectedEntry.Should().BeNull();

        int selectedEntryFires = 0;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FileListViewModel.SelectedEntry))
            {
                selectedEntryFires++;
            }
        };

        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs"), MakeChange("src/b.cs") },
            @"C:\repo", isCommitVsCommit: false);

        selectedEntryFires.Should().Be(0,
            "no notification should fire when the SelectedEntry reference is unchanged " +
            "(null before, null after) - the consumer has nothing to do");
    }

    [Fact]
    public void DisplayMode_Switching_RecomputesEntryDisplayPaths()
    {
        var vm = new FileListViewModel();
        vm.LoadFromChanges(new[] { MakeChange("src/foo.cs") }, @"C:\repo", isCommitVsCommit: true);
        var entry = vm.FlatEntries[0];

        vm.DisplayMode = FileListDisplayMode.RepoRelative;
        entry.DisplayPath.Should().Be(@"src\foo.cs");

        vm.DisplayMode = FileListDisplayMode.FullPath;
        entry.DisplayPath.Should().Be(@"C:\repo\src\foo.cs");

        vm.DisplayMode = FileListDisplayMode.GroupedByDirectory;
        entry.DisplayPath.Should().Be("foo.cs");
    }

    [Fact]
    public void IsFullPathMode_Setter_UpdatesDisplayMode()
    {
        var vm = new FileListViewModel();
        vm.IsFullPathMode = true;
        vm.DisplayMode.Should().Be(FileListDisplayMode.FullPath);

        vm.IsRepoRelativeMode = true;
        vm.DisplayMode.Should().Be(FileListDisplayMode.RepoRelative);

        vm.IsGroupedByDirectoryMode = true;
        vm.DisplayMode.Should().Be(FileListDisplayMode.GroupedByDirectory);
    }

    // ---- Section layering + identity (formerly FlatGroupedView coverage) ----

    [Fact]
    public void Sections_AreOrderedByCanonicalLayerOrder()
    {
        var vm = new FileListViewModel();
        var changes = new[]
        {
            MakeChange("u.txt", Models.FileStatus.Untracked, WorkingTreeLayer.Untracked),
            MakeChange("s.cs", layer: WorkingTreeLayer.Staged),
            MakeChange("c.cs", Models.FileStatus.Conflicted, WorkingTreeLayer.Conflicted),
            MakeChange("w.cs", layer: WorkingTreeLayer.Unstaged),
            MakeChange("h.cs", layer: WorkingTreeLayer.CommittedSinceCommit),
        };

        vm.LoadFromChanges(changes, @"C:\repo", isCommitVsCommit: false);

        vm.Sections.Select(s => s.Layer).Should().Equal(
            WorkingTreeLayer.Conflicted,
            WorkingTreeLayer.CommittedSinceCommit,
            WorkingTreeLayer.Staged,
            WorkingTreeLayer.Unstaged,
            WorkingTreeLayer.Untracked);
    }

    [Fact]
    public void Sections_EntriesWithinSection_AreSortedByRepoRelativePath()
    {
        var vm = new FileListViewModel();
        var changes = new[]
        {
            MakeChange("src/zeta.cs", layer: WorkingTreeLayer.Unstaged),
            MakeChange("src/alpha.cs", layer: WorkingTreeLayer.Unstaged),
            MakeChange("src/mu.cs", layer: WorkingTreeLayer.Unstaged),
        };

        vm.LoadFromChanges(changes, @"C:\repo", isCommitVsCommit: false);

        var unstaged = vm.Sections.Single(s => s.Layer == WorkingTreeLayer.Unstaged);
        unstaged.Entries.Select(e => e.Change.Path).Should().Equal(
            "src/alpha.cs", "src/mu.cs", "src/zeta.cs");
    }

    [Fact]
    public void Sections_SuppressGrouping_WhenCommitVsCommit()
    {
        var vm = new FileListViewModel();
        var changes = new[]
        {
            MakeChange("a.cs", layer: WorkingTreeLayer.None),
            MakeChange("b.cs", layer: WorkingTreeLayer.None),
        };

        vm.LoadFromChanges(changes, @"C:\repo", isCommitVsCommit: true);

        vm.Sections.Should().HaveCount(1);
        vm.Sections[0].Header.Should().Be("Changes");
    }

    [Fact]
    public void Sections_SharedHeader_PersistsAcrossLoadFromChanges()
    {
        // Header identity survives a rebuild so the user's collapse state
        // (held on FileListSectionHeader.IsExpanded) doesn't reset every
        // time the file list is refreshed.
        var vm = new FileListViewModel();
        vm.LoadFromChanges(new[] { MakeChange("a.cs") }, @"C:\repo", isCommitVsCommit: false);
        var firstHeader = vm.Sections.Single().SharedHeader;

        vm.LoadFromChanges(new[] { MakeChange("a.cs"), MakeChange("b.cs") }, @"C:\repo", isCommitVsCommit: false);
        var secondHeader = vm.Sections.Single().SharedHeader;

        secondHeader.Should().BeSameAs(firstHeader);
    }

    [Fact]
    public void Sections_PreserveCollapseState_AcrossLoadFromChanges()
    {
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("a.cs", layer: WorkingTreeLayer.Unstaged) },
            @"C:\repo", isCommitVsCommit: false);

        vm.Sections.Single().SharedHeader.IsExpanded = false;

        vm.LoadFromChanges(
            new[] { MakeChange("a.cs", layer: WorkingTreeLayer.Unstaged), MakeChange("b.cs", layer: WorkingTreeLayer.Unstaged) },
            @"C:\repo", isCommitVsCommit: false);

        vm.Sections.Single().SharedHeader.IsExpanded.Should().BeFalse();
    }

    // ---- Children projection (drives the unified TreeView) ----

    [Fact]
    public void Children_AreFileEntries_WhenDisplayModeIsFlat()
    {
        var vm = new FileListViewModel { DisplayMode = FileListDisplayMode.RepoRelative };
        var changes = new[]
        {
            MakeChange("src/a.cs", layer: WorkingTreeLayer.Unstaged),
            MakeChange("src/b.cs", layer: WorkingTreeLayer.Unstaged),
        };

        vm.LoadFromChanges(changes, @"C:\repo", isCommitVsCommit: false);

        var section = vm.Sections.Single();
        section.Children.Should().AllBeOfType<FileEntryViewModel>();
        section.Children.Cast<FileEntryViewModel>()
            .Select(e => e.Change.Path)
            .Should().Equal("src/a.cs", "src/b.cs");
    }

    [Fact]
    public void Children_AreDirectoryRoots_WhenDisplayModeIsGroupedByDirectory()
    {
        var vm = new FileListViewModel { DisplayMode = FileListDisplayMode.GroupedByDirectory };
        var changes = new[]
        {
            MakeChange("src/a.cs", layer: WorkingTreeLayer.Unstaged),
            MakeChange("tests/b.cs", layer: WorkingTreeLayer.Unstaged),
        };

        vm.LoadFromChanges(changes, @"C:\repo", isCommitVsCommit: false);

        var section = vm.Sections.Single();
        section.Children.Should().AllBeOfType<DirectoryNodeViewModel>();
        section.Children.Should().HaveSameCount(section.RootItems);
    }

    [Fact]
    public void Children_GroupedByDirectory_RepoRootFilesSurfaceAsSiblings_NotEmptyDirectoryNode()
    {
        // Regression test: when a section contains both repo-root files
        // and files in subdirectories, the root files must appear as
        // FileEntryViewModel siblings of the root directories, not as
        // children of a synthetic empty-label DirectoryNodeViewModel
        // (which used to render as an empty header row).
        var vm = new FileListViewModel { DisplayMode = FileListDisplayMode.GroupedByDirectory };
        var changes = new[]
        {
            MakeChange("package.json", layer: WorkingTreeLayer.Unstaged),
            MakeChange("scripts/check.mjs", layer: WorkingTreeLayer.Unstaged),
        };

        vm.LoadFromChanges(changes, @"C:\repo", isCommitVsCommit: false);

        var section = vm.Sections.Single();
        section.Children.Should().HaveCount(2);
        section.Children[0].Should().BeOfType<FileEntryViewModel>()
            .Which.Change.Path.Should().Be("package.json");
        section.Children[1].Should().BeOfType<DirectoryNodeViewModel>()
            .Which.Label.Should().Be("scripts");
    }

    [Fact]
    public void Children_RebuildOnDisplayModeChange()
    {
        var vm = new FileListViewModel { DisplayMode = FileListDisplayMode.RepoRelative };
        vm.LoadFromChanges(
            new[]
            {
                MakeChange("src/a.cs", layer: WorkingTreeLayer.Unstaged),
                MakeChange("tests/b.cs", layer: WorkingTreeLayer.Unstaged),
            },
            @"C:\repo", isCommitVsCommit: false);

        var section = vm.Sections.Single();
        section.Children.Should().AllBeOfType<FileEntryViewModel>();

        vm.DisplayMode = FileListDisplayMode.GroupedByDirectory;
        section.Children.Should().AllBeOfType<DirectoryNodeViewModel>();

        vm.DisplayMode = FileListDisplayMode.FullPath;
        section.Children.Should().AllBeOfType<FileEntryViewModel>();
    }

    [Fact]
    public void SelectedEntry_RoundTripsAcrossLayers_WithoutLosingState()
    {
        // Regression test for the multi-ListBox stale-state bug: clicking
        // file A in section-1, then B in section-2, then A in section-1
        // must end with SelectedEntry == A. Before the single-grouped-
        // ListBox refactor each section had its own Selector and the
        // last-clicked-in Selector held a stale SelectedItem, which made
        // the third click a silent no-op.
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[]
            {
                MakeChange("a.cs", layer: WorkingTreeLayer.Unstaged),
                MakeChange("b.cs", layer: WorkingTreeLayer.Untracked),
            },
            @"C:\repo", isCommitVsCommit: false);

        var a = vm.FlatEntries.Single(e => e.Change.Path == "a.cs");
        var b = vm.FlatEntries.Single(e => e.Change.Path == "b.cs");

        vm.SelectedEntry = a;
        vm.SelectedEntry.Should().BeSameAs(a);

        vm.SelectedEntry = b;
        vm.SelectedEntry.Should().BeSameAs(b);

        vm.SelectedEntry = a;
        vm.SelectedEntry.Should().BeSameAs(a);

        vm.SelectedEntry = b;
        vm.SelectedEntry.Should().BeSameAs(b);
    }

    // ---- Grouped-mode TreeView selection sync (FileEntryViewModel.IsSelected) ----

    [Fact]
    public void IsSelected_FlipToTrue_PromotesEntryToSelectedEntry()
    {
        // Simulates a click in the unified TreeView: the TreeViewItem's
        // IsSelected TwoWay binding pushes true into the entry's
        // IsSelected, and FileListViewModel must promote that to
        // SelectedEntry so the diff pane swaps content.
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("a.cs"), MakeChange("b.cs") },
            @"C:\repo", isCommitVsCommit: false);

        var b = vm.FlatEntries.Single(e => e.Change.Path == "b.cs");
        b.IsSelected = true;

        vm.SelectedEntry.Should().BeSameAs(b);
    }

    [Fact]
    public void SelectedEntry_Setter_FlipsIsSelectedOnNewEntry_AndClearsPrior()
    {
        // The other direction: programmatic SelectedEntry change (F7/F8,
        // refresh-time restoration, etc.) must push IsSelected onto the
        // chosen entry so the TreeView visually highlights and scrolls to
        // it, while clearing the prior entry's IsSelected so the previous
        // tree row is no longer selected.
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("a.cs"), MakeChange("b.cs") },
            @"C:\repo", isCommitVsCommit: false);

        var a = vm.FlatEntries.Single(e => e.Change.Path == "a.cs");
        var b = vm.FlatEntries.Single(e => e.Change.Path == "b.cs");

        vm.SelectedEntry = a;
        a.IsSelected.Should().BeTrue();
        b.IsSelected.Should().BeFalse();

        vm.SelectedEntry = b;
        a.IsSelected.Should().BeFalse();
        b.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void SelectedEntry_Setter_ExpandsAncestorSection_AndDirectories()
    {
        // F7/F8 might land on a file inside a collapsed section or a
        // collapsed directory. The selection must auto-expand its
        // ancestors, otherwise WPF's TreeViewItem auto-scroll lands inside
        // a collapsed branch and the user sees nothing change.
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[]
            {
                MakeChange("src/deep/nested/x.cs", layer: WorkingTreeLayer.Unstaged),
                MakeChange("src/deep/nested/y.cs", layer: WorkingTreeLayer.Unstaged),
            },
            @"C:\repo", isCommitVsCommit: false);

        var section = vm.Sections.Single();
        section.SharedHeader.IsExpanded = false;
        CollapseRecursive(section.RootItems.OfType<DirectoryNodeViewModel>());

        var y = vm.FlatEntries.Single(e => e.Change.Path == "src/deep/nested/y.cs");
        vm.SelectedEntry = y;

        section.SharedHeader.IsExpanded.Should().BeTrue();
        AllAncestorsExpanded(section.RootItems.OfType<DirectoryNodeViewModel>(), y).Should().BeTrue(
            "every directory on the path from the section root down to the selected file must be expanded");

        static void CollapseRecursive(IEnumerable<DirectoryNodeViewModel> nodes)
        {
            foreach (var n in nodes)
            {
                n.IsExpanded = false;
                CollapseRecursive(n.Children);
            }
        }

        static bool AllAncestorsExpanded(IEnumerable<DirectoryNodeViewModel> nodes, FileEntryViewModel target)
        {
            foreach (var node in nodes)
            {
                if (node.Files.Contains(target))
                {
                    return node.IsExpanded;
                }
                if (AllAncestorsExpanded(node.Children, target))
                {
                    return node.IsExpanded;
                }
            }
            return false;
        }
    }

    [Fact]
    public void SelectedEntry_Setter_ExpandsAncestorSection_WhenTargetIsRepoRootFile()
    {
        // Root files now sit directly under the section as
        // FileEntryViewModel siblings of the root directories.
        // Selecting one must still expand the section header, otherwise
        // the auto-scroll lands inside a collapsed section and the user
        // sees nothing change.
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[]
            {
                MakeChange("package.json", layer: WorkingTreeLayer.Unstaged),
                MakeChange("src/a.cs", layer: WorkingTreeLayer.Unstaged),
            },
            @"C:\repo", isCommitVsCommit: false);

        var section = vm.Sections.Single();
        section.SharedHeader.IsExpanded = false;

        var rootFile = vm.FlatEntries.Single(e => e.Change.Path == "package.json");
        vm.SelectedEntry = rootFile;

        section.SharedHeader.IsExpanded.Should().BeTrue();
    }

    [Fact]
    public void LoadFromChanges_UnsubscribesFromOldEntries_NoHandlerLeak()
    {
        // Orphan-handler test: if LoadFromChanges leaks per-entry
        // PropertyChanged subscriptions, an entry from a stale list could
        // still drive SelectedEntry after a refresh — both a memory leak
        // and a correctness hazard. Setting IsSelected=true on an orphaned
        // entry must NOT mutate SelectedEntry.
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("a.cs"), MakeChange("b.cs") },
            @"C:\repo", isCommitVsCommit: false);

        var orphan = vm.FlatEntries.Single(e => e.Change.Path == "b.cs");

        vm.LoadFromChanges(
            new[] { MakeChange("c.cs"), MakeChange("d.cs") },
            @"C:\repo", isCommitVsCommit: false);

        vm.SelectedEntry.Should().BeNull();
        orphan.IsSelected = true;

        vm.SelectedEntry.Should().BeNull("the orphaned entry's PropertyChanged subscription was disposed during rebuild");
    }

    [Fact]
    public void IsSelected_RoundTripsAcrossLayers_ViaSimulatedTreeViewClicks()
    {
        // Grouped-mode equivalent of the flat-mode A→B→A→B regression
        // test, but routed through the IsSelected=true path that mimics
        // the TreeView TwoWay binding. Catches a regression in the
        // entry-IsSelected → SelectedEntry forwarding logic.
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[]
            {
                MakeChange("a.cs", layer: WorkingTreeLayer.Unstaged),
                MakeChange("b.cs", layer: WorkingTreeLayer.Untracked),
            },
            @"C:\repo", isCommitVsCommit: false);

        var a = vm.FlatEntries.Single(e => e.Change.Path == "a.cs");
        var b = vm.FlatEntries.Single(e => e.Change.Path == "b.cs");

        a.IsSelected = true;
        vm.SelectedEntry.Should().BeSameAs(a);

        b.IsSelected = true;
        vm.SelectedEntry.Should().BeSameAs(b);
        a.IsSelected.Should().BeFalse();

        a.IsSelected = true;
        vm.SelectedEntry.Should().BeSameAs(a);
        b.IsSelected.Should().BeFalse();

        b.IsSelected = true;
        vm.SelectedEntry.Should().BeSameAs(b);
        a.IsSelected.Should().BeFalse();
    }
}
