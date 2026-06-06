using System.Collections.Generic;
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
            RepoRoots = new[] { @"C:\Repos", @"D:\OSS" },
            DefaultCloneDestination = @"D:\Clones",
            RepoUrlMappings = new Dictionary<RepoUrlKey, string>
            {
                [RepoUrlKey.From("github.com", "geevensingh", "jotjson")] = @"C:\Repos\jotjson",
            },
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

    [Fact]
    public void RepoRoots_RoundTrip_PreservesOrder()
    {
        var svc = new SettingsService(_settingsPath);
        var roots = new[] { @"C:\Repos", @"D:\OSS", @"E:\Forks" };
        svc.Save(svc.Current with { RepoRoots = roots });

        var reloaded = new SettingsService(_settingsPath);
        reloaded.Current.RepoRoots.Should().Equal(roots);
    }

    [Fact]
    public void DefaultCloneDestination_RoundTrips_AndNullStaysNull()
    {
        var svc = new SettingsService(_settingsPath);
        svc.Save(svc.Current with { DefaultCloneDestination = @"D:\Clones" });

        var loaded = new SettingsService(_settingsPath);
        loaded.Current.DefaultCloneDestination.Should().Be(@"D:\Clones");

        // Round-trip null too: previously-set destination cleared.
        loaded.Save(loaded.Current with { DefaultCloneDestination = null });
        var loaded2 = new SettingsService(_settingsPath);
        loaded2.Current.DefaultCloneDestination.Should().BeNull();
    }

    [Fact]
    public void RepoUrlMappings_RoundTrip_NormalizesKeysOnRead()
    {
        var svc = new SettingsService(_settingsPath);
        var mappings = new Dictionary<RepoUrlKey, string>
        {
            [RepoUrlKey.From("github.com", "geevensingh", "jotjson")] = @"C:\Repos\jotjson",
            [RepoUrlKey.From("github.com", "microsoft", "vscode")] = @"D:\OSS\vscode",
        };
        svc.Save(svc.Current with { RepoUrlMappings = mappings });

        var reloaded = new SettingsService(_settingsPath);
        reloaded.Current.RepoUrlMappings.Should().HaveCount(2);
        reloaded.Current.RepoUrlMappings[RepoUrlKey.From("github.com", "geevensingh", "jotjson")]
            .Should().Be(@"C:\Repos\jotjson");
        reloaded.Current.RepoUrlMappings[RepoUrlKey.From("github.com", "microsoft", "vscode")]
            .Should().Be(@"D:\OSS\vscode");
    }

    [Fact]
    public void RepoUrlMappings_OnDiskFormat_UsesPipeKeyEncoding()
    {
        var svc = new SettingsService(_settingsPath);
        svc.Save(svc.Current with
        {
            RepoUrlMappings = new Dictionary<RepoUrlKey, string>
            {
                [RepoUrlKey.From("github.com", "geevensingh", "jotjson")] = @"C:\Repos\jotjson",
            },
        });

        var json = JsonNode.Parse(File.ReadAllText(_settingsPath))!.AsObject();
        var mappings = json["repoUrlMappings"]!.AsObject();
        mappings.Should().ContainKey("github.com|geevensingh|jotjson");
        mappings["github.com|geevensingh|jotjson"]!.GetValue<string>()
            .Should().Be(@"C:\Repos\jotjson");
    }

    [Fact]
    public void RepoUrlMappings_MalformedKeysOnDisk_AreDroppedNotCrashed()
    {
        // A hand-edited file with one broken key and one good key.
        // The good one survives; the broken one is dropped silently
        // (preferable to refusing to load the whole settings file).
        var json = new JsonObject
        {
            ["schemaVersion"] = AppSettings.CurrentSchemaVersion,
            ["repoUrlMappings"] = new JsonObject
            {
                ["github.com|owner|repo"] = @"C:\good",
                ["not-a-valid-key"] = @"C:\bad",
            },
        };
        File.WriteAllText(_settingsPath, json.ToJsonString());

        var svc = new SettingsService(_settingsPath);

        svc.Current.RepoUrlMappings.Should().HaveCount(1);
        svc.Current.RepoUrlMappings[RepoUrlKey.From("github.com", "owner", "repo")]
            .Should().Be(@"C:\good");
    }

    [Fact]
    public void Load_V4File_MigratesToV5_PRReviewFieldsHydrateToDefaults()
    {
        // v4 schema (current minus 2) had no PR-review fields. After
        // v4->v5 migration the three fields should hydrate to their
        // safe defaults (empty list / null / empty dict) and other
        // fields should be preserved unchanged. The follow-up v5->v6
        // migration then adds RenderSvgImage default; both run during
        // a single load() chain so the assertions below cover the
        // full v4->current outcome.
        var v4 = new JsonObject
        {
            ["schemaVersion"] = 4,
            ["fontSize"] = 17,
            ["tabWidth"] = 3,
            ["fileListPaneWidthPixels"] = 400.0,
        };
        File.WriteAllText(_settingsPath, v4.ToJsonString());

        var svc = new SettingsService(_settingsPath);

        svc.LastLoadOutcome.Should().Be(SettingsLoadOutcome.Migrated);
        svc.Current.FontSize.Should().Be(17);
        svc.Current.TabWidth.Should().Be(3);
        svc.Current.FileListPaneWidthPixels.Should().Be(400.0);
        svc.Current.RepoRoots.Should().BeEmpty();
        svc.Current.DefaultCloneDestination.Should().BeNull();
        svc.Current.RepoUrlMappings.Should().BeEmpty();
        svc.Current.RenderSvgImage.Should().BeTrue();
    }

    [Fact]
    public void Load_V5File_MigratesToV6_RenderSvgImageDefaultsToTrue()
    {
        // v5 schema (current minus 1) had no RenderSvgImage field.
        // After v5->v6 migration the field should hydrate to its
        // default (true) and other fields should be preserved.
        // Issue #15.
        var v5 = new JsonObject
        {
            ["schemaVersion"] = 5,
            ["fontSize"] = 18,
            ["tabWidth"] = 5,
            ["isSideBySide"] = false,
        };
        File.WriteAllText(_settingsPath, v5.ToJsonString());

        var svc = new SettingsService(_settingsPath);

        svc.LastLoadOutcome.Should().Be(SettingsLoadOutcome.Migrated);
        svc.Current.FontSize.Should().Be(18);
        svc.Current.TabWidth.Should().Be(5);
        svc.Current.IsSideBySide.Should().BeFalse();
        svc.Current.RenderSvgImage.Should().BeTrue();
    }

    [Fact]
    public void Load_V6File_MigratesToV7_AutoUpdateFieldsHydrateToDefaults()
    {
        // v6 schema (current minus 1) had no auto-update fields.
        // After v6->v7 migration the three fields should hydrate to
        // their safe defaults (NotifyOnly / Daily / false) and other
        // fields should be preserved. Phase 2.2 deliberately defaults
        // AutoUpdate to NotifyOnly so existing installs do not opt
        // into silent updates without explicit user consent.
        var v6 = new JsonObject
        {
            ["schemaVersion"] = 6,
            ["fontSize"] = 19,
            ["tabWidth"] = 7,
            ["isSideBySide"] = false,
            ["renderSvgImage"] = false,
        };
        File.WriteAllText(_settingsPath, v6.ToJsonString());

        var svc = new SettingsService(_settingsPath);

        svc.LastLoadOutcome.Should().Be(SettingsLoadOutcome.Migrated);
        svc.Current.FontSize.Should().Be(19);
        svc.Current.TabWidth.Should().Be(7);
        svc.Current.IsSideBySide.Should().BeFalse();
        svc.Current.RenderSvgImage.Should().BeFalse();
        svc.Current.AutoUpdate.Should().Be(AutoUpdateMode.NotifyOnly);
        svc.Current.UpdateCheckCadence.Should().Be(UpdateCheckCadence.Daily);
        svc.Current.IncludePreReleases.Should().BeFalse();
    }

    [Fact]
    public void Save_Then_Load_RoundTripsAutoUpdateFields()
    {
        var svc1 = new SettingsService(_settingsPath);
        svc1.Save(svc1.Current with
        {
            AutoUpdate = AutoUpdateMode.Disabled,
            UpdateCheckCadence = UpdateCheckCadence.Weekly,
            IncludePreReleases = true,
        });

        var svc2 = new SettingsService(_settingsPath);

        svc2.LastLoadOutcome.Should().Be(SettingsLoadOutcome.Loaded);
        svc2.Current.AutoUpdate.Should().Be(AutoUpdateMode.Disabled);
        svc2.Current.UpdateCheckCadence.Should().Be(UpdateCheckCadence.Weekly);
        svc2.Current.IncludePreReleases.Should().BeTrue();
    }

    [Fact]
    public void Load_V7File_MigratesToV8_SkippedUpdateVersionDefaultsToNull()
    {
        // v7 schema (current minus 1) had no skippedUpdateVersion
        // field. After v7->v8 migration the field should hydrate to
        // null (nothing skipped) and other fields should be
        // preserved. Auto-update fields from v7 carry through
        // unchanged.
        var v7 = new JsonObject
        {
            ["schemaVersion"] = 7,
            ["fontSize"] = 21,
            ["autoUpdate"] = AutoUpdateMode.Automatic.ToString(),
            ["updateCheckCadence"] = UpdateCheckCadence.Hourly.ToString(),
            ["includePreReleases"] = true,
        };
        File.WriteAllText(_settingsPath, v7.ToJsonString());

        var svc = new SettingsService(_settingsPath);

        svc.LastLoadOutcome.Should().Be(SettingsLoadOutcome.Migrated);
        svc.Current.FontSize.Should().Be(21);
        svc.Current.AutoUpdate.Should().Be(AutoUpdateMode.Automatic);
        svc.Current.UpdateCheckCadence.Should().Be(UpdateCheckCadence.Hourly);
        svc.Current.IncludePreReleases.Should().BeTrue();
        svc.Current.SkippedUpdateVersion.Should().BeNull();
    }

    [Fact]
    public void Save_Then_Load_RoundTripsSkippedUpdateVersion()
    {
        var svc1 = new SettingsService(_settingsPath);
        svc1.Save(svc1.Current with { SkippedUpdateVersion = "1.5.0-rc1" });

        var svc2 = new SettingsService(_settingsPath);

        svc2.LastLoadOutcome.Should().Be(SettingsLoadOutcome.Loaded);
        svc2.Current.SkippedUpdateVersion.Should().Be("1.5.0-rc1");
    }

    [Fact]
    public void Load_V8File_MigratesToV9_PreferMarkdownRenderedDefaultsToTrue()
    {
        // v8 schema (current minus 1) had no PreferMarkdownRendered
        // field. After v8->v9 migration the field should hydrate to
        // its default (true) and other fields should be preserved.
        // Same migration shape as v5->v6 added RenderSvgImage.
        var v8 = new JsonObject
        {
            ["schemaVersion"] = 8,
            ["fontSize"] = 22,
            ["renderSvgImage"] = false,
            ["isSideBySide"] = false,
        };
        File.WriteAllText(_settingsPath, v8.ToJsonString());

        var svc = new SettingsService(_settingsPath);

        svc.LastLoadOutcome.Should().Be(SettingsLoadOutcome.Migrated);
        svc.Current.FontSize.Should().Be(22);
        svc.Current.RenderSvgImage.Should().BeFalse();
        svc.Current.IsSideBySide.Should().BeFalse();
        svc.Current.PreferMarkdownRendered.Should().BeTrue(
            "default for the new field, hydrated when missing from a pre-v9 file");
    }

    [Fact]
    public void Save_Then_Load_RoundTripsPreferMarkdownRendered()
    {
        var svc1 = new SettingsService(_settingsPath);
        svc1.Save(svc1.Current with { PreferMarkdownRendered = false });

        var svc2 = new SettingsService(_settingsPath);

        svc2.LastLoadOutcome.Should().Be(SettingsLoadOutcome.Loaded);
        svc2.Current.PreferMarkdownRendered.Should().BeFalse();
    }

    [Fact]
    public void RepoUrlMappings_StableOrderingOnDisk_AcrossSaves()
    {
        // Maps with the same content should serialize to the same JSON
        // regardless of insertion order — important so settings.json
        // diffs cleanly under source control / hand-edit comparisons.
        var svc1 = new SettingsService(_settingsPath);
        svc1.Save(svc1.Current with
        {
            RepoUrlMappings = new Dictionary<RepoUrlKey, string>
            {
                [RepoUrlKey.From("github.com", "z-org", "repo")] = "z",
                [RepoUrlKey.From("github.com", "a-org", "repo")] = "a",
                [RepoUrlKey.From("github.com", "m-org", "repo")] = "m",
            },
        });
        var json1 = File.ReadAllText(_settingsPath);

        var altPath = _settingsPath + ".alt";
        var svc2 = new SettingsService(altPath);
        svc2.Save(svc2.Current with
        {
            RepoUrlMappings = new Dictionary<RepoUrlKey, string>
            {
                [RepoUrlKey.From("github.com", "a-org", "repo")] = "a",
                [RepoUrlKey.From("github.com", "m-org", "repo")] = "m",
                [RepoUrlKey.From("github.com", "z-org", "repo")] = "z",
            },
        });
        var json2 = File.ReadAllText(altPath);

        json1.Should().Be(json2);
    }
}
