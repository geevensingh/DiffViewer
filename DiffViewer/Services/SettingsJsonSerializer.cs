using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Hand-rolled JSON serializer for <see cref="AppSettings"/>. We avoid
/// <see cref="JsonSerializer.Serialize{TValue}(TValue, JsonSerializerOptions?)"/>
/// for the whole record because:
/// <list type="bullet">
///   <item>The discriminated <see cref="ColorSchemeChoice"/> needs a
///         tagged shape (<c>"type": "preset" | "custom"</c>) which
///         <c>JsonSerializer</c> won't produce by default.</item>
///   <item>We want the on-disk JSON to be human-friendly (camelCase keys,
///         indented) so a power user can hand-edit it.</item>
/// </list>
/// </summary>
internal static class SettingsJsonSerializer
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(AppSettings s)
    {
        var obj = new JsonObject
        {
            ["schemaVersion"] = AppSettings.CurrentSchemaVersion,
            ["ignoreWhitespace"] = s.IgnoreWhitespace,
            ["showIntraLineDiff"] = s.ShowIntraLineDiff,
            ["isSideBySide"] = s.IsSideBySide,
            ["showVisibleWhitespace"] = s.ShowVisibleWhitespace,
            ["liveUpdates"] = s.LiveUpdates,
            ["sideVisibility"] = s.SideVisibility.ToString(),
            ["displayMode"] = s.DisplayMode.ToString(),
            ["largeFileThresholdBytes"] = s.LargeFileThresholdBytes,
            ["fontFamily"] = s.FontFamily,
            ["fontSize"] = s.FontSize,
            ["tabWidth"] = s.TabWidth,
            ["showLineNumbers"] = s.ShowLineNumbers,
            ["wordWrap"] = s.WordWrap,
            ["colorScheme"] = SerializeColorScheme(s.ColorScheme),
            ["externalEditorPath"] = s.ExternalEditorPath,
            ["externalEditorLineArgFormat"] = s.ExternalEditorLineArgFormat,
            ["suppressRevertHunkConfirmation"] = s.SuppressRevertHunkConfirmation,
            ["suppressDeleteFileConfirmation"] = s.SuppressDeleteFileConfirmation,
            ["windowState"] = SerializeWindowState(s.WindowState),
            ["fileListPaneWidthPixels"] = s.FileListPaneWidthPixels,
            ["repoRoots"] = SerializeStringList(s.RepoRoots),
            ["defaultCloneDestination"] = s.DefaultCloneDestination,
            ["repoUrlMappings"] = SerializeRepoUrlMappings(s.RepoUrlMappings),
        };
        return obj.ToJsonString(WriteOptions);
    }

    public static AppSettings Deserialize(JsonObject obj)
    {
        var defaults = new AppSettings();
        return new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            IgnoreWhitespace = TryBool(obj, "ignoreWhitespace") ?? defaults.IgnoreWhitespace,
            ShowIntraLineDiff = TryBool(obj, "showIntraLineDiff") ?? defaults.ShowIntraLineDiff,
            IsSideBySide = TryBool(obj, "isSideBySide") ?? defaults.IsSideBySide,
            ShowVisibleWhitespace = TryBool(obj, "showVisibleWhitespace") ?? defaults.ShowVisibleWhitespace,
            LiveUpdates = TryBool(obj, "liveUpdates") ?? defaults.LiveUpdates,
            SideVisibility = TryEnum<DiffSideVisibility>(obj, "sideVisibility") ?? defaults.SideVisibility,
            DisplayMode = TryEnum<FileListDisplayMode>(obj, "displayMode") ?? defaults.DisplayMode,
            LargeFileThresholdBytes = TryLong(obj, "largeFileThresholdBytes") ?? defaults.LargeFileThresholdBytes,
            FontFamily = TryString(obj, "fontFamily") ?? defaults.FontFamily,
            FontSize = TryDouble(obj, "fontSize") ?? defaults.FontSize,
            TabWidth = TryInt(obj, "tabWidth") ?? defaults.TabWidth,
            ShowLineNumbers = TryBool(obj, "showLineNumbers") ?? defaults.ShowLineNumbers,
            WordWrap = TryBool(obj, "wordWrap") ?? defaults.WordWrap,
            ColorScheme = DeserializeColorScheme(obj["colorScheme"]) ?? defaults.ColorScheme,
            ExternalEditorPath = TryString(obj, "externalEditorPath"),
            ExternalEditorLineArgFormat = TryString(obj, "externalEditorLineArgFormat"),
            SuppressRevertHunkConfirmation = TryBool(obj, "suppressRevertHunkConfirmation") ?? defaults.SuppressRevertHunkConfirmation,
            SuppressDeleteFileConfirmation = TryBool(obj, "suppressDeleteFileConfirmation") ?? defaults.SuppressDeleteFileConfirmation,
            WindowState = DeserializeWindowState(obj["windowState"]),
            FileListPaneWidthPixels = TryDouble(obj, "fileListPaneWidthPixels") ?? defaults.FileListPaneWidthPixels,
            RepoRoots = DeserializeStringList(obj["repoRoots"]) ?? defaults.RepoRoots,
            DefaultCloneDestination = TryString(obj, "defaultCloneDestination"),
            RepoUrlMappings = DeserializeRepoUrlMappings(obj["repoUrlMappings"]) ?? defaults.RepoUrlMappings,
        };
    }

    private static JsonNode? SerializeWindowState(WindowStateSnapshot? snapshot)
    {
        if (snapshot is null) return null;
        return new JsonObject
        {
            ["left"] = snapshot.Left,
            ["top"] = snapshot.Top,
            ["width"] = snapshot.Width,
            ["height"] = snapshot.Height,
            ["isMaximized"] = snapshot.IsMaximized,
        };
    }

    private static WindowStateSnapshot? DeserializeWindowState(JsonNode? node)
    {
        if (node is not JsonObject obj) return null;
        var left = TryDouble(obj, "left");
        var top = TryDouble(obj, "top");
        var width = TryDouble(obj, "width");
        var height = TryDouble(obj, "height");
        if (left is null || top is null || width is null || height is null) return null;
        var isMaximized = TryBool(obj, "isMaximized") ?? false;
        return new WindowStateSnapshot(left.Value, top.Value, width.Value, height.Value, isMaximized);
    }

    private static JsonObject SerializeColorScheme(ColorSchemeChoice choice) => choice switch
    {
        ColorSchemeChoice.PresetScheme p => new JsonObject
        {
            ["type"] = "preset",
            ["name"] = p.Name.ToString(),
        },
        ColorSchemeChoice.CustomScheme c => new JsonObject
        {
            ["type"] = "custom",
            ["colors"] = new JsonObject
            {
                ["addedLineBg"] = c.Colors.AddedLineBg,
                ["removedLineBg"] = c.Colors.RemovedLineBg,
                ["modifiedLineBg"] = c.Colors.ModifiedLineBg,
                ["addedIntraline"] = c.Colors.AddedIntraline,
                ["removedIntraline"] = c.Colors.RemovedIntraline,
            },
        },
        _ => throw new InvalidOperationException($"Unknown ColorSchemeChoice: {choice.GetType().Name}"),
    };

    private static ColorSchemeChoice? DeserializeColorScheme(JsonNode? node)
    {
        if (node is not JsonObject obj) return null;
        var type = TryString(obj, "type");
        return type switch
        {
            "preset" => ColorSchemeChoice.Preset(
                TryEnum<ColorSchemePresetName>(obj, "name") ?? ColorSchemePresetName.Classic),
            "custom" when obj["colors"] is JsonObject c => ColorSchemeChoice.Custom(new ColorSchemeColors(
                AddedLineBg: TryString(c, "addedLineBg") ?? "#e6ffec",
                RemovedLineBg: TryString(c, "removedLineBg") ?? "#ffeef0",
                ModifiedLineBg: TryString(c, "modifiedLineBg") ?? "#fff5d0",
                AddedIntraline: TryString(c, "addedIntraline") ?? "#aceebb",
                RemovedIntraline: TryString(c, "removedIntraline") ?? "#fdb8c0")),
            _ => null,
        };
    }

    private static JsonArray SerializeStringList(IReadOnlyList<string> values)
    {
        var arr = new JsonArray();
        foreach (var v in values) arr.Add(JsonValue.Create(v));
        return arr;
    }

    private static IReadOnlyList<string>? DeserializeStringList(JsonNode? node)
    {
        if (node is not JsonArray arr) return null;
        var list = new List<string>(arr.Count);
        foreach (var item in arr)
        {
            if (item is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
            {
                list.Add(s);
            }
        }
        return list;
    }

    private static JsonObject SerializeRepoUrlMappings(IReadOnlyDictionary<RepoUrlKey, string> mappings)
    {
        var obj = new JsonObject();
        // Sort by encoded key so diffs / hand-edits are stable across saves.
        foreach (var kvp in mappings.OrderBy(kvp => kvp.Key.ToWireString(), StringComparer.Ordinal))
        {
            obj[kvp.Key.ToWireString()] = kvp.Value;
        }
        return obj;
    }

    private static IReadOnlyDictionary<RepoUrlKey, string>? DeserializeRepoUrlMappings(JsonNode? node)
    {
        if (node is not JsonObject obj) return null;
        var result = new Dictionary<RepoUrlKey, string>();
        foreach (var kvp in obj)
        {
            var key = RepoUrlKey.TryParseWire(kvp.Key);
            if (key is null) continue; // Skip malformed keys (hand-edit typo tolerance).
            if (kvp.Value is not JsonValue v) continue;
            if (!v.TryGetValue<string>(out var path) || string.IsNullOrWhiteSpace(path)) continue;
            result[key] = path;
        }
        return result;
    }

    private static bool? TryBool(JsonObject obj, string key) =>
        obj[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    private static int? TryInt(JsonObject obj, string key) =>
        obj[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;

    private static long? TryLong(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue v) return null;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<int>(out var i)) return i;
        return null;
    }

    private static double? TryDouble(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<int>(out var i)) return i;
        return null;
    }

    private static string? TryString(JsonObject obj, string key) =>
        obj[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static T? TryEnum<T>(JsonObject obj, string key) where T : struct, Enum =>
        TryString(obj, key) is { } s && Enum.TryParse<T>(s, ignoreCase: true, out var e) ? e : null;
}
