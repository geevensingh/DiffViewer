using System.Text.Json.Nodes;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// Coverage for the two PR auto-refresh settings introduced in v10:
/// JSON round-trip, defaults, clamping, and the v9→v10 migration.
/// </summary>
public sealed class PullRequestAutoRefreshSettingsTests
{
    [Fact]
    public void RoundTrip_PreservesBothFieldsAtNonDefaultValues()
    {
        var settings = new AppSettings
        {
            PullRequestAutoRefresh = false,
            PullRequestPollIntervalSeconds = 120,
        };

        var json = SettingsJsonSerializer.Serialize(settings);
        var reloaded = SettingsJsonSerializer.Deserialize(JsonNode.Parse(json)!.AsObject());

        reloaded.PullRequestAutoRefresh.Should().BeFalse();
        reloaded.PullRequestPollIntervalSeconds.Should().Be(120);
    }

    [Fact]
    public void Defaults_RoundTripExactly()
    {
        var settings = new AppSettings();

        var json = SettingsJsonSerializer.Serialize(settings);
        var reloaded = SettingsJsonSerializer.Deserialize(JsonNode.Parse(json)!.AsObject());

        reloaded.PullRequestAutoRefresh.Should().BeTrue();
        reloaded.PullRequestPollIntervalSeconds
            .Should().Be(AppSettings.PullRequestPollIntervalSecondsDefault);
    }

    [Fact]
    public void Deserialize_BelowMin_ClampsToMin()
    {
        var obj = new JsonObject
        {
            ["schemaVersion"] = AppSettings.CurrentSchemaVersion,
            ["pullRequestPollIntervalSeconds"] = 5,
        };

        var loaded = SettingsJsonSerializer.Deserialize(obj);
        loaded.PullRequestPollIntervalSeconds
            .Should().Be(AppSettings.PullRequestPollIntervalSecondsMin);
    }

    [Fact]
    public void Deserialize_AboveMax_ClampsToMax()
    {
        var obj = new JsonObject
        {
            ["schemaVersion"] = AppSettings.CurrentSchemaVersion,
            ["pullRequestPollIntervalSeconds"] = 999999,
        };

        var loaded = SettingsJsonSerializer.Deserialize(obj);
        loaded.PullRequestPollIntervalSeconds
            .Should().Be(AppSettings.PullRequestPollIntervalSecondsMax);
    }

    [Fact]
    public void Deserialize_AtMin_KeepsExactValue()
    {
        var obj = new JsonObject
        {
            ["schemaVersion"] = AppSettings.CurrentSchemaVersion,
            ["pullRequestPollIntervalSeconds"] = AppSettings.PullRequestPollIntervalSecondsMin,
        };

        var loaded = SettingsJsonSerializer.Deserialize(obj);
        loaded.PullRequestPollIntervalSeconds
            .Should().Be(AppSettings.PullRequestPollIntervalSecondsMin);
    }

    [Fact]
    public void Deserialize_AtMax_KeepsExactValue()
    {
        var obj = new JsonObject
        {
            ["schemaVersion"] = AppSettings.CurrentSchemaVersion,
            ["pullRequestPollIntervalSeconds"] = AppSettings.PullRequestPollIntervalSecondsMax,
        };

        var loaded = SettingsJsonSerializer.Deserialize(obj);
        loaded.PullRequestPollIntervalSeconds
            .Should().Be(AppSettings.PullRequestPollIntervalSecondsMax);
    }

    [Fact]
    public void MigrationV9ToV10_LeavesFieldsAtDefaults()
    {
        // Simulate a v9-shaped settings.json: every other field present,
        // both new fields absent. The migration should be a no-op and the
        // deserializer should fill in the defaults.
        var v9Json = new JsonObject
        {
            ["schemaVersion"] = 9,
            ["liveUpdates"] = true,
        };

        var migrated = SettingsMigrations.MigrateUpTo(v9Json, fromVersion: 9, toVersion: 10);
        migrated["schemaVersion"]!.GetValue<int>().Should().Be(10);

        var loaded = SettingsJsonSerializer.Deserialize(migrated);
        loaded.PullRequestAutoRefresh.Should().BeTrue();
        loaded.PullRequestPollIntervalSeconds
            .Should().Be(AppSettings.PullRequestPollIntervalSecondsDefault);
    }
}
