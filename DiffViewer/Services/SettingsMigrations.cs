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
}
