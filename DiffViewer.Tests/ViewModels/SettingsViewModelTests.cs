using System;
using System.Collections.Generic;
using System.IO;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;
    private readonly SettingsService _service;

    public SettingsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DiffViewer.SettingsVmTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "settings.json");
        _service = new SettingsService(_settingsPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private SettingsViewModel NewVm() => new(_service, useDispatcherTimer: false);

    [Fact]
    public void Constructor_LoadsCurrentSettings()
    {
        _service.Save(_service.Current with { FontFamily = "Cascadia Code", TabWidth = 2 });
        var vm = NewVm();

        vm.FontFamily.Should().Be("Cascadia Code");
        vm.TabWidth.Should().Be(2);
    }

    [Fact]
    public void Constructor_DoesNotPersistDuringSeed()
    {
        // First construct the file with a non-default value.
        _service.Save(_service.Current with { FontFamily = "Cascadia Code" });
        var bytesBefore = File.ReadAllBytes(_settingsPath);

        // Constructing a VM should NOT rewrite the file just because it
        // pumped the seed values through its observable properties.
        _ = NewVm();

        File.ReadAllBytes(_settingsPath).Should().Equal(bytesBefore);
    }

    [Fact]
    public void Toggle_ShowLineNumbers_PersistsImmediately()
    {
        var vm = NewVm();
        vm.ShowLineNumbers = false;

        new SettingsService(_settingsPath).Current.ShowLineNumbers.Should().BeFalse();
    }

    [Fact]
    public void Toggle_WordWrap_PersistsImmediately()
    {
        var vm = NewVm();
        vm.WordWrap = true;

        new SettingsService(_settingsPath).Current.WordWrap.Should().BeTrue();
    }

    [Fact]
    public void ConfirmRevertHunk_PersistsAsInvertedSuppressFlag()
    {
        var vm = NewVm();
        vm.ConfirmRevertHunk = false;

        new SettingsService(_settingsPath).Current.SuppressRevertHunkConfirmation.Should().BeTrue();
    }

    [Fact]
    public void ConfirmDeleteFile_PersistsAsInvertedSuppressFlag()
    {
        var vm = NewVm();
        vm.ConfirmDeleteFile = false;

        new SettingsService(_settingsPath).Current.SuppressDeleteFileConfirmation.Should().BeTrue();
    }

    [Fact]
    public void NumericInputs_DoNotPersistUntilCommit()
    {
        var vm = NewVm();
        vm.FontSize = 16.0;

        // Until CommitNumericFields() runs, the file still has the default value.
        new SettingsService(_settingsPath).Current.FontSize.Should().Be(11.0);

        vm.CommitNumericFields();
        new SettingsService(_settingsPath).Current.FontSize.Should().Be(16.0);
    }

    [Fact]
    public void TextInputs_DoNotPersistUntilCommit()
    {
        var vm = NewVm();
        vm.ExternalEditorPath = @"C:\bin\code.cmd";

        new SettingsService(_settingsPath).Current.ExternalEditorPath.Should().BeNull();

        vm.CommitNumericFields();
        new SettingsService(_settingsPath).Current.ExternalEditorPath.Should().Be(@"C:\bin\code.cmd");
    }

    [Fact]
    public void CommitNumericFields_ClampsOutOfRange()
    {
        var vm = NewVm();
        vm.FontSize = 999;
        vm.TabWidth = 0;
        vm.LargeFileThresholdMb = 0;

        vm.CommitNumericFields();

        var saved = new SettingsService(_settingsPath).Current;
        saved.FontSize.Should().Be(72.0);
        saved.TabWidth.Should().Be(1);
        saved.LargeFileThresholdBytes.Should().Be(1L * 1024 * 1024);
    }

    [Fact]
    public void CommitNumericFields_NormalizesEmptyTextToNull()
    {
        _service.Save(_service.Current with { ExternalEditorPath = "old", ExternalEditorLineArgFormat = "old" });
        var vm = NewVm();
        vm.ExternalEditorPath = "";
        vm.ExternalEditorLineArgFormat = "   ";

        vm.CommitNumericFields();

        var saved = new SettingsService(_settingsPath).Current;
        saved.ExternalEditorPath.Should().BeNull();
        saved.ExternalEditorLineArgFormat.Should().BeNull();
    }

    [Fact]
    public void ColorPreset_PersistsImmediatelyWhenNoDispatcher()
    {
        var vm = NewVm();
        vm.SelectedColorPreset = ColorSchemePresetName.HighContrast;

        var saved = new SettingsService(_settingsPath).Current.ColorScheme;
        saved.Should().BeOfType<ColorSchemeChoice.PresetScheme>()
            .Which.Name.Should().Be(ColorSchemePresetName.HighContrast);
    }

    [Fact]
    public void ColorPreset_PickingPresetClearsCustomFlag()
    {
        // Seed a custom palette as if someone hand-edited the JSON.
        var custom = new ColorSchemeColors("#aaa", "#bbb", "#ccc", "#ddd", "#eee");
        _service.Save(_service.Current with { ColorScheme = ColorSchemeChoice.Custom(custom) });

        var vm = NewVm();
        vm.IsCustomColorScheme.Should().BeTrue();

        vm.SelectedColorPreset = ColorSchemePresetName.Pale;
        vm.IsCustomColorScheme.Should().BeFalse();

        var saved = new SettingsService(_settingsPath).Current.ColorScheme;
        saved.Should().BeOfType<ColorSchemeChoice.PresetScheme>()
            .Which.Name.Should().Be(ColorSchemePresetName.Pale);
    }

    [Fact]
    public void Constructor_FlagsCustomColorScheme()
    {
        var custom = new ColorSchemeColors("#aaa", "#bbb", "#ccc", "#ddd", "#eee");
        _service.Save(_service.Current with { ColorScheme = ColorSchemeChoice.Custom(custom) });

        var vm = NewVm();
        vm.IsCustomColorScheme.Should().BeTrue();
    }

    [Fact]
    public void ResetAllToDefaults_RewritesFileAndReloadsState()
    {
        _service.Save(_service.Current with
        {
            FontFamily = "Cascadia Code",
            FontSize = 18,
            TabWidth = 8,
            LargeFileThresholdBytes = 99L * 1024 * 1024,
        });

        // Confirm prompt accepts.
        var vm = new SettingsViewModel(_service, confirmReset: _ => true, useDispatcherTimer: false);
        vm.ResetAllToDefaultsCommand.Execute(null);

        var saved = new SettingsService(_settingsPath).Current;
        saved.Should().BeEquivalentTo(new AppSettings());
        vm.FontFamily.Should().Be("Consolas");
        vm.LargeFileThresholdMb.Should().Be(25);
    }

    [Fact]
    public void ResetAllToDefaults_RespectsConfirmCancel()
    {
        _service.Save(_service.Current with { FontFamily = "Cascadia Code" });

        var vm = new SettingsViewModel(_service, confirmReset: _ => false, useDispatcherTimer: false);
        vm.ResetAllToDefaultsCommand.Execute(null);

        new SettingsService(_settingsPath).Current.FontFamily.Should().Be("Cascadia Code");
    }

    [Fact]
    public void OpenSettingsJson_UsesInjectedHandlerWhenProvided()
    {
        string? opened = null;
        var vm = new SettingsViewModel(_service, openInEditor: p => opened = p, useDispatcherTimer: false);

        vm.OpenSettingsJsonCommand.Execute(null);

        opened.Should().Be(SettingsService.DefaultFilePath);
    }

    [Fact]
    public void AvailableFonts_DefaultsToEmpty_WhenNotInjected()
    {
        var vm = NewVm();
        vm.AvailableFonts.Should().NotBeNull();
        vm.AvailableFonts.Should().BeEmpty();
    }

    [Fact]
    public void AvailableFonts_ReflectsInjectedList()
    {
        var fonts = new[]
        {
            new FontFamilyOption("Cascadia Code", IsMonospaced: true),
            new FontFamilyOption("Segoe UI", IsMonospaced: false),
        };
        var vm = new SettingsViewModel(_service, useDispatcherTimer: false, availableFonts: fonts);

        vm.AvailableFonts.Should().BeEquivalentTo(fonts);
    }

    [Fact]
    public void FontFamilyOption_GroupName_MonospacedAndVariable()
    {
        new FontFamilyOption("Consolas", IsMonospaced: true).GroupName.Should().Be("Monospaced");
        new FontFamilyOption("Arial", IsMonospaced: false).GroupName.Should().Be("Variable width");
    }

    // ---------- PR-review settings ----------

    private SettingsViewModel NewVmWithPicker(Func<string?, string?>? pickFolder = null,
                                              Func<string, bool>? confirmDefault = null) =>
        new(_service,
            useDispatcherTimer: false,
            pickFolder: pickFolder,
            confirmRememberDefaultClone: confirmDefault);

    [Fact]
    public void AddRepoRoot_AppendsAndPersists()
    {
        var vm = NewVmWithPicker(pickFolder: _ => @"C:\Repos");

        vm.AddRepoRootCommand.Execute(null);

        vm.RepoRoots.Should().ContainSingle().Which.Should().Be(@"C:\Repos");
        _service.Current.RepoRoots.Should().Equal(@"C:\Repos");
    }

    [Fact]
    public void AddRepoRoot_NoPicker_DoesNotCrash()
    {
        var vm = NewVm(); // no pickFolder injected
        vm.AddRepoRootCommand.Execute(null);
        vm.RepoRoots.Should().BeEmpty();
        vm.StatusMessage.Should().Contain("No folder picker");
    }

    [Fact]
    public void AddRepoRoot_PickerCancelled_NoChange()
    {
        var vm = NewVmWithPicker(pickFolder: _ => null);
        vm.AddRepoRootCommand.Execute(null);
        vm.RepoRoots.Should().BeEmpty();
        _service.Current.RepoRoots.Should().BeEmpty();
    }

    [Fact]
    public void AddRepoRoot_Duplicate_NotAddedAgain()
    {
        var vm = NewVmWithPicker(pickFolder: _ => @"C:\Repos");
        vm.AddRepoRootCommand.Execute(null);
        vm.AddRepoRootCommand.Execute(null);

        vm.RepoRoots.Should().ContainSingle();
    }

    [Fact]
    public void AddRepoRoot_DuplicateIsCaseInsensitive()
    {
        var vm = NewVmWithPicker(pickFolder: _ => @"C:\Repos");
        vm.AddRepoRootCommand.Execute(null);

        // Different casing must NOT add a second copy — Windows paths
        // are case-insensitive in practice and we don't want the same
        // root scanned twice.
        var vm2 = NewVmWithPicker(pickFolder: _ => @"c:\repos");
        vm2.AddRepoRootCommand.Execute(null);

        vm2.RepoRoots.Should().ContainSingle();
    }

    [Fact]
    public void RemoveRepoRoot_RemovesAndPersists()
    {
        _service.Save(_service.Current with { RepoRoots = new[] { @"C:\A", @"C:\B" } });
        var vm = NewVmWithPicker();

        vm.RemoveRepoRootCommand.Execute(@"C:\A");

        vm.RepoRoots.Should().Equal(@"C:\B");
        _service.Current.RepoRoots.Should().Equal(@"C:\B");
    }

    [Fact]
    public void MoveRepoRoot_ReordersAndPersists()
    {
        _service.Save(_service.Current with { RepoRoots = new[] { @"C:\A", @"C:\B", @"C:\C" } });
        var vm = NewVmWithPicker();

        vm.MoveRepoRootDownCommand.Execute(@"C:\A");
        vm.RepoRoots.Should().Equal(@"C:\B", @"C:\A", @"C:\C");
        _service.Current.RepoRoots.Should().Equal(@"C:\B", @"C:\A", @"C:\C");

        vm.MoveRepoRootUpCommand.Execute(@"C:\C");
        vm.RepoRoots.Should().Equal(@"C:\B", @"C:\C", @"C:\A");
    }

    [Fact]
    public void MoveRepoRoot_OnBoundary_IsNoop()
    {
        _service.Save(_service.Current with { RepoRoots = new[] { @"C:\A", @"C:\B" } });
        var vm = NewVmWithPicker();

        vm.MoveRepoRootUpCommand.Execute(@"C:\A");
        vm.RepoRoots.Should().Equal(@"C:\A", @"C:\B");

        vm.MoveRepoRootDownCommand.Execute(@"C:\B");
        vm.RepoRoots.Should().Equal(@"C:\A", @"C:\B");
    }

    [Fact]
    public void BrowseDefaultCloneDestination_PicksAndPersists()
    {
        var vm = NewVmWithPicker(pickFolder: _ => @"D:\Clones");

        vm.BrowseDefaultCloneDestinationCommand.Execute(null);

        vm.DefaultCloneDestination.Should().Be(@"D:\Clones");
        vm.HasDefaultCloneDestination.Should().BeTrue();
        _service.Current.DefaultCloneDestination.Should().Be(@"D:\Clones");
    }

    [Fact]
    public void BrowseDefaultCloneDestination_PassesCurrentAsInitial()
    {
        _service.Save(_service.Current with { DefaultCloneDestination = @"D:\Existing" });
        string? observedInitial = null;
        var vm = NewVmWithPicker(pickFolder: initial =>
        {
            observedInitial = initial;
            return @"D:\NewPick";
        });

        vm.BrowseDefaultCloneDestinationCommand.Execute(null);

        observedInitial.Should().Be(@"D:\Existing");
    }

    [Fact]
    public void ClearDefaultCloneDestination_ResetsToNull()
    {
        _service.Save(_service.Current with { DefaultCloneDestination = @"D:\Clones" });
        var vm = NewVmWithPicker();

        vm.ClearDefaultCloneDestinationCommand.Execute(null);

        vm.DefaultCloneDestination.Should().BeEmpty();
        vm.HasDefaultCloneDestination.Should().BeFalse();
        _service.Current.DefaultCloneDestination.Should().BeNull();
    }

    [Fact]
    public void RepoUrlMappings_LoadedAndDisplayedSorted()
    {
        var mappings = new Dictionary<RepoUrlKey, string>
        {
            [RepoUrlKey.From("github.com", "z-org", "repo")] = "z",
            [RepoUrlKey.From("github.com", "a-org", "repo")] = "a",
        };
        _service.Save(_service.Current with { RepoUrlMappings = mappings });

        var vm = NewVmWithPicker();

        vm.RepoUrlMappings.Should().HaveCount(2);
        vm.RepoUrlMappings[0].Key.Owner.Should().Be("a-org");
        vm.RepoUrlMappings[1].Key.Owner.Should().Be("z-org");
    }

    [Fact]
    public void ForgetRepoUrlMapping_RemovesAndPersists()
    {
        var key = RepoUrlKey.From("github.com", "owner", "repo");
        _service.Save(_service.Current with
        {
            RepoUrlMappings = new Dictionary<RepoUrlKey, string> { [key] = @"C:\path" },
        });
        var vm = NewVmWithPicker();
        var row = vm.RepoUrlMappings[0];

        vm.ForgetRepoUrlMappingCommand.Execute(row);

        vm.RepoUrlMappings.Should().BeEmpty();
        _service.Current.RepoUrlMappings.Should().BeEmpty();
    }

    [Fact]
    public void RecordRepoUrlMapping_AddsRowAndPersists()
    {
        var vm = NewVmWithPicker();
        var key = RepoUrlKey.From("github.com", "owner", "repo");

        vm.RecordRepoUrlMapping(key, @"C:\Repos\repo");

        vm.RepoUrlMappings.Should().ContainSingle()
            .Which.Key.Should().Be(key);
        _service.Current.RepoUrlMappings.Should().ContainKey(key)
            .WhoseValue.Should().Be(@"C:\Repos\repo");
    }

    [Fact]
    public void RecordRepoUrlMapping_ExistingKey_IsReplaced()
    {
        var key = RepoUrlKey.From("github.com", "owner", "repo");
        _service.Save(_service.Current with
        {
            RepoUrlMappings = new Dictionary<RepoUrlKey, string> { [key] = @"C:\old" },
        });
        var vm = NewVmWithPicker();

        vm.RecordRepoUrlMapping(key, @"C:\new");

        vm.RepoUrlMappings.Should().ContainSingle();
        vm.RepoUrlMappings[0].Path.Should().Be(@"C:\new");
    }

    [Fact]
    public void RecordRepoUrlMapping_PromptsToRememberDefault_WhenNoneSetAndUserAgrees()
    {
        string? observedPrompt = null;
        var vm = NewVmWithPicker(confirmDefault: parent =>
        {
            observedPrompt = parent;
            return true;
        });

        vm.RecordRepoUrlMapping(
            RepoUrlKey.From("github.com", "owner", "repo"),
            clonePath: @"D:\Clones\repo",
            proposeDefaultClonePath: @"D:\Clones");

        observedPrompt.Should().Be(@"D:\Clones");
        vm.DefaultCloneDestination.Should().Be(@"D:\Clones");
        _service.Current.DefaultCloneDestination.Should().Be(@"D:\Clones");
    }

    [Fact]
    public void RecordRepoUrlMapping_UserDeclinesDefaultPrompt_KeepsCurrent()
    {
        var vm = NewVmWithPicker(confirmDefault: _ => false);

        vm.RecordRepoUrlMapping(
            RepoUrlKey.From("github.com", "owner", "repo"),
            clonePath: @"D:\Clones\repo",
            proposeDefaultClonePath: @"D:\Clones");

        vm.DefaultCloneDestination.Should().BeEmpty();
        _service.Current.DefaultCloneDestination.Should().BeNull();
    }

    [Fact]
    public void RecordRepoUrlMapping_DoesNotPromptWhenDefaultAlreadySet()
    {
        _service.Save(_service.Current with { DefaultCloneDestination = @"D:\Existing" });
        var promptCount = 0;
        var vm = NewVmWithPicker(confirmDefault: _ => { promptCount++; return true; });

        vm.RecordRepoUrlMapping(
            RepoUrlKey.From("github.com", "owner", "repo"),
            clonePath: @"D:\Clones\repo",
            proposeDefaultClonePath: @"D:\Clones");

        promptCount.Should().Be(0);
        _service.Current.DefaultCloneDestination.Should().Be(@"D:\Existing");
    }
}
