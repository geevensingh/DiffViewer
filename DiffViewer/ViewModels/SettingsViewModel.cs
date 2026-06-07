using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.Utility;

namespace DiffViewer.ViewModels;

/// <summary>
/// Backs <c>SettingsDialog.xaml</c>. Reads / writes through
/// <see cref="ISettingsService"/> with the live-save commit policy
/// described in the plan:
/// <list type="bullet">
///   <item>Toggles (checkboxes, dropdowns) → save immediately.</item>
///   <item>Numeric inputs → save on focus-loss / Enter (the View
///     is responsible for triggering <see cref="CommitNumericFields"/>
///     at those moments; the VM never auto-commits per keystroke).</item>
///   <item>Text inputs (editor path / line-arg format) → save on
///     focus-loss / Enter.</item>
///   <item>Color-scheme dropdown → 200 ms trailing-edge debounce.</item>
/// </list>
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    public const int ColorSchemeDebounceMs = 200;

    private readonly ISettingsService _settings;
    private readonly Action<string>? _openInEditor;
    private readonly Func<string, bool>? _confirmReset;
    private readonly Func<string?, string?>? _pickFolder;
    private readonly Func<string, bool>? _confirmRememberDefaultClone;
    private readonly DispatcherTimer? _colorSchemeDebounce;
    private bool _suppress;

    public SettingsViewModel(
        ISettingsService settings,
        Action<string>? openInEditor = null,
        Func<string, bool>? confirmReset = null,
        bool useDispatcherTimer = true,
        IReadOnlyList<FontFamilyOption>? availableFonts = null,
        Func<string?, string?>? pickFolder = null,
        Func<string, bool>? confirmRememberDefaultClone = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _openInEditor = openInEditor;
        _confirmReset = confirmReset;
        _pickFolder = pickFolder;
        _confirmRememberDefaultClone = confirmRememberDefaultClone;

        ColorSchemePresets = new ObservableCollection<ColorSchemePresetName>(
            Enum.GetValues<ColorSchemePresetName>());

        AvailableFonts = availableFonts ?? Array.Empty<FontFamilyOption>();

        RepoRoots = new ObservableCollection<string>();
        RepoUrlMappings = new ObservableCollection<RepoUrlMappingRow>();

        if (useDispatcherTimer && System.Windows.Application.Current is not null)
        {
            _colorSchemeDebounce = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ColorSchemeDebounceMs),
            };
            _colorSchemeDebounce.Tick += (_, _) =>
            {
                _colorSchemeDebounce!.Stop();
                CommitColorScheme();
            };
        }

        LoadFromSettings();
    }

    // ---------- Bindable state ----------

    public ObservableCollection<ColorSchemePresetName> ColorSchemePresets { get; }

    /// <summary>
    /// All installed system fonts available in the font-family dropdown.
    /// Grouped in the View by <see cref="FontFamilyOption.GroupName"/>
    /// (Monospaced / Variable width).
    /// </summary>
    public IReadOnlyList<FontFamilyOption> AvailableFonts { get; }

    // Diff appearance
    [ObservableProperty] private string _fontFamily = "Consolas";
    [ObservableProperty] private double _fontSize = 11.0;
    [ObservableProperty] private int _tabWidth = 4;
    [ObservableProperty] private bool _showLineNumbers = true;
    [ObservableProperty] private bool _wordWrap;
    [ObservableProperty] private ColorSchemePresetName _selectedColorPreset = ColorSchemePresetName.Classic;

    /// <summary>True iff the persisted color-scheme is a hand-edited custom palette.</summary>
    [ObservableProperty] private bool _isCustomColorScheme;

    // External editor
    [ObservableProperty] private string _externalEditorPath = string.Empty;
    [ObservableProperty] private string _externalEditorLineArgFormat = string.Empty;

    // Limits
    [ObservableProperty] private int _largeFileThresholdMb = 25;

    // Confirmations (note: bound as positives even though stored as suppress-flags)
    [ObservableProperty] private bool _confirmRevertHunk = true;
    [ObservableProperty] private bool _confirmDeleteFile = true;

    // PR-review settings
    public ObservableCollection<string> RepoRoots { get; }
    public ObservableCollection<RepoUrlMappingRow> RepoUrlMappings { get; }
    [ObservableProperty] private string _defaultCloneDestination = string.Empty;
    [ObservableProperty] private string? _selectedRepoRoot;
    public bool HasDefaultCloneDestination => !string.IsNullOrWhiteSpace(DefaultCloneDestination);
    partial void OnDefaultCloneDestinationChanged(string value) =>
        OnPropertyChanged(nameof(HasDefaultCloneDestination));

    // Auto-update settings (v7)
    [ObservableProperty] private AutoUpdateMode _autoUpdate = AutoUpdateMode.NotifyOnly;
    [ObservableProperty] private UpdateCheckCadence _updateCheckCadence = UpdateCheckCadence.Daily;
    [ObservableProperty] private bool _includePreReleases;

    // Pull-request auto-refresh settings (v10)
    [ObservableProperty] private bool _pullRequestAutoRefresh = true;
    [ObservableProperty] private int _pullRequestPollIntervalSeconds
        = AppSettings.PullRequestPollIntervalSecondsDefault;

    /// <summary>Option list for the AutoUpdate dropdown in the dialog.</summary>
    public IReadOnlyList<AutoUpdateMode> AutoUpdateOptions { get; } = Enum.GetValues<AutoUpdateMode>();

    /// <summary>Option list for the UpdateCheckCadence dropdown in the dialog.</summary>
    public IReadOnlyList<UpdateCheckCadence> UpdateCheckCadenceOptions { get; } = Enum.GetValues<UpdateCheckCadence>();

    /// <summary>
    /// Display version of the running DiffViewer build (e.g.
    /// <c>"1.6.0"</c>). Read once at construction; does not change
    /// at runtime. Shown in the dialog's Updates section as
    /// "what version am I on right now" context for the auto-update
    /// settings.
    /// </summary>
    public string CurrentVersion { get; } = AppVersionInfo.GetDisplayVersion();

    // Status line
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ---------- Commands ----------

    [RelayCommand]
    private void OpenSettingsJson()
    {
        var path = SettingsService.DefaultFilePath;
        try
        {
            if (_openInEditor is not null)
            {
                _openInEditor(path);
                StatusMessage = $"Opened {path}";
                return;
            }

            // OS-default shell-open fallback (per the plan).
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
            StatusMessage = $"Opened {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't open {path}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetAllToDefaults()
    {
        var path = SettingsService.DefaultFilePath;
        if (_confirmReset is not null && !_confirmReset(
            "Reset all DiffViewer settings to defaults? This cannot be undone."))
        {
            return;
        }

        _settings.Save(new AppSettings());
        LoadFromSettings();
        StatusMessage = "Settings reset to defaults.";
    }

    /// <summary>
    /// Called by the View when a numeric or text input loses focus or the
    /// user presses Enter. The VM holds the buffered value in its
    /// observable property; this method commits it to the settings file.
    /// </summary>
    public void CommitNumericFields()
    {
        if (_suppress) return;
        var clampedFontSize = Math.Clamp(FontSize, 6.0, 72.0);
        var clampedTabWidth = Math.Clamp(TabWidth, 1, 16);
        var clampedThresholdMb = Math.Clamp(LargeFileThresholdMb, 1, 2048);
        var clampedPollInterval = Math.Clamp(
            PullRequestPollIntervalSeconds,
            AppSettings.PullRequestPollIntervalSecondsMin,
            AppSettings.PullRequestPollIntervalSecondsMax);

        if (clampedFontSize != FontSize) FontSize = clampedFontSize;
        if (clampedTabWidth != TabWidth) TabWidth = clampedTabWidth;
        if (clampedThresholdMb != LargeFileThresholdMb) LargeFileThresholdMb = clampedThresholdMb;
        if (clampedPollInterval != PullRequestPollIntervalSeconds)
            PullRequestPollIntervalSeconds = clampedPollInterval;

        _settings.Update(s => s with
        {
            FontSize = clampedFontSize,
            TabWidth = clampedTabWidth,
            LargeFileThresholdBytes = (long)clampedThresholdMb * 1024 * 1024,
            FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "Consolas" : FontFamily,
            ExternalEditorPath = string.IsNullOrWhiteSpace(ExternalEditorPath) ? null : ExternalEditorPath,
            ExternalEditorLineArgFormat = string.IsNullOrWhiteSpace(ExternalEditorLineArgFormat)
                ? null
                : ExternalEditorLineArgFormat,
            PullRequestPollIntervalSeconds = clampedPollInterval,
        });
        StatusMessage = "Saved.";
    }

    /// <summary>
    /// Synchronously fires any pending color-scheme debounce. The View
    /// calls this from the Close button's handler so the latest preset
    /// click is applied without waiting for the 200 ms window.
    /// </summary>
    public void FlushPendingWrites()
    {
        if (_colorSchemeDebounce is { IsEnabled: true })
        {
            _colorSchemeDebounce.Stop();
            CommitColorScheme();
        }
    }

    // ---------- Toggle persistence (live save) ----------

    partial void OnShowLineNumbersChanged(bool value) =>
        SaveIfNotSuppressed(s => s with { ShowLineNumbers = value });

    partial void OnWordWrapChanged(bool value) =>
        SaveIfNotSuppressed(s => s with { WordWrap = value });

    partial void OnConfirmRevertHunkChanged(bool value) =>
        SaveIfNotSuppressed(s => s with { SuppressRevertHunkConfirmation = !value });

    partial void OnConfirmDeleteFileChanged(bool value) =>
        SaveIfNotSuppressed(s => s with { SuppressDeleteFileConfirmation = !value });

    partial void OnAutoUpdateChanged(AutoUpdateMode value) =>
        SaveIfNotSuppressed(s => s with { AutoUpdate = value });

    partial void OnUpdateCheckCadenceChanged(UpdateCheckCadence value) =>
        SaveIfNotSuppressed(s => s with { UpdateCheckCadence = value });

    partial void OnIncludePreReleasesChanged(bool value) =>
        SaveIfNotSuppressed(s => s with { IncludePreReleases = value });

    partial void OnPullRequestAutoRefreshChanged(bool value) =>
        SaveIfNotSuppressed(s => s with { PullRequestAutoRefresh = value });

    // PullRequestPollIntervalSeconds is committed via CommitNumericFields
    // (on focus loss / Enter), mirroring the LargeFileThresholdMb pattern,
    // so per-keystroke writes don't churn settings.json.

    partial void OnSelectedColorPresetChanged(ColorSchemePresetName value)
    {
        if (_suppress) return;

        // User explicitly picked a preset → opt out of any custom palette.
        IsCustomColorScheme = false;

        if (_colorSchemeDebounce is null)
        {
            // Test path or no Application.Current — commit synchronously.
            CommitColorScheme();
            return;
        }

        _colorSchemeDebounce.Stop();
        _colorSchemeDebounce.Start();
    }

    private void CommitColorScheme()
    {
        if (_suppress) return;
        var preset = SelectedColorPreset;
        _settings.Update(s => s with { ColorScheme = ColorSchemeChoice.Preset(preset) });
        StatusMessage = $"Color scheme: {preset}.";
    }

    private void SaveIfNotSuppressed(Func<AppSettings, AppSettings> mutate)
    {
        if (_suppress) return;
        _settings.Update(mutate);
        StatusMessage = "Saved.";
    }

    // ---------- PR-review commands ----------

    [RelayCommand]
    private void AddRepoRoot()
    {
        if (_pickFolder is null)
        {
            StatusMessage = "No folder picker available.";
            return;
        }

        var picked = _pickFolder(null);
        if (string.IsNullOrWhiteSpace(picked)) return;

        if (RepoRoots.Any(r => string.Equals(r, picked, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"Already a repo root: {picked}";
            return;
        }

        RepoRoots.Add(picked);
        PersistRepoRoots();
    }

    [RelayCommand]
    private void RemoveRepoRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        if (!RepoRoots.Remove(root)) return;
        PersistRepoRoots();
    }

    [RelayCommand]
    private void MoveRepoRootUp(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        var index = RepoRoots.IndexOf(root);
        if (index <= 0) return;
        RepoRoots.Move(index, index - 1);
        PersistRepoRoots();
    }

    [RelayCommand]
    private void MoveRepoRootDown(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        var index = RepoRoots.IndexOf(root);
        if (index < 0 || index >= RepoRoots.Count - 1) return;
        RepoRoots.Move(index, index + 1);
        PersistRepoRoots();
    }

    private void PersistRepoRoots()
    {
        if (_suppress) return;
        var snapshot = RepoRoots.ToList();
        _settings.Update(s => s with { RepoRoots = snapshot });
        StatusMessage = "Saved.";
    }

    [RelayCommand]
    private void BrowseDefaultCloneDestination()
    {
        if (_pickFolder is null)
        {
            StatusMessage = "No folder picker available.";
            return;
        }

        var initial = string.IsNullOrWhiteSpace(DefaultCloneDestination) ? null : DefaultCloneDestination;
        var picked = _pickFolder(initial);
        if (string.IsNullOrWhiteSpace(picked)) return;

        DefaultCloneDestination = picked;
        SaveIfNotSuppressed(s => s with { DefaultCloneDestination = picked });
    }

    [RelayCommand]
    private void ClearDefaultCloneDestination()
    {
        DefaultCloneDestination = string.Empty;
        SaveIfNotSuppressed(s => s with { DefaultCloneDestination = null });
    }

    [RelayCommand]
    private void ForgetRepoUrlMapping(RepoUrlMappingRow? row)
    {
        if (row is null) return;
        if (!RepoUrlMappings.Remove(row)) return;
        if (_suppress) return;
        var snapshot = RepoUrlMappings.ToDictionary(m => m.Key, m => m.Path);
        _settings.Update(s => s with { RepoUrlMappings = snapshot });
        StatusMessage = $"Forgot mapping for {row.DisplayKey}.";
    }

    /// <summary>
    /// Public entry point used by the missing-clone dialog (Phase 5+).
    /// Records or updates an explicit (host, owner, repo) → path mapping
    /// and, if the user agrees, records the picked clone's parent as the
    /// new <see cref="AppSettings.DefaultCloneDestination"/>. The
    /// <paramref name="proposeDefaultClonePath"/> argument lets the
    /// caller pre-compute the parent directory (e.g. for the "clone for
    /// me" path); pass <c>null</c> to skip the prompt.
    /// </summary>
    public void RecordRepoUrlMapping(
        RepoUrlKey key,
        string clonePath,
        string? proposeDefaultClonePath = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(clonePath);

        var existing = RepoUrlMappings.FirstOrDefault(m => m.Key == key);
        if (existing is not null) RepoUrlMappings.Remove(existing);
        RepoUrlMappings.Add(new RepoUrlMappingRow(key, clonePath));

        var snapshot = RepoUrlMappings.ToDictionary(m => m.Key, m => m.Path);

        bool acceptDefault = false;
        if (!string.IsNullOrWhiteSpace(proposeDefaultClonePath)
            && string.IsNullOrWhiteSpace(DefaultCloneDestination)
            && _confirmRememberDefaultClone is not null)
        {
            acceptDefault = _confirmRememberDefaultClone(proposeDefaultClonePath);
        }

        _settings.Update(s => s with
        {
            RepoUrlMappings = snapshot,
            DefaultCloneDestination = acceptDefault ? proposeDefaultClonePath : s.DefaultCloneDestination,
        });

        if (acceptDefault) DefaultCloneDestination = proposeDefaultClonePath!;
        StatusMessage = $"Remembered clone for {key.Owner}/{key.Repo}.";
    }

    private void LoadFromSettings()
    {
        _suppress = true;
        try
        {
            var s = _settings.Current;
            FontFamily = s.FontFamily;
            FontSize = s.FontSize;
            TabWidth = s.TabWidth;
            ShowLineNumbers = s.ShowLineNumbers;
            WordWrap = s.WordWrap;
            ExternalEditorPath = s.ExternalEditorPath ?? string.Empty;
            ExternalEditorLineArgFormat = s.ExternalEditorLineArgFormat ?? string.Empty;
            LargeFileThresholdMb = (int)Math.Clamp(s.LargeFileThresholdBytes / (1024 * 1024), 1, 2048);
            ConfirmRevertHunk = !s.SuppressRevertHunkConfirmation;
            ConfirmDeleteFile = !s.SuppressDeleteFileConfirmation;

            AutoUpdate = s.AutoUpdate;
            UpdateCheckCadence = s.UpdateCheckCadence;
            IncludePreReleases = s.IncludePreReleases;

            PullRequestAutoRefresh = s.PullRequestAutoRefresh;
            PullRequestPollIntervalSeconds = s.PullRequestPollIntervalSeconds;

            RepoRoots.Clear();
            foreach (var root in s.RepoRoots) RepoRoots.Add(root);

            DefaultCloneDestination = s.DefaultCloneDestination ?? string.Empty;

            RepoUrlMappings.Clear();
            foreach (var kvp in s.RepoUrlMappings
                .OrderBy(kvp => kvp.Key.ToWireString(), StringComparer.Ordinal))
            {
                RepoUrlMappings.Add(new RepoUrlMappingRow(kvp.Key, kvp.Value));
            }

            switch (s.ColorScheme)
            {
                case ColorSchemeChoice.PresetScheme p:
                    SelectedColorPreset = p.Name;
                    IsCustomColorScheme = false;
                    break;
                case ColorSchemeChoice.CustomScheme:
                    IsCustomColorScheme = true;
                    // Leave SelectedColorPreset at whatever it was (the dialog
                    // shows "Custom (edit JSON)" instead of the dropdown value).
                    break;
            }
        }
        finally { _suppress = false; }
    }

    public void Dispose()
    {
        _colorSchemeDebounce?.Stop();
    }
}
