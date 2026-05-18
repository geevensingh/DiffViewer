using System.Text;
using System.Windows.Threading;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public class DiffPaneViewModelTests
{
    private static FileChange ModifiedTextFile(string path) =>
        new(
            Path: path,
            OldPath: null,
            Status: Models.FileStatus.Modified,
            ConflictCode: null,
            Layer: WorkingTreeLayer.Unstaged,
            LeftBlobSha: "aaaaaaa", RightBlobSha: "bbbbbbb",
            IsBinary: false,
            LeftFileSizeBytes: null, RightFileSizeBytes: null,
            IsLfsPointer: false, IsSparseNotCheckedOut: false,
            OldMode: 0, NewMode: 0);

    private static FileChange Binary(string path) =>
        ModifiedTextFile(path) with { IsBinary = true };

    private static FileChange Lfs(string path) =>
        ModifiedTextFile(path) with { IsLfsPointer = true };

    private static FileChange Submodule(string path) =>
        new(path, null, Models.FileStatus.SubmoduleMoved, null, WorkingTreeLayer.Unstaged,
            "1111111", "2222222", false, null, null, false, false, 0, 0);

    private static FileChange ModeOnly(string path) =>
        new(path, null, Models.FileStatus.TypeChanged, null, WorkingTreeLayer.Unstaged,
            "abcdefg", "abcdefg", false, null, null, false, false, 0x1A4, 0x1ED); // 0644, 0755

    private static FileEntryViewModel Entry(FileChange change) =>
        new(change, @"C:\repo");

    [Fact]
    public async Task LoadAsync_WithNullEntry_ShowsSelectAFilePlaceholder()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);

        await vm.LoadAsync(null);

        vm.PlaceholderMessage.Should().NotBeNull();
        vm.ShowPlaceholder.Should().BeTrue();
        vm.ShowEditors.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_BinaryFile_ShowsBinaryPlaceholder()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);

        await vm.LoadAsync(Entry(Binary("img.png")));

        vm.ShowPlaceholder.Should().BeTrue();
        vm.PlaceholderMessage.Should().Contain("Binary");
        repo.ReadCount.Should().Be(0);
    }

    [Fact]
    public async Task LoadAsync_LfsPointer_ShowsLfsPlaceholder()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);

        await vm.LoadAsync(Entry(Lfs("big.bin")));

        vm.PlaceholderMessage.Should().Contain("LFS");
        repo.ReadCount.Should().Be(0);
    }

    [Fact]
    public async Task LoadAsync_Submodule_ShowsSubmodulePlaceholder()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);

        await vm.LoadAsync(Entry(Submodule("vendor/lib")));

        vm.PlaceholderMessage.Should().Contain("Submodule");
    }

    [Fact]
    public async Task LoadAsync_ModeOnly_ShowsModePlaceholder()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);

        await vm.LoadAsync(Entry(ModeOnly("script.sh")));

        vm.PlaceholderMessage.Should().Contain("Mode");
    }

    [Fact]
    public async Task LoadAsync_FileExceedingThreshold_ShowsTooLargePlaceholder()
    {
        var repo = new FakeRepository { LeftText = "x", RightText = "y" };
        var settings = new InMemorySettingsServiceForPane(new AppSettings { LargeFileThresholdBytes = 1024 });
        var vm = new DiffPaneViewModel(repo, settingsService: settings);

        var change = ModifiedTextFile("huge.bin") with
        {
            LeftFileSizeBytes = 5L * 1024 * 1024,
            RightFileSizeBytes = 5L * 1024 * 1024,
        };

        await vm.LoadAsync(Entry(change));

        vm.ShowPlaceholder.Should().BeTrue();
        vm.PlaceholderMessage.Should().Contain("too large");
        repo.ReadCount.Should().Be(0, "we should not read blobs above the threshold");
    }

    [Fact]
    public async Task LoadAsync_FileBelowThreshold_DoesNotTriggerTooLargePlaceholder()
    {
        var repo = new FakeRepository { LeftText = "alpha\n", RightText = "beta\n" };
        var settings = new InMemorySettingsServiceForPane(new AppSettings { LargeFileThresholdBytes = 1024 * 1024 });

        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, new DiffService(), settingsService: settings);
            var change = ModifiedTextFile("small.cs") with
            {
                LeftFileSizeBytes = 32,
                RightFileSizeBytes = 32,
            };

            await vm.LoadAsync(Entry(change));

            vm.PlaceholderMessage.Should().BeNull();
        });
    }

    [Fact]
    public void ColorScheme_SeededFromSettingsOnConstruction()
    {
        var repo = new FakeRepository();
        var settings = new InMemorySettingsServiceForPane(new AppSettings
        {
            ColorScheme = ColorSchemeChoice.Preset(ColorSchemePresetName.HighContrast),
        });

        var vm = new DiffPaneViewModel(repo, settingsService: settings);

        vm.CurrentColorScheme.Should().BeSameAs(DiffViewer.Rendering.DiffColorScheme.HighContrast);
    }

    [Fact]
    public void ColorScheme_SettingsChange_FiresColorSchemeChangedAndUpdatesCurrent()
    {
        var repo = new FakeRepository();
        var settings = new InMemorySettingsServiceForPane(new AppSettings
        {
            ColorScheme = ColorSchemeChoice.Preset(ColorSchemePresetName.Classic),
        });
        var vm = new DiffPaneViewModel(repo, settingsService: settings);

        int eventCount = 0;
        vm.ColorSchemeChanged += (_, _) => eventCount++;

        settings.Update(s => s with
        {
            ColorScheme = ColorSchemeChoice.Preset(ColorSchemePresetName.GitHub),
        });

        eventCount.Should().Be(1);
        vm.CurrentColorScheme.Should().BeSameAs(DiffViewer.Rendering.DiffColorScheme.GitHub);
    }

    [Fact]
    public void ColorScheme_UnrelatedSettingsChange_DoesNotFireColorSchemeChanged()
    {
        var repo = new FakeRepository();
        var settings = new InMemorySettingsServiceForPane(new AppSettings
        {
            ColorScheme = ColorSchemeChoice.Preset(ColorSchemePresetName.Classic),
        });
        var vm = new DiffPaneViewModel(repo, settingsService: settings);

        int eventCount = 0;
        vm.ColorSchemeChanged += (_, _) => eventCount++;

        settings.Update(s => s with { FontSize = 14.0 });

        eventCount.Should().Be(0);
    }

    [Fact]
    public void EditorAppearance_SeededFromSettingsOnConstruction()
    {
        var repo = new FakeRepository();
        var settings = new InMemorySettingsServiceForPane(new AppSettings
        {
            FontFamily = "Cascadia Code",
            FontSize = 14.5,
            TabWidth = 2,
            ShowLineNumbers = false,
            WordWrap = true,
        });

        var vm = new DiffPaneViewModel(repo, settingsService: settings);

        vm.FontFamily.Should().Be("Cascadia Code");
        vm.FontSize.Should().Be(14.5);
        vm.TabWidth.Should().Be(2);
        vm.ShowLineNumbers.Should().BeFalse();
        vm.WordWrap.Should().BeTrue();
    }

    [Fact]
    public void EditorAppearance_FontFamilyChange_PushedFromSettings()
    {
        var repo = new FakeRepository();
        var settings = new InMemorySettingsServiceForPane(new AppSettings { FontFamily = "Consolas" });
        var vm = new DiffPaneViewModel(repo, settingsService: settings);

        settings.Update(s => s with { FontFamily = "JetBrains Mono" });

        vm.FontFamily.Should().Be("JetBrains Mono");
    }

    [Fact]
    public void EditorAppearance_TabWidthChange_PushedFromSettings()
    {
        var repo = new FakeRepository();
        var settings = new InMemorySettingsServiceForPane(new AppSettings { TabWidth = 4 });
        var vm = new DiffPaneViewModel(repo, settingsService: settings);

        settings.Update(s => s with { TabWidth = 8 });

        vm.TabWidth.Should().Be(8);
    }

    [Fact]
    public void EditorAppearance_ShowLineNumbersChange_PushedFromSettings()
    {
        var repo = new FakeRepository();
        var settings = new InMemorySettingsServiceForPane(new AppSettings { ShowLineNumbers = true });
        var vm = new DiffPaneViewModel(repo, settingsService: settings);

        settings.Update(s => s with { ShowLineNumbers = false });

        vm.ShowLineNumbers.Should().BeFalse();
    }

    [Fact]
    public void EditorAppearance_WordWrapChange_PushedFromSettings()
    {
        var repo = new FakeRepository();
        var settings = new InMemorySettingsServiceForPane(new AppSettings { WordWrap = false });
        var vm = new DiffPaneViewModel(repo, settingsService: settings);

        settings.Update(s => s with { WordWrap = true });

        vm.WordWrap.Should().BeTrue();
    }

    [Fact]
    public void EditorAppearance_FontSizeChange_PushedFromSettings_DoesNotEcho()
    {
        // Regression guard: the partial OnFontSizeChanged handler writes
        // back to settings, which would re-fire Changed and loop. The
        // _suppressSettingsWrite gate in OnSettingsChanged must block that.
        var repo = new FakeRepository();
        var settings = new InMemorySettingsServiceForPane(new AppSettings { FontSize = 11.0 });
        var vm = new DiffPaneViewModel(repo, settingsService: settings);

        int updateCount = 0;
        settings.Changed += (_, _) => updateCount++;

        settings.Update(s => s with { FontSize = 16.0 });

        vm.FontSize.Should().Be(16.0);
        updateCount.Should().Be(1, "the VM must not echo the settings change back to disk");
    }

    [Fact]
    public void SideVisibility_DefaultsToBoth_WhenNoSettingsService()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);

        vm.SideVisibility.Should().Be(DiffSideVisibility.Both);
        vm.ShowLeftSide.Should().BeTrue();
        vm.ShowRightSide.Should().BeTrue();
        vm.ShowMiddleDivider.Should().BeTrue();
    }

    [Theory]
    [InlineData(DiffSideVisibility.Both, true, true, true)]
    [InlineData(DiffSideVisibility.LeftOnly, true, false, false)]
    [InlineData(DiffSideVisibility.RightOnly, false, true, false)]
    public void SideVisibility_DerivedFlags_MatchEnumValue(
        DiffSideVisibility value, bool expectLeft, bool expectRight, bool expectDivider)
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo) { SideVisibility = value };

        vm.ShowLeftSide.Should().Be(expectLeft);
        vm.ShowRightSide.Should().Be(expectRight);
        vm.ShowMiddleDivider.Should().Be(expectDivider);
    }

    [Fact]
    public void SideVisibility_SeededFromSettingsOnConstruction()
    {
        var repo = new FakeRepository();
        var settings = new InMemorySettingsServiceForPane(new AppSettings
        {
            SideVisibility = DiffSideVisibility.LeftOnly,
        });

        var vm = new DiffPaneViewModel(repo, settingsService: settings);

        vm.SideVisibility.Should().Be(DiffSideVisibility.LeftOnly);
        vm.ShowLeftSide.Should().BeTrue();
        vm.ShowRightSide.Should().BeFalse();
    }

    [Fact]
    public void SideVisibility_Change_PersistsToSettings()
    {
        var repo = new FakeRepository();
        var settings = new InMemorySettingsServiceForPane(new AppSettings());
        var vm = new DiffPaneViewModel(repo, settingsService: settings);

        vm.SideVisibility = DiffSideVisibility.RightOnly;

        settings.Current.SideVisibility.Should().Be(DiffSideVisibility.RightOnly);
    }

    [Fact]
    public void WordWrap_ToolbarChange_PersistsToSettings()
    {
        // Issue #11: the WordWrap toolbar toggle must round-trip to disk
        // the same way SideVisibility does, otherwise the toggle state
        // doesn't survive a restart.
        var repo = new FakeRepository();
        var settings = new InMemorySettingsServiceForPane(new AppSettings { WordWrap = false });
        var vm = new DiffPaneViewModel(repo, settingsService: settings);

        vm.WordWrap = true;

        settings.Current.WordWrap.Should().BeTrue();
    }

    [Fact]
    public void ShowLineNumbers_ToolbarChange_PersistsToSettings()
    {
        // Companion to WordWrap_ToolbarChange_PersistsToSettings: line
        // numbers are also reachable from the toolbar and must persist
        // the same way. Caught while wiring up word wrap (issue #11).
        var repo = new FakeRepository();
        var settings = new InMemorySettingsServiceForPane(new AppSettings { ShowLineNumbers = true });
        var vm = new DiffPaneViewModel(repo, settingsService: settings);

        vm.ShowLineNumbers = false;

        settings.Current.ShowLineNumbers.Should().BeFalse();
    }

    [Fact]
    public void SideVisibility_ExternalSettingsChange_PushedToViewModel()
    {
        var repo = new FakeRepository();
        var settings = new InMemorySettingsServiceForPane(new AppSettings
        {
            SideVisibility = DiffSideVisibility.Both,
        });
        var vm = new DiffPaneViewModel(repo, settingsService: settings);

        settings.Update(s => s with { SideVisibility = DiffSideVisibility.LeftOnly });

        vm.SideVisibility.Should().Be(DiffSideVisibility.LeftOnly);
    }

    [Fact]
    public void SideVisibility_Change_RaisesDerivedFlagPropertyChanged()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo) { SideVisibility = DiffSideVisibility.Both };

        var changed = new HashSet<string>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName is not null) changed.Add(e.PropertyName); };

        vm.SideVisibility = DiffSideVisibility.LeftOnly;

        changed.Should().Contain(nameof(DiffPaneViewModel.SideVisibility));
        changed.Should().Contain(nameof(DiffPaneViewModel.ShowLeftSide));
        changed.Should().Contain(nameof(DiffPaneViewModel.ShowRightSide));
        changed.Should().Contain(nameof(DiffPaneViewModel.ShowMiddleDivider));
    }

    [Fact]
    public async Task SideVisibility_Change_RebuildsInlineDocument()
    {
        // OnSideVisibilityChanged must invalidate the inline doc — inline
        // mode filters by SideVisibility (LeftOnly emits the left file,
        // RightOnly the right). Without the rebuild, toggling the toolbar
        // in inline mode would leave the user looking at stale content.
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\nBETA\n",
        };
        var diff = new DiffService();

        string? bothText = null;
        string? leftOnlyText = null;
        string? rightOnlyText = null;

        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));

            bothText = vm.InlineDocument.Text;

            vm.SideVisibility = DiffSideVisibility.LeftOnly;
            leftOnlyText = vm.InlineDocument.Text;

            vm.SideVisibility = DiffSideVisibility.RightOnly;
            rightOnlyText = vm.InlineDocument.Text;
        });

        // Both: full unified weave — deletion and insertion both present.
        bothText.Should().Contain("beta").And.Contain("BETA");
        // LeftOnly: left file verbatim — has 'beta', no 'BETA'.
        leftOnlyText.Should().Be("alpha\nbeta\n");
        // RightOnly: right file verbatim — has 'BETA', no 'beta'.
        rightOnlyText.Should().Be("alpha\nBETA\n");
    }

    private sealed class InMemorySettingsServiceForPane : ISettingsService
    {
        private AppSettings _current;
        public InMemorySettingsServiceForPane(AppSettings initial) => _current = initial;
        public AppSettings Current => _current;
        public SettingsLoadOutcome LastLoadOutcome => SettingsLoadOutcome.Loaded;
        public event EventHandler<SettingsChangedEventArgs>? Changed;
        public void Save(AppSettings updated)
        {
            var prev = _current;
            _current = updated;
            Changed?.Invoke(this, new SettingsChangedEventArgs(prev, _current));
        }
        public AppSettings Update(Func<AppSettings, AppSettings> mutate)
        {
            Save(mutate(_current));
            return _current;
        }
    }

    [Fact]
    public async Task LoadAsync_TextFile_PopulatesBothDocuments()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\ngamma\n",
        };

        string? leftText = null;
        string? rightText = null;
        bool? showEditors = null;
        bool? showPlaceholder = null;

        await RunOnUiSyncContextAsync(async () =>
        {
            // The TextDocument is a DispatcherObject - construct it on the
            // dispatcher thread so the LoadAsync continuation can write to it,
            // and read its Text inside the same thread.
            var vm = new DiffPaneViewModel(repo);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            leftText = vm.LeftDocument.Text;
            rightText = vm.RightDocument.Text;
            showEditors = vm.ShowEditors;
            showPlaceholder = vm.ShowPlaceholder;
        });

        leftText.Should().Be("alpha\nbeta\n");
        rightText.Should().Be("alpha\ngamma\n");
        showEditors.Should().BeTrue();
        showPlaceholder.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_TextFile_WithDiffService_PopulatesHighlightMap()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\nBETA\n",
        };
        var diff = new DiffService();

        int leftLineCount = 0;
        int rightLineCount = 0;
        int eventFireCount = 0;

        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            vm.HighlightMapChanged += (_, _) => eventFireCount++;
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            leftLineCount = vm.HighlightMap.LeftLines.Count;
            rightLineCount = vm.HighlightMap.RightLines.Count;
        });

        leftLineCount.Should().BeGreaterThan(0);
        rightLineCount.Should().BeGreaterThan(0);
        eventFireCount.Should().Be(1);
    }

    [Fact]
    public async Task LoadAsync_WhitespaceOnlyDiff_WithIgnoreWhitespace_ShowsBanner()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha   \nbeta\n",
        };
        var diff = new DiffService();

        bool? bannerVisible = null;
        int? hunkCount = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff)
            {
                IgnoreWhitespace = true,
            };
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            bannerVisible = vm.IsWhitespaceOnlyBannerVisible;
            hunkCount = vm.HighlightMap.LeftLines.Count + vm.HighlightMap.RightLines.Count;
        });

        bannerVisible.Should().BeTrue();
        hunkCount.Should().Be(0);
    }

    [Fact]
    public async Task LoadAsync_WhitespaceOnlyDiff_WithoutIgnoreWhitespace_HidesBanner()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha   \nbeta\n",
        };
        var diff = new DiffService();

        bool? bannerVisible = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            bannerVisible = vm.IsWhitespaceOnlyBannerVisible;
        });

        bannerVisible.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_NoActualDifference_DoesNotShowBanner()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\nbeta\n",
        };
        var diff = new DiffService();

        bool? bannerVisible = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff)
            {
                IgnoreWhitespace = true,
            };
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            bannerVisible = vm.IsWhitespaceOnlyBannerVisible;
        });

        bannerVisible.Should().BeFalse();
    }

    [Fact]
    public async Task IgnoreWhitespaceToggle_AfterLoad_RecomputesDiffAndUpdatesBanner()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha   \nbeta\n",
        };
        var diff = new DiffService();

        bool? bannerAfterToggleOn = null;
        bool? bannerAfterToggleOff = null;
        int eventCount = 0;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            vm.HighlightMapChanged += (_, _) => eventCount++;

            vm.IgnoreWhitespace = true;
            bannerAfterToggleOn = vm.IsWhitespaceOnlyBannerVisible;

            vm.IgnoreWhitespace = false;
            bannerAfterToggleOff = vm.IsWhitespaceOnlyBannerVisible;
        });

        bannerAfterToggleOn.Should().BeTrue();
        bannerAfterToggleOff.Should().BeFalse();
        eventCount.Should().Be(2);
    }

    [Fact]
    public async Task ShowIntraLineDiff_DefaultsToTrue()
    {
        var repo = new FakeRepository();
        var diff = new DiffService();

        bool? intra = null;
        await RunOnUiSyncContextAsync(() =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            intra = vm.ShowIntraLineDiff;
            return Task.CompletedTask;
        });

        intra.Should().BeTrue();
    }

    [Fact]
    public async Task IsSideBySide_FalseFlipsShowInline()
    {
        var repo = new FakeRepository { LeftText = "a\n", RightText = "b\n" };
        var diff = new DiffService();

        bool? sideBefore = null, inlineBefore = null, sideAfter = null, inlineAfter = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            sideBefore = vm.ShowSideBySide;
            inlineBefore = vm.ShowInline;

            vm.IsSideBySide = false;
            sideAfter = vm.ShowSideBySide;
            inlineAfter = vm.ShowInline;
        });

        sideBefore.Should().BeTrue();
        inlineBefore.Should().BeFalse();
        sideAfter.Should().BeFalse();
        inlineAfter.Should().BeTrue();
    }

    [Fact]
    public async Task IsLiveUpdatesAvailable_ReflectsCommitVsCommitFlag()
    {
        var repo = new FakeRepository();

        bool? wtAvailable = null;
        bool? cvcAvailable = null;
        await RunOnUiSyncContextAsync(() =>
        {
            var workingTreeVm = new DiffPaneViewModel(repo, isCommitVsCommit: false);
            var commitVsCommitVm = new DiffPaneViewModel(repo, isCommitVsCommit: true);
            wtAvailable = workingTreeVm.IsLiveUpdatesAvailable;
            cvcAvailable = commitVsCommitVm.IsLiveUpdatesAvailable;
            return Task.CompletedTask;
        });

        wtAvailable.Should().BeTrue();
        cvcAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task JumpToHunk_NavigatesToTheGivenIndexAndRaisesEvent()
    {
        var repo = new FakeRepository
        {
            LeftText = "one\ntwo\nthree\nfour\nfive\nsix\nseven\neight\n",
            RightText = "ONE\ntwo\nthree\nfour\nfive\nsix\nseven\nEIGHT\n",
        };
        var diff = new DiffService();

        int? visitedIndex = null;
        int? hunkCount = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            vm.HunkNavigationRequested += (_, args) => visitedIndex = args.HunkIndex;
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            hunkCount = vm.CurrentHunks.Count;
            vm.JumpToHunk(0);
        });

        hunkCount.Should().BeGreaterThan(0);
        visitedIndex.Should().Be(0);
    }

    [Fact]
    public async Task JumpToHunk_WithOutOfRangeIndex_DoesNothing()
    {
        var repo = new FakeRepository
        {
            LeftText = "one\ntwo\nthree\nfour\nfive\nsix\nseven\neight\n",
            RightText = "ONE\ntwo\nthree\nfour\nfive\nsix\nseven\nEIGHT\n",
        };
        var diff = new DiffService();

        bool eventFired = false;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            vm.HunkNavigationRequested += (_, _) => eventFired = true;
            vm.JumpToHunk(-1);
            vm.JumpToHunk(int.MaxValue);
        });

        eventFired.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_TextFile_PopulatesInlineDocument()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\ngamma\n",
        };
        var diff = new DiffService();

        string? inlineText = null;
        int? lineKindCount = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            inlineText = vm.InlineDocument.Text;
            lineKindCount = vm.InlineLineHighlights.Count;
        });

        inlineText.Should().NotBeNullOrEmpty();
        // Full-file inline view: `alpha` survives as context, `beta` is
        // removed, `gamma` is inserted. Lines are emitted verbatim — no
        // +/- prefix and no @@ headers (the InlineDiffBackgroundRenderer
        // tints removed/inserted lines, the same channel side-by-side mode
        // uses). That's the BuildFullFile contract.
        inlineText.Should().Contain("alpha");
        inlineText.Should().Contain("beta");
        inlineText.Should().Contain("gamma");
        inlineText.Should().NotContain("@@");
        inlineText.Should().NotContain("-beta");
        inlineText.Should().NotContain("+gamma");
        lineKindCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HunkAtLine_ReturnsHunkContainingCaret_AndNullInContext()
    {
        // 16 lines, head + tail edits with 12 lines of unchanged middle
        // wide enough that DiffPlex's 3-line context can't bridge them —
        // produces two hunks. Line 8 sits in the middle gap.
        var leftMid = string.Concat(Enumerable.Range(2, 14).Select(i => $"line{i}\n"));
        var rightMid = string.Concat(Enumerable.Range(2, 14).Select(i => $"line{i}\n"));
        var repo = new FakeRepository
        {
            LeftText = "one\n" + leftMid + "sixteen\n",
            RightText = "ONE\n" + rightMid + "SIXTEEN\n",
        };
        var diff = new DiffService();

        DiffHunk? hitHead = null;
        DiffHunk? hitTail = null;
        DiffHunk? missMid = null;
        int hunkCount = 0;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            hunkCount = vm.CurrentHunks.Count;
            hitHead = vm.HunkAtLine(ChangeSide.Right, 1);
            missMid = vm.HunkAtLine(ChangeSide.Right, 8);
            hitTail = vm.HunkAtLine(ChangeSide.Right, 16);
        });

        hunkCount.Should().BeGreaterThan(1);
        hitHead.Should().NotBeNull();
        hitTail.Should().NotBeNull();
        missMid.Should().BeNull();
    }

    [Fact]
    public async Task BuildHunkPatchInputs_PopulatesPathAndCachedSources()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\ngamma\n",
        };
        var diff = new DiffService();

        HunkPatchInputs? inputs = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            var hunk = vm.CurrentHunks.First();
            inputs = vm.BuildHunkPatchInputs(hunk);
        });

        inputs.Should().NotBeNull();
        inputs!.FilePath.Should().Be("a.cs");
        inputs.LeftSource.Should().Be("alpha\nbeta\n");
        inputs.RightSource.Should().Be("alpha\ngamma\n");
    }

    [Fact]
    public async Task UpdateRightClickContext_OnUnstagedHunk_EnablesStageAndRevert()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\ngamma\n",
        };
        var diff = new DiffService();

        bool? canStage = null, canUnstage = null, canRevert = null, isInHunk = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));      // Layer = Unstaged
            var hunk = vm.CurrentHunks.First();
            int line = hunk.NewStartLine;
            vm.UpdateRightClickContext(new HunkActionContext(ChangeSide.Right, line));
            canStage = vm.CanStageHunkAtCaret;
            canUnstage = vm.CanUnstageHunkAtCaret;
            canRevert = vm.CanRevertHunkAtCaret;
            isInHunk = vm.IsCaretInHunk;
        });

        isInHunk.Should().BeTrue();
        canStage.Should().BeTrue();
        canRevert.Should().BeTrue();
        canUnstage.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateRightClickContext_OnStagedHunk_EnablesUnstageOnly()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\ngamma\n",
        };
        var diff = new DiffService();

        bool? canStage = null, canUnstage = null, canRevert = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            var staged = ModifiedTextFile("a.cs") with { Layer = WorkingTreeLayer.Staged };
            await vm.LoadAsync(Entry(staged));
            var hunk = vm.CurrentHunks.First();
            vm.UpdateRightClickContext(new HunkActionContext(ChangeSide.Right, hunk.NewStartLine));
            canStage = vm.CanStageHunkAtCaret;
            canUnstage = vm.CanUnstageHunkAtCaret;
            canRevert = vm.CanRevertHunkAtCaret;
        });

        canStage.Should().BeFalse();
        canUnstage.Should().BeTrue();
        canRevert.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateRightClickContext_OnContextLine_AllHunkActionsDisabled()
    {
        // Same wide-gap fixture as HunkAtLine test — line 8 is in the
        // middle context, between the head and tail hunks.
        var leftMid = string.Concat(Enumerable.Range(2, 14).Select(i => $"line{i}\n"));
        var rightMid = string.Concat(Enumerable.Range(2, 14).Select(i => $"line{i}\n"));
        var repo = new FakeRepository
        {
            LeftText = "one\n" + leftMid + "sixteen\n",
            RightText = "ONE\n" + rightMid + "SIXTEEN\n",
        };
        var diff = new DiffService();

        bool? canStage = null, canUnstage = null, canRevert = null, isInHunk = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            vm.UpdateRightClickContext(new HunkActionContext(ChangeSide.Right, 8));
            canStage = vm.CanStageHunkAtCaret;
            canUnstage = vm.CanUnstageHunkAtCaret;
            canRevert = vm.CanRevertHunkAtCaret;
            isInHunk = vm.IsCaretInHunk;
        });

        isInHunk.Should().BeFalse();
        canStage.Should().BeFalse();
        canUnstage.Should().BeFalse();
        canRevert.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_SkipsWork_WhenSameEntryReloadedWithUnchangedContent()
    {
        // Identity-skip fast path: a refresh that selects the same entry
        // again, with unchanged blob identity on both sides, should NOT
        // re-read the blobs, re-run the diff, or fire HighlightMapChanged.
        // This is the optimisation that kills the same-content refresh
        // flash users see when the watcher fires on an unrelated repo
        // event.
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\nBETA\n",
        };
        var diff = new DiffService();

        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            var entry = Entry(ModifiedTextFile("a.cs"));
            await vm.LoadAsync(entry);

            repo.ReadCount.Should().Be(2, "first load reads both sides");
            int highlightEvents = 0;
            vm.HighlightMapChanged += (_, _) => highlightEvents++;

            // Refresh with a fresh FileEntryViewModel wrapping a FileChange
            // with identical SHAs (the realistic refresh shape - the watcher
            // / re-enumeration produces new instances even for unchanged
            // files).
            var refreshed = Entry(ModifiedTextFile("a.cs"));
            await vm.LoadAsync(refreshed);

            repo.ReadCount.Should().Be(2,
                "the identity-skip path must avoid re-reading blobs on a same-content refresh");
            highlightEvents.Should().Be(0,
                "no ApplyResult means no HighlightMapChanged - this is what stops the flash");
            vm.CurrentEntry.Should().BeSameAs(refreshed,
                "the skip path still updates CurrentEntry to the refreshed FileEntryViewModel " +
                "so MainViewModel.isSameFileRefresh sees the new instance on the NEXT refresh");
        });
    }

    [Fact]
    public async Task LoadAsync_ReloadsAgain_WhenLeftSideContentChanges()
    {
        // Counterpart to the skip test: if the underlying content moves,
        // the identity changes, the skip predicate fails, and the load
        // runs in full. This is the safety net that ensures the
        // optimisation never hides a real edit from the user.
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\nBETA\n",
        };
        var diff = new DiffService();

        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            var entry = Entry(ModifiedTextFile("a.cs"));
            await vm.LoadAsync(entry);
            repo.ReadCount.Should().Be(2);

            // Simulate the working-tree file being edited between
            // refreshes: the fake's ProbeSideIdentity is content-derived,
            // so changing LeftText produces a new identity.
            repo.LeftText = "alpha\nbeta-EDITED\n";

            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));

            repo.ReadCount.Should().Be(4,
                "content changed on disk - skip must NOT fire, full reload required");
            vm.LeftDocument.Text.Should().Contain("EDITED",
                "the editor must reflect the new content, not the stale cached buffer");
        });
    }

    [Fact]
    public async Task LoadAsync_DoesNotSkip_WhenSwitchingToDifferentFile()
    {
        // Path mismatch is the most basic skip-defeater; this guards
        // against a future regression where LoadSignature accidentally
        // omits Path / Layer from its equality.
        var repo = new FakeRepository { LeftText = "x\n", RightText = "y\n" };
        var diff = new DiffService();

        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            repo.ReadCount.Should().Be(2);

            await vm.LoadAsync(Entry(ModifiedTextFile("b.cs")));

            repo.ReadCount.Should().Be(4,
                "switching files must always re-read - this is a different path");
        });
    }

    [Fact]
    public async Task LoadAsync_DoesNotSkip_AfterNullEntryDeselection()
    {
        // Selecting nothing then re-selecting the same file should
        // produce a full reload: deselection clears the signature so
        // the editor is repopulated from scratch (otherwise the user
        // would see stale documents from before the deselection).
        var repo = new FakeRepository { LeftText = "alpha\n", RightText = "beta\n" };
        var diff = new DiffService();

        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            repo.ReadCount.Should().Be(2);

            await vm.LoadAsync(null);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));

            repo.ReadCount.Should().Be(4,
                "after deselection, re-selecting the same file must re-read - " +
                "the signature is cleared on null load to avoid serving stale documents");
        });
    }

    [Fact]
    public async Task LoadAsync_SkipsPlaceholderReapply_OnSameBinaryFileRefresh()
    {
        // Placeholder paths (binary, LFS, large-file, mode-only) are
        // also covered by the identity-skip: a refresh on an unchanged
        // binary file shouldn't re-fire HighlightMapChanged either,
        // since that triggers the same redraw machinery on the overview
        // bar and contributes to the flash.
        var repo = new FakeRepository();

        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo);
            await vm.LoadAsync(Entry(Binary("img.png")));
            vm.PlaceholderMessage.Should().Contain("Binary");

            int highlightEvents = 0;
            vm.HighlightMapChanged += (_, _) => highlightEvents++;

            await vm.LoadAsync(Entry(Binary("img.png")));

            highlightEvents.Should().Be(0,
                "same-binary refresh must skip the placeholder ApplyResult " +
                "to avoid re-firing HighlightMapChanged");
        });
    }

    /// <summary>
    /// DiffPaneViewModel.LoadAsync uses TaskScheduler.FromCurrentSynchronizationContext()
    /// for its continuation; awaiting it from a plain xunit thread without a
    /// SynchronizationContext deadlocks. Wrap in a minimal Dispatcher pump.
    /// </summary>
    private static async Task RunOnUiSyncContextAsync(Func<Task> body)
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try { await body(); tcs.SetResult(); }
                catch (Exception ex) { tcs.SetException(ex); }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await tcs.Task;
    }

    private sealed class FakeRepository : IRepositoryService
    {
        public string LeftText { get; set; } = string.Empty;
        public string RightText { get; set; } = string.Empty;
        public int ReadCount;

        public RepositoryShape Shape => new(@"C:\repo", @"C:\repo", @"C:\repo\.git", false, false, false, false, false);
        public IReadOnlyList<FileChange> CurrentChanges { get; } = Array.Empty<FileChange>();

        public event EventHandler<ChangeListUpdatedEventArgs>? ChangeListUpdated { add { } remove { } }
        public event EventHandler<RepositoryLostEventArgs>? RepositoryLost { add { } remove { } }

        public string? ResolveCommitIsh(string reference) => reference;
        public CommitMetadata? GetCommitMetadata(string commitIsh) => null;
        public bool ValidateRevisions(string leftRef, string rightRef) => true;
        public IReadOnlyList<FileChange> EnumerateChanges(DiffSide left, DiffSide right) => Array.Empty<FileChange>();

        public BlobContent ReadSide(FileChange change, ChangeSide side)
        {
            ReadCount++;
            var text = side == ChangeSide.Left ? LeftText : RightText;
            return new BlobContent(Encoding.UTF8.GetBytes(text), Encoding.UTF8, text, false, false);
        }

        public BlobIdentity? ProbeSideIdentity(FileChange change, ChangeSide side)
        {
            // Mirror ReadSide observably so the identity-skip fast path
            // exercises naturally: stable content -> stable identity ->
            // skip; mutated LeftText / RightText -> fresh identity ->
            // reload.
            var text = side == ChangeSide.Left ? LeftText : RightText;
            if (text.Length == 0) return BlobIdentity.Empty;
            return BlobIdentity.FromBlob($"fake:{side}:{text.Length}:{text.GetHashCode():X8}");
        }

        public void RefreshIndex() { }
        public FileChange? TryResolveCurrent(string path, WorkingTreeLayer layer) => null;
        public bool TryReopen() => true;
        public bool IsPathIgnored(string repoRelativeForwardSlashPath) => false;
        public (IReadOnlyList<FileChange> Snapshot, IDisposable Subscription) SnapshotAndSubscribe(
            EventHandler<ChangeListUpdatedEventArgs> handler) =>
            (CurrentChanges, new DummyDisposable());
        public void Dispose() { }

        private sealed class DummyDisposable : IDisposable { public void Dispose() { } }
    }
}
