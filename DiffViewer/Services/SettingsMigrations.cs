using System.Text.Json.Nodes;

namespace DiffViewer.Services;

/// <summary>
/// Versioned migrations for <c>settings.json</c>. Each migration takes a
/// <see cref="JsonObject"/> shaped for version N and returns one shaped
/// for N+1. <see cref="MigrateUpTo"/> chains them.
///
/// <para>v2 introduced the optional <c>windowState</c> object for
/// remembered main-window geometry. The migration is a no-op because
/// the field is nullable — pre-v2 files simply load without a saved
/// window state and the window opens at the built-in defaults.</para>
///
/// <para>v3 introduced <c>suppressRevertFileConfirmation</c> for the
/// whole-file Revert action. The migration is a no-op because the
/// field is a bool with a <c>false</c> default — pre-v3 files
/// deserialize with the prompt enabled (the safe default).</para>
///
/// <para>v4 introduced <c>fileListPaneWidthPixels</c> for the
/// persisted file-list / diff-pane splitter position. The migration
/// is a no-op because the field has a sensible default (320 px,
/// matching the historical hardcoded XAML value) — pre-v4 files
/// deserialize to that default and the window opens with the same
/// split users had before persistence existed.</para>
///
/// <para>v5 introduced the three PR-review settings:
/// <c>repoRoots</c>, <c>defaultCloneDestination</c>, and
/// <c>repoUrlMappings</c>. The migration is a no-op because every
/// field has a safe default — pre-v5 files deserialize to an empty
/// repo-roots list, a <c>null</c> default clone destination, and an
/// empty mappings dictionary, which exactly reflects the "no PR-review
/// configuration yet" state.</para>
///
/// <para>v6 introduced <c>renderSvgImage</c> for the SVG-only
/// "Rendered" toolbar toggle (issue #15). The migration is a no-op
/// because the field has a sensible default (<c>true</c>) — pre-v6
/// files deserialize with the rasterised view enabled, matching the
/// headline behaviour the feature was designed to deliver.</para>
///
/// <para>v7 introduced the three auto-update settings:
/// <c>autoUpdate</c> (<see cref="AutoUpdateMode"/>),
/// <c>updateCheckCadence</c> (<see cref="UpdateCheckCadence"/>), and
/// <c>includePreReleases</c>. The migration is a no-op because every
/// field has a safe default — pre-v7 files deserialize to
/// <see cref="AutoUpdateMode.NotifyOnly"/> / <see cref="UpdateCheckCadence.Daily"/> /
/// <c>false</c>, which exactly reflects the "no auto-update opted into yet"
/// posture Phase 2.2 ships with.</para>
///
/// <para>v8 introduced <c>skippedUpdateVersion</c> for the auto-update
/// banner's "Skip this version" gesture (Phase 2.4). The migration
/// is a no-op because the field is nullable with a <c>null</c>
/// default — pre-v8 files deserialize to "nothing skipped", which
/// means the banner shows for every available update (the safe
/// default).</para>
///
/// <para>v9 introduced <c>preferMarkdownRendered</c> for the
/// markdown-only "Rendered" toolbar toggle (the markdown rendered-diff
/// feature). The migration is a no-op because the field has a
/// sensible default (<c>true</c>) — pre-v9 files deserialize with the
/// rendered view enabled by default, matching the headline behaviour
/// of the feature (same shape as the v6 <c>renderSvgImage</c>
/// migration).</para>
///
/// <para>v10 introduced the two PR auto-refresh settings:
/// <c>pullRequestAutoRefresh</c> (bool, default <c>true</c>) and
/// <c>pullRequestPollIntervalSeconds</c> (int, default 300; clamped
/// 30..3600 on load). The migration is a no-op because both fields
/// have safe defaults that match the headline behaviour of the
/// feature — pre-v10 files deserialize with auto-refresh enabled at
/// the 5-minute default cadence, which exactly matches what a fresh
/// install gets.</para>
/// </summary>
internal static class SettingsMigrations
{
    /// <summary>
    /// Run every migration in order from <paramref name="fromVersion"/>
    /// up to <paramref name="toVersion"/>. The returned object always
    /// carries a <c>schemaVersion</c> equal to <paramref name="toVersion"/>.
    /// </summary>
    public static JsonObject MigrateUpTo(JsonObject obj, int fromVersion, int toVersion)
    {
        var current = obj;
        for (int v = fromVersion; v < toVersion; v++)
        {
            Func<JsonObject, JsonObject> step = v switch
            {
                0 => MigrateV0ToV1, // pre-versioned files - treat as v1's shape
                1 => MigrateV1ToV2, // adds windowState (nullable)
                2 => MigrateV2ToV3, // adds suppressRevertFileConfirmation (bool, default false)
                3 => MigrateV3ToV4, // adds fileListPaneWidthPixels (double, default 320)
                4 => MigrateV4ToV5, // adds repoRoots, defaultCloneDestination, repoUrlMappings (all defaultable)
                5 => MigrateV5ToV6, // adds renderSvgImage (bool, default true)
                6 => MigrateV6ToV7, // adds autoUpdate / updateCheckCadence / includePreReleases (all defaultable)
                7 => MigrateV7ToV8, // adds skippedUpdateVersion (nullable string, default null)
                8 => MigrateV8ToV9, // adds preferMarkdownRendered (bool, default true)
                9 => MigrateV9ToV10, // adds pullRequestAutoRefresh + pullRequestPollIntervalSeconds (both defaultable)
                _ => throw new InvalidOperationException($"No migration registered from version {v} to {v + 1}."),
            };
            current = step(current);
            current["schemaVersion"] = v + 1;
        }
        return current;
    }

    /// <summary>
    /// Pre-versioned (v0) files have the same shape as v1; we just stamp
    /// the version and let the deserializer fill in defaults for any
    /// missing field.
    /// </summary>
    private static JsonObject MigrateV0ToV1(JsonObject obj) => obj;

    /// <summary>
    /// v2 adds the optional <c>windowState</c> object. A missing field
    /// means "no saved state" so the migration is a no-op; the
    /// deserializer will default <c>WindowState</c> to <c>null</c> and
    /// the window will open at the built-in defaults.
    /// </summary>
    private static JsonObject MigrateV1ToV2(JsonObject obj) => obj;

    /// <summary>
    /// v3 adds <c>suppressRevertFileConfirmation</c>. A missing field
    /// means "prompt on Revert file…" (the safe default) so the
    /// migration is a no-op; the deserializer fills in
    /// <see cref="AppSettings.SuppressRevertFileConfirmation"/> as
    /// <c>false</c>.
    /// </summary>
    private static JsonObject MigrateV2ToV3(JsonObject obj) => obj;

    /// <summary>
    /// v4 adds <c>fileListPaneWidthPixels</c>. A missing field means
    /// "open with the historical default 320-px split" so the migration
    /// is a no-op; the deserializer fills in
    /// <see cref="AppSettings.FileListPaneWidthPixels"/> from
    /// <see cref="AppSettings"/>' built-in default.
    /// </summary>
    private static JsonObject MigrateV3ToV4(JsonObject obj) => obj;

    /// <summary>
    /// v5 adds the three PR-review settings (<c>repoRoots</c>,
    /// <c>defaultCloneDestination</c>, <c>repoUrlMappings</c>). All have
    /// safe defaults that exactly reflect "no PR-review configuration",
    /// so the migration is a no-op; the deserializer fills in an empty
    /// list, a <c>null</c> destination, and an empty mappings dictionary.
    /// </summary>
    private static JsonObject MigrateV4ToV5(JsonObject obj) => obj;

    /// <summary>
    /// v6 adds <c>renderSvgImage</c> (SVG-only "Rendered" toolbar
    /// toggle). A missing field means "rasterised view" (the headline
    /// behaviour of issue #15) so the migration is a no-op; the
    /// deserializer fills in
    /// <see cref="AppSettings.RenderSvgImage"/> as <c>true</c>.
    /// </summary>
    private static JsonObject MigrateV5ToV6(JsonObject obj) => obj;

    /// <summary>
    /// v7 adds the three auto-update settings (<c>autoUpdate</c>,
    /// <c>updateCheckCadence</c>, <c>includePreReleases</c>). All have
    /// safe defaults that map to "no auto-update opted into yet" so
    /// the migration is a no-op; the deserializer fills in
    /// <see cref="AutoUpdateMode.NotifyOnly"/> /
    /// <see cref="UpdateCheckCadence.Daily"/> / <c>false</c>.
    /// </summary>
    private static JsonObject MigrateV6ToV7(JsonObject obj) => obj;

    /// <summary>
    /// v8 adds <c>skippedUpdateVersion</c> for the auto-update
    /// banner's "Skip this version" gesture. The migration is a
    /// no-op because the field is nullable with a <c>null</c>
    /// default; pre-v8 files load with no skipped version, meaning
    /// the banner shows for every available update.
    /// </summary>
    private static JsonObject MigrateV7ToV8(JsonObject obj) => obj;

    /// <summary>
    /// v9 adds <c>preferMarkdownRendered</c> for the markdown-only
    /// "Rendered" toolbar toggle. A missing field means "rendered
    /// view" (the headline behaviour of the markdown rendered-diff
    /// feature) so the migration is a no-op; the deserializer fills
    /// in <see cref="AppSettings.PreferMarkdownRendered"/> as
    /// <c>true</c>.
    /// </summary>
    private static JsonObject MigrateV8ToV9(JsonObject obj) => obj;

    /// <summary>
    /// v10 adds the two PR auto-refresh settings
    /// (<c>pullRequestAutoRefresh</c>,
    /// <c>pullRequestPollIntervalSeconds</c>). Both have safe defaults
    /// that map to "auto-refresh enabled at 5-minute cadence" so the
    /// migration is a no-op; the deserializer fills in
    /// <see cref="AppSettings.PullRequestAutoRefresh"/> as <c>true</c>
    /// and
    /// <see cref="AppSettings.PullRequestPollIntervalSeconds"/> as
    /// <see cref="AppSettings.PullRequestPollIntervalSecondsDefault"/>.
    /// </summary>
    private static JsonObject MigrateV9ToV10(JsonObject obj) => obj;
}
