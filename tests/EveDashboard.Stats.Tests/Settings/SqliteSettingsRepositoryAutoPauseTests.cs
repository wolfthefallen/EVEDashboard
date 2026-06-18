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
/// Plan 37 — auto-pause settings tests. Two new keys (one int + one bool)
/// behind <see cref="SqliteSettingsRepository.GetAutoPauseIdleMinutesAsync"/> /
/// <see cref="SqliteSettingsRepository.SetAutoPauseIdleMinutesAsync"/> and
/// <see cref="SqliteSettingsRepository.GetAutoPauseSoundEnabledAsync"/> /
/// <see cref="SqliteSettingsRepository.SetAutoPauseSoundEnabledAsync"/>.
/// </summary>
public class SqliteSettingsRepositoryAutoPauseTests
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

    // ---- Idle minutes: defaults / clamp / malformed --------------------------------

    [Fact]
    public async Task GetAutoPauseIdleMinutesAsync_returns_default_5_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var minutes = await repo.GetAutoPauseIdleMinutesAsync();

        minutes.Should().Be(5);
    }

    [Theory]
    [InlineData("0", 1)]    // below range -> clamped up
    [InlineData("-3", 1)]   // negative -> clamped to min
    [InlineData("999", 60)] // above range -> clamped down
    public async Task GetAutoPauseIdleMinutesAsync_clamps_out_of_range_stored_value(string stored, int expected)
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "auto_pause_idle_minutes", stored);
        var capturing = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, capturing);

        var minutes = await repo.GetAutoPauseIdleMinutesAsync();

        minutes.Should().Be(expected);
        capturing.Entries.Should().Contain(e => e.Message.Contains("clamping"));
    }

    [Fact]
    public async Task GetAutoPauseIdleMinutesAsync_returns_default_with_warning_on_malformed()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "auto_pause_idle_minutes", "abc");
        var capturing = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, capturing);

        var minutes = await repo.GetAutoPauseIdleMinutesAsync();

        minutes.Should().Be(5);
        capturing.Entries.Should().Contain(e => e.Message.Contains("not a valid integer"));
    }

    [Fact]
    public async Task SetAutoPauseIdleMinutesAsync_persists_in_range_value()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetAutoPauseIdleMinutesAsync(15);

        var minutes = await repo.GetAutoPauseIdleMinutesAsync();
        minutes.Should().Be(15);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(100, 60)]
    public async Task SetAutoPauseIdleMinutesAsync_clamps_out_of_range(int input, int expected)
    {
        await using var db = await NewSchemaAsync();
        var capturing = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, capturing);

        await repo.SetAutoPauseIdleMinutesAsync(input);

        var minutes = await repo.GetAutoPauseIdleMinutesAsync();
        minutes.Should().Be(expected);
        capturing.Entries.Should().Contain(e => e.Message.Contains("clamping"));
    }

    [Fact]
    public async Task SetAutoPauseIdleMinutesAsync_raises_SettingChanged_with_correct_key()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetAutoPauseIdleMinutesAsync(10);

        keys.Should().ContainSingle().Which.Should().Be("auto_pause_idle_minutes");
    }

    // ---- Sound enabled: defaults / malformed ---------------------------------------

    [Fact]
    public async Task GetAutoPauseSoundEnabledAsync_returns_true_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var enabled = await repo.GetAutoPauseSoundEnabledAsync();

        enabled.Should().BeTrue();
    }

    [Theory]
    [InlineData("true",  true)]
    [InlineData("false", false)]
    public async Task GetAutoPauseSoundEnabledAsync_round_trips_canonical_values(string stored, bool expected)
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "auto_pause_sound_enabled", stored);
        var repo = NewRepo(db);

        var enabled = await repo.GetAutoPauseSoundEnabledAsync();

        enabled.Should().Be(expected);
    }

    [Fact]
    public async Task GetAutoPauseSoundEnabledAsync_returns_default_with_warning_on_unknown_value()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "auto_pause_sound_enabled", "yes");
        var capturing = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, capturing);

        var enabled = await repo.GetAutoPauseSoundEnabledAsync();

        enabled.Should().BeTrue();
        capturing.Entries.Should().Contain(e => e.Message.Contains("Unknown auto_pause_sound_enabled"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetAutoPauseSoundEnabledAsync_persists_and_raises_SettingChanged(bool value)
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetAutoPauseSoundEnabledAsync(value);

        var read = await repo.GetAutoPauseSoundEnabledAsync();
        read.Should().Be(value);
        keys.Should().ContainSingle().Which.Should().Be("auto_pause_sound_enabled");
    }

    // ---- Sound volume: defaults / clamp / malformed (plan 50) ----------------------

    [Fact]
    public async Task GetAutoPauseSoundVolumeAsync_returns_default_65_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var volume = await repo.GetAutoPauseSoundVolumeAsync();

        volume.Should().Be(65);
    }

    [Theory]
    [InlineData("-10", 0)]   // below range -> clamped to min
    [InlineData("250", 100)] // above range -> clamped to max
    public async Task GetAutoPauseSoundVolumeAsync_clamps_out_of_range_stored_value(string stored, int expected)
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "auto_pause_sound_volume", stored);
        var capturing = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, capturing);

        var volume = await repo.GetAutoPauseSoundVolumeAsync();

        volume.Should().Be(expected);
        capturing.Entries.Should().Contain(e => e.Message.Contains("clamping"));
    }

    [Fact]
    public async Task GetAutoPauseSoundVolumeAsync_returns_default_with_warning_on_malformed()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "auto_pause_sound_volume", "loud");
        var capturing = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, capturing);

        var volume = await repo.GetAutoPauseSoundVolumeAsync();

        volume.Should().Be(65);
        capturing.Entries.Should().Contain(e => e.Message.Contains("not a valid integer"));
    }

    [Fact]
    public async Task SetAutoPauseSoundVolumeAsync_persists_in_range_value()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetAutoPauseSoundVolumeAsync(40);

        var volume = await repo.GetAutoPauseSoundVolumeAsync();
        volume.Should().Be(40);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(150, 100)]
    public async Task SetAutoPauseSoundVolumeAsync_clamps_out_of_range(int input, int expected)
    {
        await using var db = await NewSchemaAsync();
        var capturing = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, capturing);

        await repo.SetAutoPauseSoundVolumeAsync(input);

        var volume = await repo.GetAutoPauseSoundVolumeAsync();
        volume.Should().Be(expected);
        capturing.Entries.Should().Contain(e => e.Message.Contains("clamping"));
    }

    [Fact]
    public async Task SetAutoPauseSoundVolumeAsync_raises_SettingChanged_with_correct_key()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetAutoPauseSoundVolumeAsync(30);

        keys.Should().ContainSingle().Which.Should().Be("auto_pause_sound_volume");
    }

    // ---- Seed defaults --------------------------------------------------------------

    [Fact]
    public async Task EnsureSeedDefaultsAsync_seeds_idle_minutes_to_5_and_sound_to_true()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.EnsureSeedDefaultsAsync();

        (await repo.GetAutoPauseIdleMinutesAsync()).Should().Be(5);
        (await repo.GetAutoPauseSoundEnabledAsync()).Should().BeTrue();
        (await repo.GetAutoPauseSoundVolumeAsync()).Should().Be(65);
    }

    [Fact]
    public async Task EnsureSeedDefaultsAsync_does_not_overwrite_existing_sound_volume()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "auto_pause_sound_volume", "20");
        var repo = NewRepo(db);

        await repo.EnsureSeedDefaultsAsync();

        (await repo.GetAutoPauseSoundVolumeAsync()).Should().Be(20);
    }

    [Fact]
    public async Task EnsureSeedDefaultsAsync_does_not_overwrite_existing_idle_minutes()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "auto_pause_idle_minutes", "12");
        var repo = NewRepo(db);

        await repo.EnsureSeedDefaultsAsync();

        (await repo.GetAutoPauseIdleMinutesAsync()).Should().Be(12);
    }

    [Fact]
    public async Task EnsureSeedDefaultsAsync_does_not_overwrite_existing_sound_setting()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "auto_pause_sound_enabled", "false");
        var repo = NewRepo(db);

        await repo.EnsureSeedDefaultsAsync();

        (await repo.GetAutoPauseSoundEnabledAsync()).Should().BeFalse();
    }
}
