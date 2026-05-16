using System.Text.Json.Nodes;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// Coverage for <see cref="WindowStateSnapshot"/> equality plus the
/// JSON round-trip path through <see cref="SettingsJsonSerializer"/>.
/// Lives under <c>Services/</c> rather than <c>Models/</c> to avoid
/// shadowing the <see cref="DiffViewer.Models"/> namespace from sibling
/// test files that use partial-qualified <c>Models.X</c> references.
/// </summary>
public sealed class WindowStateSnapshotTests
{
    [Fact]
    public void Record_Equality_Compares_All_Fields()
    {
        var a = new WindowStateSnapshot(100, 200, 1200, 800, IsMaximized: true);
        var b = new WindowStateSnapshot(100, 200, 1200, 800, IsMaximized: true);
        var c = new WindowStateSnapshot(100, 200, 1200, 800, IsMaximized: false);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void Json_RoundTrip_PreservesAllFields_ViaAppSettings()
    {
        var snapshot = new WindowStateSnapshot(-100.5, 42.25, 1234.5, 678.75, IsMaximized: true);
        var settings = new AppSettings { WindowState = snapshot };

        var json = SettingsJsonSerializer.Serialize(settings);
        var reloaded = SettingsJsonSerializer.Deserialize(JsonNode.Parse(json)!.AsObject());

        reloaded.WindowState.Should().Be(snapshot);
    }

    [Fact]
    public void Null_Snapshot_Serializes_To_Null_Field()
    {
        var settings = new AppSettings { WindowState = null };
        var json = SettingsJsonSerializer.Serialize(settings);
        var obj = JsonNode.Parse(json)!.AsObject();

        // WhenWritingNull is enabled in the serializer; the key may be
        // present-with-null or absent. Either is acceptable.
        if (obj.ContainsKey("windowState"))
        {
            obj["windowState"].Should().BeNull();
        }

        var reloaded = SettingsJsonSerializer.Deserialize(obj);
        reloaded.WindowState.Should().BeNull();
    }

    [Fact]
    public void Partial_WindowState_Object_Deserializes_As_Null()
    {
        // A hand-edited file with a malformed (incomplete) windowState
        // object should not silently produce a snapshot with default
        // zeros. Safer to treat the whole snapshot as missing so the
        // window opens at defaults.
        var obj = new JsonObject
        {
            ["schemaVersion"] = AppSettings.CurrentSchemaVersion,
            ["windowState"] = new JsonObject
            {
                ["left"] = 100,
                ["top"] = 200,
                // width and height intentionally missing
                ["isMaximized"] = false,
            },
        };

        var loaded = SettingsJsonSerializer.Deserialize(obj);
        loaded.WindowState.Should().BeNull();
    }
}
