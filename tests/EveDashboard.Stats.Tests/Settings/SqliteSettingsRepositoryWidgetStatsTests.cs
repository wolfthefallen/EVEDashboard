using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using EveDashboard.Core.Storage;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Schema;
using EveDashboard.Stats.Settings;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Stats.Tests.Settings;

/// <summary>
/// Per-widget stat customisation. Covers the four methods on
/// <see cref="SqliteSettingsRepository"/> — <c>Get/SetSessionStatsConfigAsync</c>
/// and <c>Get/SetPerPilotStatsConfigAsync</c> — plus their setting key shapes,
/// null/malformed branches, and SettingChanged-event semantics.
/// </summary>
public class SqliteSettingsRepositoryWidgetStatsTests
{
    private const string InMemory = "Data Source=:memory:";

    private static SqliteDatabase OpenInMemory() =>
        new(InMemory, NullLogger<SqliteDatabase>.Instance);

    private static SqliteSettingsRepository NewRepo(
        ISqliteDatabase db,
        ILogger<SqliteSettingsRepository>? log = null) =>
        new(db, log ?? NullLogger<SqliteSettingsRepository>.Instance);

    private static async Task<SqliteDatabase> NewSchemaAsync()
    {
        var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);
        return db;
    }

    private static async Task UpsertSettingAsync(SqliteDatabase db, string key, string value)
    {
        await db.ExecuteNonQueryAsync(
            "INSERT INTO setting (key, value) VALUES ($k, $v) " +
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            new Dictionary<string, object?> { ["$k"] = key, ["$v"] = value });
    }

    private static WidgetStatsConfig SampleConfig() =>
        new(new[]
        {
            new StatEntry("M3PerSecond", Hidden: false, StatEmphasis.None),
            new StatEntry("PilotName", Hidden: true, StatEmphasis.Header0),
            new StatEntry("TotalTimeMined", Hidden: false, StatEmphasis.Hero),
        });

    // ---- SessionStats config ------------------------------------------------------

    [Fact]
    public async Task GetSessionStatsConfigAsync_returns_null_when_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var got = await repo.GetSessionStatsConfigAsync();

        got.Should().BeNull();
    }

    [Fact]
    public async Task SetSessionStatsConfigAsync_then_GetSessionStatsConfigAsync_round_trips()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var cfg = SampleConfig();

        await repo.SetSessionStatsConfigAsync(cfg);
        var got = await repo.GetSessionStatsConfigAsync();

        got.Should().NotBeNull();
        got!.Entries.Should().Equal(cfg.Entries);
    }

    [Fact]
    public async Task GetSessionStatsConfigAsync_returns_null_and_logs_warning_when_malformed()
    {
        await using var db = await NewSchemaAsync();
        var captureLog = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, captureLog);

        // Malformed: key present but value is non-JSON garbage.
        await UpsertSettingAsync(db, "session_stats_config", "{not-json");

        var got = await repo.GetSessionStatsConfigAsync();

        got.Should().BeNull();
        captureLog.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("session_stats_config"));
    }

    [Fact]
    public async Task SetSessionStatsConfigAsync_raises_SettingChanged_with_matching_key()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var capturedKeys = new List<string>();
        repo.SettingChanged += (_, args) => capturedKeys.Add(args.Key);

        await repo.SetSessionStatsConfigAsync(SampleConfig());

        capturedKeys.Should().ContainSingle().Which.Should().Be("session_stats_config");
    }

    [Fact]
    public async Task SetSessionStatsConfigAsync_throws_on_null_value()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        Func<Task> act = () => repo.SetSessionStatsConfigAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---- Per-pilot config ---------------------------------------------------------

    [Fact]
    public async Task GetPerPilotStatsConfigAsync_returns_null_when_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var got = await repo.GetPerPilotStatsConfigAsync(pilotId: 90863271);

        got.Should().BeNull();
    }

    [Fact]
    public async Task SetPerPilotStatsConfigAsync_then_GetPerPilotStatsConfigAsync_round_trips_for_one_pilot()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var cfg = SampleConfig();

        await repo.SetPerPilotStatsConfigAsync(pilotId: 90863271, cfg);
        var got = await repo.GetPerPilotStatsConfigAsync(pilotId: 90863271);

        got.Should().NotBeNull();
        got!.Entries.Should().Equal(cfg.Entries);
    }

    [Fact]
    public async Task PerPilot_configs_are_isolated_by_pilot_id()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var alice = new WidgetStatsConfig(new[] { new StatEntry("M3PerSecond", false, StatEmphasis.None) });
        var bob   = new WidgetStatsConfig(new[] { new StatEntry("IskPerHour",  true,  StatEmphasis.Hero) });

        await repo.SetPerPilotStatsConfigAsync(pilotId: 1, alice);
        await repo.SetPerPilotStatsConfigAsync(pilotId: 2, bob);

        var aliceBack = await repo.GetPerPilotStatsConfigAsync(pilotId: 1);
        var bobBack   = await repo.GetPerPilotStatsConfigAsync(pilotId: 2);

        aliceBack!.Entries.Should().Equal(alice.Entries);
        bobBack!.Entries.Should().Equal(bob.Entries);
    }

    [Fact]
    public async Task GetPerPilotStatsConfigAsync_returns_null_and_logs_warning_when_malformed()
    {
        await using var db = await NewSchemaAsync();
        var captureLog = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, captureLog);

        await UpsertSettingAsync(db, "per_pilot_stats_config.42", "{not-json");

        var got = await repo.GetPerPilotStatsConfigAsync(pilotId: 42);

        got.Should().BeNull();
        captureLog.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("per_pilot_stats_config.42"));
    }

    [Fact]
    public async Task SetPerPilotStatsConfigAsync_raises_SettingChanged_with_pilot_specific_key()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var capturedKeys = new List<string>();
        repo.SettingChanged += (_, args) => capturedKeys.Add(args.Key);

        await repo.SetPerPilotStatsConfigAsync(pilotId: 12345, SampleConfig());

        capturedKeys.Should().ContainSingle().Which.Should().Be("per_pilot_stats_config.12345");
    }

    [Fact]
    public async Task SetPerPilotStatsConfigAsync_throws_on_null_value()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        Func<Task> act = () => repo.SetPerPilotStatsConfigAsync(pilotId: 1, value: null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SetPerPilotStatsConfigAsync_round_trips_for_negative_pilot_id()
    {
        // Defensive: pilot ids are always positive in EVE, but the repo MUST not
        // clamp / sanitise them — that would lose user data on a bug elsewhere.
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var cfg = SampleConfig();

        await repo.SetPerPilotStatsConfigAsync(pilotId: -7, cfg);
        var got = await repo.GetPerPilotStatsConfigAsync(pilotId: -7);

        got.Should().NotBeNull();
        got!.Entries.Should().Equal(cfg.Entries);
    }

    // ---- EnsureSeedDefaultsAsync does NOT seed the new keys -----------------------

    [Fact]
    public async Task EnsureSeedDefaultsAsync_does_not_seed_session_stats_config()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.EnsureSeedDefaultsAsync();

        var got = await repo.GetSessionStatsConfigAsync();
        got.Should().BeNull();
    }

    [Fact]
    public async Task EnsureSeedDefaultsAsync_does_not_seed_per_pilot_stats_config()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.EnsureSeedDefaultsAsync();

        var got = await repo.GetPerPilotStatsConfigAsync(pilotId: 999);
        got.Should().BeNull();
    }
}
