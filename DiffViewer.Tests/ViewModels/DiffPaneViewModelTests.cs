using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;
using ImageMetadata = DiffViewer.Models.ImageMetadata;

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
    public async Task LoadAsync_BinaryImage_WithDecoder_DispatchesToImageDiff()
    {
        var repo = new FakeRepository
        {
            LeftBytesOverride = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            RightBytesOverride = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            LeftIsBinaryOverride = true,
            RightIsBinaryOverride = true,
        };
        var decoder = new FakeImageDecoder();

        await RunOnUiSyncContextAsync(async () =>
        {
            var bitmap = MakeFrozenBitmap(4, 4);
            decoder.DecodeFunc = (_, _) => new ImageDecodeResult(
                bitmap,
                new ImageMetadata(4, 4, 1234, ImageFormat.Png, 1),
                null);

            var vm = new DiffPaneViewModel(repo, imageDecoder: decoder);
            await vm.LoadAsync(Entry(Binary("img.png")));
            await vm.LastLoadTask;

            vm.ImageDiff.Should().NotBeNull();
            vm.ShowImageDiff.Should().BeTrue();
            vm.PlaceholderMessage.Should().BeNull();
            vm.ShowPlaceholder.Should().BeFalse();
            vm.ShowEditors.Should().BeFalse();
            vm.IsLoading.Should().BeFalse();
            decoder.CallCount.Should().Be(2, "both sides have bytes");
        });
    }

    [Fact]
    public async Task LoadAsync_BinaryImage_DecoderFails_FallsBackToBinaryPlaceholder()
    {
        var repo = new FakeRepository
        {
            LeftBytesOverride = new byte[] { 0x00, 0x01, 0x02, 0x03 },
            RightBytesOverride = new byte[] { 0x00, 0x01, 0x02, 0x03 },
            LeftIsBinaryOverride = true,
            RightIsBinaryOverride = true,
        };
        var decoder = new FakeImageDecoder
        {
            DecodeFunc = (_, _) => new ImageDecodeResult(null, null, "fake error"),
        };

        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, imageDecoder: decoder);
            await vm.LoadAsync(Entry(Binary("img.png")));
            await vm.LastLoadTask;

            vm.ImageDiff.Should().BeNull();
            vm.ShowImageDiff.Should().BeFalse();
            vm.PlaceholderMessage.Should().Be(DiffPaneViewModel.BinaryPlaceholderMessage);
            vm.ShowPlaceholder.Should().BeTrue();
            vm.IsLoading.Should().BeFalse();
        });
    }

    [Fact]
    public async Task LoadAsync_BinaryImage_OverThreshold_SkipsDecode()
    {
        var repo = new FakeRepository();
        var settings = new InMemorySettingsServiceForPane(new AppSettings { LargeFileThresholdBytes = 1024 });
        var decoder = new FakeImageDecoder();
        var vm = new DiffPaneViewModel(repo, settingsService: settings, imageDecoder: decoder);

        var change = Binary("huge.png") with
        {
            LeftFileSizeBytes = 5L * 1024 * 1024,
            RightFileSizeBytes = 5L * 1024 * 1024,
        };

        await vm.LoadAsync(Entry(change));

        // Existing precedence: shape (binary) wins over large in the
        // placeholder string, but the important invariant for image
        // dispatch is that the large-file gate prevents the decode
        // attempt entirely.
        vm.ImageDiff.Should().BeNull();
        vm.ShowPlaceholder.Should().BeTrue();
        decoder.CallCount.Should().Be(0, "the large-file gate should win before we try to decode");
        repo.ReadCount.Should().Be(0);
    }

    [Fact]
    public async Task LoadAsync_ImageThenText_ClearsImageDiff()
    {
        var repo = new FakeRepository
        {
            LeftBytesOverride = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            RightBytesOverride = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            LeftIsBinaryOverride = true,
            RightIsBinaryOverride = true,
            LeftText = "alpha\n",
            RightText = "beta\n",
        };
        var decoder = new FakeImageDecoder();

        await RunOnUiSyncContextAsync(async () =>
        {
            var bitmap = MakeFrozenBitmap(2, 2);
            decoder.DecodeFunc = (_, _) => new ImageDecodeResult(
                bitmap,
                new ImageMetadata(2, 2, 4, ImageFormat.Png, 1),
                null);

            var vm = new DiffPaneViewModel(repo, new DiffService(), imageDecoder: decoder);

            await vm.LoadAsync(Entry(Binary("img.png")));
            await vm.LastLoadTask;
            vm.ImageDiff.Should().NotBeNull();

            // Navigate to a text file. The byte overrides above only
            // matter for "img.png"; the text-mode load reads via
            // LeftText / RightText.
            repo.LeftBytesOverride = null;
            repo.RightBytesOverride = null;
            await vm.LoadAsync(Entry(ModifiedTextFile("notes.txt")));
            await vm.LastLoadTask;

            vm.ImageDiff.Should().BeNull();
            vm.ShowImageDiff.Should().BeFalse();
            vm.PlaceholderMessage.Should().BeNull();
            vm.ShowEditors.Should().BeTrue();
        });
    }

    [Fact]
    public async Task LoadAsync_NonImageExtensionBinary_FallsBackToBinaryPlaceholderWithoutDecodeCall()
    {
        var repo = new FakeRepository();
        var decoder = new FakeImageDecoder();
        var vm = new DiffPaneViewModel(repo, imageDecoder: decoder);

        // .exe is binary but not in our supported image extension list;
        // dispatch should short-circuit on extension before reading bytes.
        await vm.LoadAsync(Entry(Binary("setup.exe")));

        vm.ImageDiff.Should().BeNull();
        vm.ShowPlaceholder.Should().BeTrue();
        vm.PlaceholderMessage.Should().Contain("Binary");
        decoder.CallCount.Should().Be(0);
        repo.ReadCount.Should().Be(0);
    }

    private static BitmapSource MakeFrozenBitmap(int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[height * stride];
        var source = BitmapSource.Create(
            width, height, 96, 96,
            PixelFormats.Bgra32, palette: null,
            pixels, stride);
        source.Freeze();
        return source;
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
    public void RequestScrollByLineDelta_RaisesEventWithSignedDelta()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);

        var deltas = new List<int>();
        vm.ScrollByLineDeltaRequested += (_, args) => deltas.Add(args.LineDelta);

        vm.RequestScrollByLineDelta(3);
        vm.RequestScrollByLineDelta(-5);

        deltas.Should().Equal(3, -5);
    }

    [Fact]
    public void RequestScrollByLineDelta_WithZeroDelta_DoesNotFire()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);

        bool fired = false;
        vm.ScrollByLineDeltaRequested += (_, _) => fired = true;

        vm.RequestScrollByLineDelta(0);

        fired.Should().BeFalse();
    }

    [Fact]
    public void RequestScrollByVerticalFraction_RaisesEventWithFraction()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);

        var fractions = new List<double>();
        vm.ScrollByVerticalFractionRequested += (_, args) => fractions.Add(args.Fraction);

        vm.RequestScrollByVerticalFraction(0.25);
        vm.RequestScrollByVerticalFraction(0.75);

        fractions.Should().Equal(0.25, 0.75);
    }

    [Fact]
    public void RequestScrollByVerticalFraction_ClampsOutOfRange()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);

        var fractions = new List<double>();
        vm.ScrollByVerticalFractionRequested += (_, args) => fractions.Add(args.Fraction);

        vm.RequestScrollByVerticalFraction(-0.5);
        vm.RequestScrollByVerticalFraction(1.5);
        vm.RequestScrollByVerticalFraction(double.NaN);

        fractions.Should().Equal(0.0, 1.0);
    }

    [Fact]
    public void RequestScrollByFraction_RaisesScrollRequestedWithMappedLines()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);

        // Seed both documents so the mapping has a non-trivial denominator.
        vm.LeftDocument.Text = string.Join("\n", Enumerable.Range(1, 100));
        vm.RightDocument.Text = string.Join("\n", Enumerable.Range(1, 200));

        ScrollRequestedEventArgs? last = null;
        vm.ScrollRequested += (_, args) => last = args;

        vm.RequestScrollByFraction(0.5);

        last.Should().NotBeNull();
        last!.LeftLine.Should().Be(50);
        last.RightLine.Should().Be(100);
    }

    [Fact]
    public void RequestScrollByFraction_ClampsOutOfRangeFractions()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);
        vm.LeftDocument.Text = string.Join("\n", Enumerable.Range(1, 50));
        vm.RightDocument.Text = string.Join("\n", Enumerable.Range(1, 50));

        var events = new List<ScrollRequestedEventArgs>();
        vm.ScrollRequested += (_, args) => events.Add(args);

        vm.RequestScrollByFraction(-0.1);
        vm.RequestScrollByFraction(1.5);
        vm.RequestScrollByFraction(double.NaN);

        events.Should().HaveCount(2);
        events[0].LeftLine.Should().Be(1);
        events[0].RightLine.Should().Be(1);
        events[1].LeftLine.Should().Be(50);
        events[1].RightLine.Should().Be(50);
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

    // ============================================================
    // Caret-relative F7 / F8 navigation
    // ------------------------------------------------------------
    // Repro for "click between hunks, hit F8, app jumps backwards":
    // before the fix, TryNavigateNextHunkInFile used CurrentHunkIndex + 1
    // and ignored the editor caret. The fix makes navigation read the
    // caret position (pushed in via SetCaretPosition from the view) and
    // pick the next hunk on the user's side.
    // ============================================================

    /// <summary>
    /// Three-hunk fixture: 24 lines with edits at lines 1, 12, and 24.
    /// DiffPlex's 3-line context can't bridge the 10-line gaps, so this
    /// produces three distinct hunks on each side.
    /// </summary>
    private static FakeRepository ThreeHunkRepo()
    {
        // Middle stays the same on both sides; the head/middle/tail lines
        // are mutated to force three hunks at predictable positions.
        var leftMid1 = string.Concat(Enumerable.Range(2, 10).Select(i => $"line{i}\n"));     // lines 2..11
        var leftMid2 = string.Concat(Enumerable.Range(13, 11).Select(i => $"line{i}\n"));    // lines 13..23
        var rightMid1 = leftMid1;
        var rightMid2 = leftMid2;
        return new FakeRepository
        {
            LeftText = "head-old\n" + leftMid1 + "mid-old\n" + leftMid2 + "tail-old\n",
            RightText = "head-new\n" + rightMid1 + "mid-new\n" + rightMid2 + "tail-new\n",
        };
    }

    [Fact]
    public async Task TryNavigateNextHunkInFile_AfterCaretBetweenHunks_GoesToHunkAfterCaret()
    {
        // Repro for the user-reported bug: open file (auto-jumps to hunk 0),
        // click in the context region between hunk 1 and hunk 2, hit F8.
        // Expected: hunk 2 (the change after the caret). Buggy behavior:
        // CurrentHunkIndex + 1 = 0 + 1 = 1 → hunk 1 (backwards from caret).
        var repo = ThreeHunkRepo();
        var diff = new DiffService();

        int? targetIndex = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            vm.CurrentHunks.Count.Should().Be(3);

            vm.JumpToFirstHunk();
            vm.CurrentHunkIndex.Should().Be(0);

            // Drop the caret onto a context line that sits between hunk 1
            // (around line 12) and hunk 2 (around line 24). Line 18 is
            // safely inside the unchanged window.
            vm.SetCaretPosition(ChangeSide.Right, 18);

            vm.TryNavigateNextHunkInFile().Should().BeTrue();
            targetIndex = vm.CurrentHunkIndex;
        });

        targetIndex.Should().Be(2,
            "F8 must navigate to the change AFTER the caret, not the change after the last visited hunk");
    }

    [Fact]
    public async Task TryNavigatePreviousHunkInFile_AfterCaretBetweenHunks_GoesToHunkBeforeCaret()
    {
        var repo = ThreeHunkRepo();
        var diff = new DiffService();

        int? targetIndex = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));

            vm.JumpToLastHunk();
            vm.CurrentHunkIndex.Should().Be(2);

            // Caret in the context region between hunks 1 and 2.
            vm.SetCaretPosition(ChangeSide.Right, 18);

            vm.TryNavigatePreviousHunkInFile().Should().BeTrue();
            targetIndex = vm.CurrentHunkIndex;
        });

        targetIndex.Should().Be(1,
            "F7 must navigate to the change BEFORE the caret, not the change before the last visited hunk");
    }

    [Fact]
    public async Task TryNavigateNextHunkInFile_WhenCaretInsideHunk_AdvancesToNextHunk()
    {
        var repo = ThreeHunkRepo();
        var diff = new DiffService();

        int? targetIndex = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));

            // Caret on the first hunk (line 1 is the head-edit on right).
            vm.SetCaretPosition(ChangeSide.Right, vm.CurrentHunks[0].NewStartLine);

            vm.TryNavigateNextHunkInFile().Should().BeTrue();
            targetIndex = vm.CurrentHunkIndex;
        });

        targetIndex.Should().Be(1, "caret inside hunk i → next change is hunk i+1");
    }

    [Fact]
    public async Task TryNavigatePreviousHunkInFile_WhenCaretInsideHunk_GoesToPriorHunk()
    {
        var repo = ThreeHunkRepo();
        var diff = new DiffService();

        int? targetIndex = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));

            // Caret on the middle hunk.
            vm.SetCaretPosition(ChangeSide.Right, vm.CurrentHunks[1].NewStartLine);

            vm.TryNavigatePreviousHunkInFile().Should().BeTrue();
            targetIndex = vm.CurrentHunkIndex;
        });

        targetIndex.Should().Be(0, "caret inside hunk i → previous change is hunk i-1");
    }

    [Fact]
    public async Task TryNavigateNextHunkInFile_WhenCaretBeforeAllHunks_LandsOnFirstHunk()
    {
        // Hunk 0 starts at line 1, so a caret strictly before all hunks
        // doesn't really exist on the right-side here. Verify the
        // boundary case on the left side instead using a 2-hunk fixture
        // with a clear leading context window.
        var repo = new FakeRepository
        {
            LeftText  = "context-0\ncontext-1\nA\n" + string.Concat(Enumerable.Range(0, 10).Select(_ => "same\n")) + "B\n",
            RightText = "context-0\ncontext-1\nA'\n" + string.Concat(Enumerable.Range(0, 10).Select(_ => "same\n")) + "B'\n",
        };
        var diff = new DiffService();

        int? targetIndex = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            vm.CurrentHunks.Count.Should().Be(2);

            // Caret on line 1 (a context line that DiffPlex still
            // includes in the first hunk's context window). Use the
            // tightest "before all hunks" sample we can: line 1 may be
            // inside hunk 0's context, so jump to last hunk first then
            // place the caret on line 1 to simulate "user scrolled back
            // to the top and clicked".
            vm.JumpToLastHunk();
            vm.SetCaretPosition(ChangeSide.Right, 1);

            // Whether line 1 is inside hunk 0 or before it, "next change"
            // from line 1 should land on either hunk 0 (caret in context
            // before it) or hunk 1 (caret inside hunk 0). Both are
            // forward steps relative to line 1; the regression we're
            // guarding against is "step relative to CurrentHunkIndex=1
            // and return false because we were on the last hunk".
            vm.TryNavigateNextHunkInFile().Should().BeTrue(
                "caret near the top must yield a forward step regardless of last-visited index");
            targetIndex = vm.CurrentHunkIndex;
        });

        targetIndex.Should().BeOneOf(0, 1);
    }

    [Fact]
    public async Task TryNavigateNextHunkInFile_WhenCaretAfterAllHunks_ReturnsFalse()
    {
        var repo = ThreeHunkRepo();
        var diff = new DiffService();

        bool? result = null;
        int? indexAfter = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));

            // Caret past the last hunk (last hunk ends around line 24).
            vm.JumpToFirstHunk();
            int lastEnd = vm.CurrentHunks[^1].NewStartLine + vm.CurrentHunks[^1].NewLineCount;
            vm.SetCaretPosition(ChangeSide.Right, lastEnd + 5);

            result = vm.TryNavigateNextHunkInFile();
            indexAfter = vm.CurrentHunkIndex;
        });

        result.Should().BeFalse(
            "caret after all hunks must report 'no next in this file' so the orchestrator advances");
        indexAfter.Should().Be(0, "no navigation should have occurred");
    }

    [Fact]
    public async Task TryNavigatePreviousHunkInFile_WhenCaretBeforeAllHunks_ReturnsFalse()
    {
        // Need a fixture with a clear lead-in context block so "caret
        // before hunk 0" is reachable. Same shape as the boundary test
        // above but big enough that line 1 is unambiguously in context.
        var repo = new FakeRepository
        {
            LeftText  = string.Concat(Enumerable.Range(1, 10).Select(i => $"ctx{i}\n")) + "A\n" + string.Concat(Enumerable.Range(0, 10).Select(_ => "same\n")) + "B\n",
            RightText = string.Concat(Enumerable.Range(1, 10).Select(i => $"ctx{i}\n")) + "A'\n" + string.Concat(Enumerable.Range(0, 10).Select(_ => "same\n")) + "B'\n",
        };
        var diff = new DiffService();

        bool? result = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));

            vm.JumpToLastHunk();
            // Caret on the very first line, which is well above hunk 0's
            // edit line (around line 11). DiffPlex's context window
            // typically claims 3 lines, so line 1 should be outside.
            vm.SetCaretPosition(ChangeSide.Right, 1);

            result = vm.TryNavigatePreviousHunkInFile();
        });

        result.Should().BeFalse(
            "caret strictly before all hunks must report 'no previous in this file'");
    }

    [Fact]
    public async Task SetCaretPosition_OnLeftSide_DrivesLeftSideNavigation()
    {
        // The user's caret can be on either editor in side-by-side mode.
        // Track them independently so a click on the left editor in the
        // gap between hunks navigates relative to the left-side line
        // numbers (which differ from the right-side ones when hunks
        // change line counts).
        var repo = ThreeHunkRepo();
        var diff = new DiffService();

        int? targetIndex = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));

            // Place the left-side caret in the context gap between hunks
            // 0 and 1 (around old-side line 6 for our fixture).
            vm.SetCaretPosition(ChangeSide.Left, 6);

            vm.TryNavigateNextHunkInFile().Should().BeTrue();
            targetIndex = vm.CurrentHunkIndex;
        });

        targetIndex.Should().Be(1,
            "F8 from a left-editor caret must use left-side line numbers when picking the next hunk");
    }

    [Fact]
    public async Task TryNavigateNextHunkInFile_WithoutAnyCaret_FallsBackToCurrentHunkIndex()
    {
        // Belt-and-braces for the historical contract: tests that call
        // JumpTo* and then TryNavigate* without ever pushing a caret
        // expect the navigation to step relative to CurrentHunkIndex.
        // RaiseHunkNav syncs the tracked caret to the navigated hunk, so
        // the fallback path matters mostly for the pre-first-jump call
        // sequence. Verify the contract directly.
        var repo = ThreeHunkRepo();
        var diff = new DiffService();

        int? targetIndex = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));

            // No SetCaretPosition call; CurrentHunkIndex is still -1
            // (load reset). Stepping forward from -1 must yield hunk 0.
            vm.TryNavigateNextHunkInFile().Should().BeTrue();
            targetIndex = vm.CurrentHunkIndex;
        });

        targetIndex.Should().Be(0);
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
        public byte[]? LeftBytesOverride { get; set; }
        public byte[]? RightBytesOverride { get; set; }
        public bool LeftIsBinaryOverride { get; set; }
        public bool RightIsBinaryOverride { get; set; }
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
            // Byte overrides take precedence so image-dispatch tests can
            // supply arbitrary binary blobs without going through the
            // Encoding.UTF8.GetBytes(text) path.
            if (side == ChangeSide.Left && LeftBytesOverride is not null)
                return new BlobContent(LeftBytesOverride, Encoding.UTF8, string.Empty, LeftIsBinaryOverride, false);
            if (side == ChangeSide.Right && RightBytesOverride is not null)
                return new BlobContent(RightBytesOverride, Encoding.UTF8, string.Empty, RightIsBinaryOverride, false);
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

    /// <summary>
    /// Test double for <see cref="IImageDecoder"/>. Records every call
    /// and returns a canned <see cref="ImageDecodeResult"/> per side so
    /// tests can simulate decode success and failure without spinning
    /// up <see cref="WpfImageDecoder"/>.
    /// </summary>
    private sealed class FakeImageDecoder : IImageDecoder
    {
        public Func<byte[], string?, ImageDecodeResult> DecodeFunc { get; set; } =
            (_, _) => new ImageDecodeResult(null, null, "fake-not-configured");
        public int CallCount;

        public ImageDecodeResult Decode(byte[] bytes, string? path)
        {
            CallCount++;
            return DecodeFunc(bytes, path);
        }
    }

    [Fact]
    public void PredictBandTopFraction_SideBySide_ReturnsScrollFractionVerbatim()
    {
        // In side-by-side mode the right editor's ExtentHeight maps 1:1
        // to RightDocument.LineCount * lineHeight, so the predicted
        // band-top fraction equals the scroll fraction. The inline map
        // is irrelevant for this mode.
        var emptyMap = Array.Empty<(int?, int?)>();
        DiffPaneViewModel.PredictBandTopFraction(
            scrollFraction: 0.42,
            map: emptyMap,
            leftTotal: 100,
            rightTotal: 100,
            isSideBySide: true)
            .Should().Be(0.42);
    }

    [Fact]
    public void PredictBandTopFraction_Inline_EmptyMap_ReturnsScrollFractionVerbatim()
    {
        // Before the inline diff is built (e.g. during initial load) the
        // map is empty and there's nothing to project through; pass the
        // scroll fraction through so the bar still has a sensible ghost
        // position instead of collapsing to 0 or 1.
        DiffPaneViewModel.PredictBandTopFraction(
            scrollFraction: 0.7,
            map: Array.Empty<(int?, int?)>(),
            leftTotal: 50,
            rightTotal: 50,
            isSideBySide: false)
            .Should().Be(0.7);
    }

    [Fact]
    public void PredictBandTopFraction_Inline_PureDeleteInMiddle_ShiftsAwayFromCursor()
    {
        // Left:  a b c d e   (5 lines)   Right: a c d e (4 lines)
        // Inline mapping (1-indexed inline line):
        //   1: a       (1, 1)
        //   2: -b      (2, null)
        //   3: c       (3, 2)
        //   4: d       (4, 3)
        //   5: e       (5, 4)
        // Scroll fraction 0.4 of inlineTotal=5 -> firstInlineIndex=2,
        // i.e. inline line 3 ("c") at top. That maps to leftLine=3,
        // rightLine=2, so leftFrac=2/5=0.4, rightFrac=1/4=0.25, and
        // the predicted band-top fraction is min(0.4, 0.25) = 0.25.
        // (Without this prediction the bar would ghost at 0.4 and then
        // visibly jump down to 0.25 on settle — the inline-mode jiggle.)
        var map = new (int? OldLine, int? NewLine)[]
        {
            (1, 1),
            (2, null),
            (3, 2),
            (4, 3),
            (5, 4),
        };
        DiffPaneViewModel.PredictBandTopFraction(
            scrollFraction: 0.4,
            map: map,
            leftTotal: 5,
            rightTotal: 4,
            isSideBySide: false)
            .Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void PredictBandTopFraction_Inline_FirstInlineLineIsDelete_WalksPastIt()
    {
        // Inline line 1 is a pure delete (oldLine present, newLine null).
        // The prediction must walk forward to find the first non-null
        // newLine — otherwise rightFrac would default to 1.0 (the
        // "missing" sentinel) and the predicted band-top would collapse
        // to leftFrac, which would visibly skew the ghost.
        var map = new (int? OldLine, int? NewLine)[]
        {
            (1, null),  // pure delete at the top of the inline doc
            (2, 1),     // first context line: leftLine=2, rightLine=1
            (3, 2),
            (4, 3),
        };
        // scrollFraction=0 -> firstInlineIndex=0. Walk: map[0] gives
        // predictedLeft=1, predictedRight=null. map[1] gives
        // predictedRight=1. leftFrac=(1-1)/4=0, rightFrac=(1-1)/3=0.
        // min(0,0) = 0.
        DiffPaneViewModel.PredictBandTopFraction(
            scrollFraction: 0.0,
            map: map,
            leftTotal: 4,
            rightTotal: 3,
            isSideBySide: false)
            .Should().Be(0.0);
    }

    [Theory]
    [InlineData(double.NaN, 0.0)]
    [InlineData(-0.5, 0.0)]
    [InlineData(1.5, 1.0)]
    public void PredictBandTopFraction_ClampsAndNormalizesInput(
        double scrollFraction, double expectedNormalized)
    {
        // Out-of-range / NaN inputs are normalized before any further
        // logic. In side-by-side mode that normalized value is returned
        // verbatim, giving us a tidy way to assert the normalization.
        DiffPaneViewModel.PredictBandTopFraction(
            scrollFraction: scrollFraction,
            map: Array.Empty<(int?, int?)>(),
            leftTotal: 10,
            rightTotal: 10,
            isSideBySide: true)
            .Should().Be(expectedNormalized);
    }
}
