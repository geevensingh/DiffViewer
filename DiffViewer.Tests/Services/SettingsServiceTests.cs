using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DiffViewer.SettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Load_NoFile_UsesDefaults()
    {
        var svc = new SettingsService(_settingsPath);

        svc.LastLoadOutcome.Should().Be(SettingsLoadOutcome.DefaultsUsed);
        svc.Current.Should().BeEquivalentTo(new AppSettings());
    }

    [Fact]
    public void SaveAndReload_RoundTripsAllFields()
    {
        var svc = new SettingsService(_settingsPath);
        var modified = svc.Current with
        {
            IgnoreWhitespace = true,
            ShowIntraLineDiff = false,
            IsSideBySide = false,
            ShowVisibleWhitespace = true,
            LiveUpdates = false,
            SideVisibility = DiffSideVisibility.RightOnly,
            DisplayMode = FileListDisplayMode.GroupedByDirectory,
            LargeFileThresholdBytes = 7L * 1024 * 1024,
            FontFamily = "Cascadia Code",
            FontSize = 14.5,
            TabWidth = 2,
            ShowLineNumbers = false,
            WordWrap = true,
            ColorScheme = ColorSchemeChoice.Preset(ColorSchemePresetName.HighContrast),
            ExternalEditorPath = @"C:\bin\code.cmd",
            ExternalEditorLineArgFormat = "--goto {path}:{line}",
            SuppressRevertHunkConfirmation = true,
            SuppressDeleteFileConfirmation = true,
            WindowState = new WindowStateSnapshot(-50, 75, 1400, 900, IsMaximized: true),
            FileListPaneWidthPixels = 480.0,
        };
        svc.Save(modified);

        var reloaded = new SettingsService(_settingsPath);

        reloaded.LastLoadOutcome.Should().Be(SettingsLoadOutcome.Loaded);
        reloaded.Current.Should().BeEquivalentTo(modified);
    }

    [Fact]
    public void Save_RaisesChangedEvent()
    {
        var svc = new SettingsService(_settingsPath);
        SettingsChangedEventArgs? observed = null;
        svc.Changed += (_, e) => observed = e;

        var updated = svc.Current with { TabWidth = 8 };
        svc.Save(updated);

        observed.Should().NotBeNull();
        observed!.Previous.TabWidth.Should().Be(4); // default
        observed.Current.TabWidth.Should().Be(8);
    }

    [Fact]
    public void Update_AppliesMutationAndPersists()
    {
        var svc = new SettingsService(_settingsPath);
        svc.Update(s => s with { FontSize = 18 });

        var reloaded = new SettingsService(_settingsPath);
        reloaded.Current.FontSize.Should().Be(18);
    }

    [Fact]
    public void Save_UsesAtomicWritePattern_NoTempFileLeftBehind()
    {
        var svc = new SettingsService(_settingsPath);
        svc.Save(svc.Current with { TabWidth = 5 });

        var tmp = _settingsPath + ".tmp";
        File.Exists(_settingsPath).Should().BeTrue();
        File.Exists(tmp).Should().BeFalse("File.Replace should consume the .tmp file");
    }

    [Fact]
    public void Load_CorruptJson_BacksUpAndUsesDefaults()
    {
        File.WriteAllText(_settingsPath, "{ this is not valid json");

        var svc = new SettingsService(_settingsPath);

        svc.LastLoadOutcome.Should().Be(SettingsLoadOutcome.CorruptBackedUp);
        svc.Current.Should().BeEquivalentTo(new AppSettings());

        var backups = Directory.EnumerateFiles(_tempDir, "settings.json.bak.*").ToList();
        backups.Should().HaveCount(1);
    }

    [Fact]
    public void Load_FutureSchemaVersion_BacksUpAndUsesDefaults()
    {
        var future = new JsonObject
        {
            ["schemaVersion"] = AppSettings.CurrentSchemaVersion + 99,
            ["tabWidth"] = 99,
        };
        File.WriteAllText(_settingsPath, future.ToJsonString());

        var svc = new SettingsService(_settingsPath);

        svc.LastLoadOutcome.Should().Be(SettingsLoadOutcome.FutureVersionBackedUp);
        svc.Current.TabWidth.Should().Be(4); // default, not 99
    }

    [Fact]
    public void Load_PreVersionedFile_TreatsAsV0AndMigrates()
    {
        // No schemaVersion field at all - should be treated as v0 and migrated to v1.
        var legacy = new JsonObject
        {
            ["ignoreWhitespace"] = true,
            ["fontSize"] = 13,
        };
        File.WriteAllText(_settingsPath, legacy.ToJsonString());

        var svc = new SettingsService(_settingsPath);

        svc.LastLoadOutcome.Should().Be(SettingsLoadOutcome.Migrated);
        svc.Current.IgnoreWhitespace.Should().BeTrue();
        svc.Current.FontSize.Should().Be(13);
    }

    [Fact]
    public void Load_V1File_MigratesToV2_WindowStateDefaultsToNull()
    {
        // v1 schema (current minus 1) had no windowState field at all.
        // After v1->v2 migration the field should be null and other
        // fields preserved.
        var v1 = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fontSize"] = 17,
            ["tabWidth"] = 3,
        };
        File.WriteAllText(_settingsPath, v1.ToJsonString());

        var svc = new SettingsService(_settingsPath);

        svc.LastLoadOutcome.Should().Be(SettingsLoadOutcome.Migrated);
        svc.Current.FontSize.Should().Be(17);
        svc.Current.TabWidth.Should().Be(3);
        svc.Current.WindowState.Should().BeNull();
    }

    [Fact]
    public void WindowState_RoundTrips_Through_Save_And_Reload()
    {
        var svc = new SettingsService(_settingsPath);
        var snapshot = new WindowStateSnapshot(-200.5, 100, 1280, 720, IsMaximized: true);
        svc.Save(svc.Current with { WindowState = snapshot });

        var reloaded = new SettingsService(_settingsPath);
        reloaded.Current.WindowState.Should().Be(snapshot);
    }

    [Fact]
    public void WindowState_NullByDefault_OnFreshLoad()
    {
        var svc = new SettingsService(_settingsPath);
        svc.Current.WindowState.Should().BeNull();
    }

    [Fact]
    public void Load_MissingFields_UseDefaults()
    {
        var partial = new JsonObject
        {
            ["schemaVersion"] = AppSettings.CurrentSchemaVersion,
            ["tabWidth"] = 7,
        };
        File.WriteAllText(_settingsPath, partial.ToJsonString());

        var svc = new SettingsService(_settingsPath);

        svc.Current.TabWidth.Should().Be(7);
        svc.Current.FontFamily.Should().Be("Consolas"); // default
        svc.Current.LargeFileThresholdBytes.Should().Be(25L * 1024 * 1024); // default
        svc.Current.SideVisibility.Should().Be(DiffSideVisibility.Both); // default
    }

    [Fact]
    public void Load_GarbageSideVisibility_FallsBackToBoth()
    {
        var bad = new JsonObject
        {
            ["schemaVersion"] = AppSettings.CurrentSchemaVersion,
            ["sideVisibility"] = "NotARealValue",
        };
        File.WriteAllText(_settingsPath, bad.ToJsonString());

        var svc = new SettingsService(_settingsPath);

        svc.Current.SideVisibility.Should().Be(DiffSideVisibility.Both);
    }

    [Fact]
    public void ColorScheme_PresetShape_RoundTrips()
    {
        var svc = new SettingsService(_settingsPath);
        svc.Save(svc.Current with { ColorScheme = ColorSchemeChoice.Preset(ColorSchemePresetName.Monochrome) });

        var reloaded = new SettingsService(_settingsPath);
        var preset = reloaded.Current.ColorScheme.Should().BeOfType<ColorSchemeChoice.PresetScheme>().Subject;
        preset.Name.Should().Be(ColorSchemePresetName.Monochrome);
    }

    [Fact]
    public void ColorScheme_CustomShape_RoundTripsAndIsNotOverwrittenByDeserializer()
    {
        var custom = new ColorSchemeColors(
            AddedLineBg: "#aabbcc",
            RemovedLineBg: "#ddeeff",
            ModifiedLineBg: "#112233",
            AddedIntraline: "#445566",
            RemovedIntraline: "#778899");
        var svc = new SettingsService(_settingsPath);
        svc.Save(svc.Current with { ColorScheme = ColorSchemeChoice.Custom(custom) });

        var reloaded = new SettingsService(_settingsPath);
        var c = reloaded.Current.ColorScheme.Should().BeOfType<ColorSchemeChoice.CustomScheme>().Subject;
        c.Colors.Should().Be(custom);
    }

    [Fact]
    public void ColorScheme_HandEditedCustomShape_PreservedThroughLoad()
    {
        // A user hand-edits the file with a custom palette. Loading must
        // preserve it verbatim - no silent coercion to the default preset.
        var hand = new JsonObject
        {
            ["schemaVersion"] = AppSettings.CurrentSchemaVersion,
            ["colorScheme"] = new JsonObject
            {
                ["type"] = "custom",
                ["colors"] = new JsonObject
                {
                    ["addedLineBg"] = "#000001",
                    ["removedLineBg"] = "#000002",
                    ["modifiedLineBg"] = "#000003",
                    ["addedIntraline"] = "#000004",
                    ["removedIntraline"] = "#000005",
                },
            },
        };
        File.WriteAllText(_settingsPath, hand.ToJsonString());

        var svc = new SettingsService(_settingsPath);
        var c = svc.Current.ColorScheme.Should().BeOfType<ColorSchemeChoice.CustomScheme>().Subject;
        c.Colors.AddedLineBg.Should().Be("#000001");
        c.Colors.RemovedIntraline.Should().Be("#000005");
    }

    [Fact]
    public void Save_StampsCurrentSchemaVersionEvenIfCallerPassesOldOne()
    {
        var svc = new SettingsService(_settingsPath);
        var stale = svc.Current with { SchemaVersion = 0 };
        svc.Save(stale);

        var raw = JsonNode.Parse(File.ReadAllText(_settingsPath))!.AsObject();
        raw["schemaVersion"]!.GetValue<int>().Should().Be(AppSettings.CurrentSchemaVersion);
    }

    [Fact]
    public void Load_CreatesParentDirectoryOnFirstSave()
    {
        var deep = Path.Combine(_tempDir, "nested", "deeper", "settings.json");
        var svc = new SettingsService(deep);
        svc.Save(svc.Current with { TabWidth = 6 });

        File.Exists(deep).Should().BeTrue();
    }

    [Fact]
    public void FileListPaneWidthPixels_DefaultsTo320OnFreshLoad()
    {
        var svc = new SettingsService(_settingsPath);

        // The 320 default matches the historical hardcoded XAML value
        // before persistence existed, so first-launch users see the
        // same split they always have.
        svc.Current.FileListPaneWidthPixels.Should().Be(320.0);
    }

    [Fact]
    public void FileListPaneWidthPixels_RoundTrips_Through_Save_And_Reload()
    {
        var svc = new SettingsService(_settingsPath);
        svc.Save(svc.Current with { FileListPaneWidthPixels = 555.5 });

        var reloaded = new SettingsService(_settingsPath);
        reloaded.Current.FileListPaneWidthPixels.Should().Be(555.5);
    }

    [Fact]
    public void Load_V3File_MigratesToV4_FileListPaneWidthDefaultsTo320()
    {
        // v3 schema (current minus 1) had no fileListPaneWidthPixels
        // field. After v3->v4 migration the field should be the
        // built-in default (320) and other fields preserved.
        var v3 = new JsonObject
        {
            ["schemaVersion"] = 3,
            ["fontSize"] = 17,
            ["tabWidth"] = 3,
        };
        File.WriteAllText(_settingsPath, v3.ToJsonString());

        var svc = new SettingsService(_settingsPath);

        svc.LastLoadOutcome.Should().Be(SettingsLoadOutcome.Migrated);
        svc.Current.FontSize.Should().Be(17);
        svc.Current.TabWidth.Should().Be(3);
        svc.Current.FileListPaneWidthPixels.Should().Be(320.0);
    }
}
