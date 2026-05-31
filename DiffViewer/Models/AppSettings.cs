namespace DiffViewer.Models;

/// <summary>
/// All persisted user settings. Loaded once at startup, mutated through
/// <see cref="DiffViewer.Services.ISettingsService"/>, and saved
/// atomically to <c>%APPDATA%\DiffViewer\settings.json</c>.
///
/// <para>Every field has a sensible default so a missing JSON file or a
/// missing field never crashes the app.</para>
///
/// <para><b>Schema versioning:</b> the on-disk JSON carries
/// <see cref="SchemaVersion"/> so we can run migrations when the shape
/// changes. See <see cref="DiffViewer.Services.SettingsService"/> and
/// <see cref="DiffViewer.Services.SettingsMigrations"/>.</para>
/// </summary>
public sealed record AppSettings
{
    /// <summary>Current schema version; bump every time the shape changes.</summary>
    public const int CurrentSchemaVersion = 8;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    // ---- Main-window geometry (added in v2) ----
    /// <summary>
    /// Saved <see cref="MainWindow"/> size, position, and maximized
    /// state. <c>null</c> means "no saved state yet" — the window opens
    /// at the built-in defaults. See
    /// <see cref="DiffViewer.Utility.WindowGeometryValidator"/> for the
    /// multi-monitor sanity check applied before this is honored at
    /// launch.
    /// </summary>
    public WindowStateSnapshot? WindowState { get; init; }

    // ---- Pane split (added in v4) ----
    /// <summary>
    /// Width, in device-independent pixels, of the left pane that holds
    /// the recents bar and the file list. The diff pane fills the
    /// remainder. Clamped to <see cref="DiffViewer.Utility.FileListLayout"/>'s
    /// min/max at both load and save time so a tampered or
    /// cross-monitor settings file can never produce an unusable
    /// layout (e.g. a file list wider than the window).
    /// </summary>
    public double FileListPaneWidthPixels { get; init; } = 320.0;

    // ---- Toolbar toggles (persisted across launches per the plan) ----
    public bool IgnoreWhitespace { get; init; }
    public bool ShowIntraLineDiff { get; init; } = true;
    public bool IsSideBySide { get; init; } = true;
    public bool ShowVisibleWhitespace { get; init; }
    public bool LiveUpdates { get; init; } = true;
    public DiffSideVisibility SideVisibility { get; init; } = DiffSideVisibility.Both;

    /// <summary>
    /// SVG-only "Rendered" toolbar toggle (added in v6). When
    /// <c>true</c>, the diff pane shows the rasterised image-diff view
    /// of the SVG; when <c>false</c>, the XML text diff. Defaults to
    /// <c>true</c> because rasterised diffing is the headline feature
    /// of issue #15 — the user opted in by opening an SVG. Has no
    /// effect for non-SVG files; the toolbar hides the toggle in that
    /// case. Persisted across launches like the other toolbar toggles.
    /// </summary>
    public bool RenderSvgImage { get; init; } = true;

    // ---- File-list display mode ----
    public FileListDisplayMode DisplayMode { get; init; } = FileListDisplayMode.RepoRelative;

    // ---- Limits ----
    /// <summary>Files larger than this on either side are skipped (placeholder shown).</summary>
    public long LargeFileThresholdBytes { get; init; } = 25L * 1024 * 1024;

    // ---- Diff-pane appearance ----
    public string FontFamily { get; init; } = "Consolas";
    public double FontSize { get; init; } = 11.0;
    public int TabWidth { get; init; } = 4;
    public bool ShowLineNumbers { get; init; } = true;
    public bool WordWrap { get; init; }
    public ColorSchemeChoice ColorScheme { get; init; } = ColorSchemeChoice.Preset(ColorSchemePresetName.Classic);

    // ---- External editor (auto-detect when null/empty) ----
    public string? ExternalEditorPath { get; init; }
    public string? ExternalEditorLineArgFormat { get; init; }

    // ---- "Don't ask me again" flags for destructive ops ----
    public bool SuppressRevertHunkConfirmation { get; init; }
    public bool SuppressRevertFileConfirmation { get; init; }
    public bool SuppressDeleteFileConfirmation { get; init; }

    // ---- PR-review feature (added in v5) ----
    /// <summary>
    /// Directories DiffViewer scans for a local clone matching a
    /// PR URL's (host, owner, repo) triple. Each entry's immediate
    /// children are inspected; sub-sub-directories are not. Initially
    /// empty — the user populates this through the Settings dialog.
    /// </summary>
    public IReadOnlyList<string> RepoRoots { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional pre-fill for the destination picker in the
    /// missing-clone dialog's "clone for me" branch. Set the first time
    /// the user picks a destination and answers "yes" to "remember
    /// this". <c>null</c> means "ask every time".
    /// </summary>
    public string? DefaultCloneDestination { get; init; }

    /// <summary>
    /// Explicit (host, owner, repo) → local-clone-path overrides
    /// recorded when the user picks "browse to existing clone" in the
    /// missing-clone dialog (and the scanned <see cref="RepoRoots"/>
    /// didn't find it). Takes precedence over the root scan in
    /// <c>ILocalRepoLocator</c>.
    /// </summary>
    public IReadOnlyDictionary<RepoUrlKey, string> RepoUrlMappings { get; init; }
        = new Dictionary<RepoUrlKey, string>();

    // ---- Auto-update (added in v7) ----
    /// <summary>
    /// How DiffViewer handles available updates from the configured
    /// update source. Default <see cref="AutoUpdateMode.NotifyOnly"/>
    /// — silent auto-updates are opt-in. See
    /// <see cref="AutoUpdateMode"/> for the per-value semantics and
    /// the cross-phase rollout story.
    /// </summary>
    public AutoUpdateMode AutoUpdate { get; init; } = AutoUpdateMode.NotifyOnly;

    /// <summary>
    /// How often the update check fires. Default
    /// <see cref="UpdateCheckCadence.Daily"/>. Inert in Phase 2.2
    /// (only the startup check exists); Phase 2.3 wires a periodic
    /// timer that honors this value.
    /// </summary>
    public UpdateCheckCadence UpdateCheckCadence { get; init; } = UpdateCheckCadence.Daily;

    /// <summary>
    /// When <c>true</c>, the update source includes pre-release tags
    /// (e.g. <c>v1.5.0-rc1</c>). Default <c>false</c> — stable
    /// releases only. Wired through to
    /// <see cref="Velopack.Sources.GithubSource"/> at startup in Phase 2.3.
    /// </summary>
    public bool IncludePreReleases { get; init; }

    /// <summary>
    /// Last update version the user dismissed via the banner's
    /// "Skip this version" gesture. When the next update check
    /// returns this same version, the banner stays hidden. Cleared
    /// implicitly when a newer version is detected and skipped
    /// (the new skip overwrites the old). <c>null</c> means
    /// "nothing skipped" — the default for new installs and
    /// pre-v8 migrated installs.
    /// </summary>
    public string? SkippedUpdateVersion { get; init; }
}

/// <summary>
/// Discriminated union: either a named preset or a hand-rolled palette.
/// Persisted as <c>{ "type": "preset", "name": "Classic" }</c> or
/// <c>{ "type": "custom", "colors": { "addedLineBg": "#...", ... } }</c>
/// so the dialog never silently overwrites a hand-edited palette on
/// live-save.
/// </summary>
public abstract record ColorSchemeChoice
{
    public static ColorSchemeChoice Preset(ColorSchemePresetName name) => new PresetScheme(name);
    public static ColorSchemeChoice Custom(ColorSchemeColors colors) => new CustomScheme(colors);

    public sealed record PresetScheme(ColorSchemePresetName Name) : ColorSchemeChoice;
    public sealed record CustomScheme(ColorSchemeColors Colors) : ColorSchemeChoice;
}

/// <summary>The seven presets named in the plan's Diff appearance section.</summary>
public enum ColorSchemePresetName
{
    Classic,
    GitHub,
    HighContrast,
    ColorblindFriendly,
    SolarizedLight,
    Pale,
    Monochrome,
}

/// <summary>Five colors that define a diff palette - hex strings.</summary>
public sealed record ColorSchemeColors(
    string AddedLineBg,
    string RemovedLineBg,
    string ModifiedLineBg,
    string AddedIntraline,
    string RemovedIntraline);
