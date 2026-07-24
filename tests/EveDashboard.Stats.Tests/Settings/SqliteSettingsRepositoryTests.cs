using EveDashboard.Core.Layout;
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

public class SqliteSettingsRepositoryTests
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

    private static async Task SeedStationAsync(SqliteDatabase db, long stationId)
    {
        await db.ExecuteNonQueryAsync(
            "INSERT INTO market_stations (station_id, system_name, station_name, region_id, region_name) " +
            "VALUES ($sid, 'TestSys', 'Test Station', 10000000, 'TestRegion')",
            new Dictionary<string, object?> { ["$sid"] = stationId });
    }

    private static async Task UpsertSettingAsync(SqliteDatabase db, string key, string value)
    {
        await db.ExecuteNonQueryAsync(
            "INSERT INTO setting (key, value) VALUES ($k, $v) " +
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            new Dictionary<string, object?> { ["$k"] = key, ["$v"] = value });
    }

    // ---- Constructor / null-arg ---------------------------------------------------

    [Fact]
    public void Ctor_throws_on_null_database()
    {
        Action act = () => _ = new SqliteSettingsRepository(
            null!,
            NullLogger<SqliteSettingsRepository>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Ctor_throws_on_null_logger()
    {
        await using var db = OpenInMemory();
        Action act = () => _ = new SqliteSettingsRepository(db, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ---- Theme --------------------------------------------------------------------

    [Fact]
    public async Task GetThemeAsync_returns_dark_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var theme = await repo.GetThemeAsync();

        theme.Should().Be(ThemeMode.Dark);
    }

    [Theory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public async Task SetThemeAsync_then_GetThemeAsync_returns_set_value(ThemeMode mode)
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetThemeAsync(mode);
        var roundTrip = await repo.GetThemeAsync();

        roundTrip.Should().Be(mode);
    }

    [Fact]
    public async Task GetThemeAsync_returns_default_and_logs_warning_when_value_unrecognised()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "theme", "neon-pink");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        var theme = await repo.GetThemeAsync();

        theme.Should().Be(ThemeMode.Dark);
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("neon-pink", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetThemeAsync_raises_SettingChanged_with_key_theme()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetThemeAsync(ThemeMode.Light);

        keys.Should().ContainSingle().Which.Should().Be("theme");
    }

    [Fact]
    public async Task SettingChanged_isolates_per_handler_when_one_handler_throws()
    {
        await using var db = await NewSchemaAsync();
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);
        var observed = new List<string>();
        repo.SettingChanged += (_, _) => throw new InvalidOperationException("first handler boom");
        repo.SettingChanged += (_, e) => observed.Add(e.Key);

        await repo.SetThemeAsync(ThemeMode.Light);

        observed.Should().ContainSingle().Which.Should().Be("theme");
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Exception is InvalidOperationException);
    }

    // ---- PriceMode ----------------------------------------------------------------

    [Fact]
    public async Task GetPriceModeAsync_returns_buy_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var mode = await repo.GetPriceModeAsync();

        mode.Should().Be(PriceMode.Buy);
    }

    [Theory]
    [InlineData(PriceMode.Buy)]
    [InlineData(PriceMode.Sell)]
    [InlineData(PriceMode.Split)]
    public async Task SetPriceModeAsync_then_GetPriceModeAsync_returns_set_value(PriceMode mode)
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetPriceModeAsync(mode);
        var roundTrip = await repo.GetPriceModeAsync();

        roundTrip.Should().Be(mode);
    }

    [Fact]
    public async Task GetPriceModeAsync_returns_default_and_logs_warning_when_value_unrecognised()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "price_mode", "barter");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        var mode = await repo.GetPriceModeAsync();

        mode.Should().Be(PriceMode.Buy);
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("barter", StringComparison.Ordinal));
    }

    // ---- PriceSource (plan 53) ----------------------------------------------------

    [Fact]
    public async Task GetPriceSourceAsync_returns_goonmetrics_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        (await repo.GetPriceSourceAsync()).Should().Be(PriceSource.GoonMetrics);
    }

    [Theory]
    [InlineData(PriceSource.GoonMetrics)]
    [InlineData(PriceSource.Esi)]
    public async Task SetPriceSourceAsync_then_GetPriceSourceAsync_round_trips(PriceSource source)
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetPriceSourceAsync(source);

        (await repo.GetPriceSourceAsync()).Should().Be(source);
    }

    [Fact]
    public async Task GetPriceSourceAsync_returns_default_and_logs_warning_when_value_unrecognised()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "price_source", "nasdaq");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        (await repo.GetPriceSourceAsync()).Should().Be(PriceSource.GoonMetrics);
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("nasdaq", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetPriceSourceAsync_raises_SettingChanged_with_price_source_key()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        string? raisedKey = null;
        repo.SettingChanged += (_, e) => raisedKey = e.Key;

        await repo.SetPriceSourceAsync(PriceSource.Esi);

        raisedKey.Should().Be("price_source");
    }

    [Fact]
    public async Task SetPriceModeAsync_raises_SettingChanged_with_key_price_mode()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetPriceModeAsync(PriceMode.Sell);

        keys.Should().ContainSingle().Which.Should().Be("price_mode");
    }

    // ---- RefiningYieldPercent -----------------------------------------------------

    [Fact]
    public async Task GetRefiningYieldPercentAsync_returns_default_90_6_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var yield = await repo.GetRefiningYieldPercentAsync();

        yield.Should().Be(90.6);
    }

    [Fact]
    public async Task SetRefiningYieldPercentAsync_round_trips_value_in_range()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetRefiningYieldPercentAsync(72.5);
        var roundTrip = await repo.GetRefiningYieldPercentAsync();

        roundTrip.Should().Be(72.5);
    }

    [Fact]
    public async Task SetRefiningYieldPercentAsync_clamps_above_max_and_logs_warning()
    {
        await using var db = await NewSchemaAsync();
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        await repo.SetRefiningYieldPercentAsync(125.0);
        var roundTrip = await repo.GetRefiningYieldPercentAsync();

        roundTrip.Should().Be(90.6);
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("125", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetRefiningYieldPercentAsync_clamps_below_min_and_logs_warning()
    {
        await using var db = await NewSchemaAsync();
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        await repo.SetRefiningYieldPercentAsync(10.0);
        var roundTrip = await repo.GetRefiningYieldPercentAsync();

        roundTrip.Should().Be(50.0);
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("10", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetRefiningYieldPercentAsync_raises_SettingChanged_with_key_refining_yield_percent()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetRefiningYieldPercentAsync(80.0);

        keys.Should().ContainSingle().Which.Should().Be("refining_yield_percent");
    }

    // ---- SelectedStationId --------------------------------------------------------

    [Fact]
    public async Task GetSelectedStationIdAsync_returns_default_jita_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var id = await repo.GetSelectedStationIdAsync();

        id.Should().Be(60003760L);
    }

    [Fact]
    public async Task SetSelectedStationIdAsync_then_GetSelectedStationIdAsync_returns_set_value_when_station_present()
    {
        await using var db = await NewSchemaAsync();
        await SeedStationAsync(db, 1234567890L);
        var repo = NewRepo(db);

        await repo.SetSelectedStationIdAsync(1234567890L);
        var id = await repo.GetSelectedStationIdAsync();

        id.Should().Be(1234567890L);
    }

    [Fact]
    public async Task GetSelectedStationIdAsync_returns_default_and_logs_warning_when_saved_id_not_in_market_stations()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "selected_station_id", "9999999999");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        var id = await repo.GetSelectedStationIdAsync();

        id.Should().Be(60003760L);
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("9999999999", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetSelectedStationIdAsync_returns_default_and_logs_warning_when_value_not_a_long()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "selected_station_id", "not-a-number");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        var id = await repo.GetSelectedStationIdAsync();

        id.Should().Be(60003760L);
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("not-a-number", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetSelectedStationIdAsync_raises_SettingChanged_with_key_selected_station_id()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetSelectedStationIdAsync(42L);

        keys.Should().ContainSingle().Which.Should().Be("selected_station_id");
    }

    // ---- WindowGeometry -----------------------------------------------------------

    [Fact]
    public async Task GetWindowGeometryAsync_returns_null_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var geom = await repo.GetWindowGeometryAsync();

        geom.Should().BeNull();
    }

    [Fact]
    public async Task SetWindowGeometryAsync_then_GetWindowGeometryAsync_round_trips_all_five_fields()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var original = new WindowGeometry(
            Width: 1600,
            Height: 900,
            Left: 120,
            Top: 60,
            IsMaximized: true);

        await repo.SetWindowGeometryAsync(original);
        var roundTrip = await repo.GetWindowGeometryAsync();

        roundTrip.Should().Be(original);
    }

    [Fact]
    public async Task GetWindowGeometryAsync_returns_null_and_logs_warning_when_json_malformed()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "window_geometry", "{not-json");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        var geom = await repo.GetWindowGeometryAsync();

        geom.Should().BeNull();
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Exception is System.Text.Json.JsonException);
    }

    [Fact]
    public async Task SetWindowGeometryAsync_raises_SettingChanged_with_key_window_geometry()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetWindowGeometryAsync(new WindowGeometry(800, 600, 0, 0, false));

        keys.Should().ContainSingle().Which.Should().Be("window_geometry");
    }

    [Fact]
    public async Task SetWindowGeometryAsync_throws_on_null_value()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        Func<Task> act = async () => await repo.SetWindowGeometryAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---- WidgetLayout (plan 19) ---------------------------------------------------

    [Fact]
    public async Task GetWidgetLayoutAsync_returns_null_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var layout = await repo.GetWidgetLayoutAsync();

        layout.Should().BeNull();
    }

    [Fact]
    public async Task SetWidgetLayoutAsync_then_GetWidgetLayoutAsync_round_trips_stats_and_pilot_items_with_hidden()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var original = new WidgetLayout(new[]
        {
            new WidgetLayoutItem(new WidgetDescriptor.SessionStats(), Hidden: true),
            new WidgetLayoutItem(new WidgetDescriptor.Pilot(90863271), Hidden: true),
            new WidgetLayoutItem(new WidgetDescriptor.Pilot(2123642033), Hidden: false),
        });

        await repo.SetWidgetLayoutAsync(original);
        var roundTrip = await repo.GetWidgetLayoutAsync();

        roundTrip.Should().NotBeNull();
        roundTrip!.Items.Should().HaveCount(3);
        roundTrip.Items[0].Descriptor.Should().BeOfType<WidgetDescriptor.SessionStats>();
        roundTrip.Items[0].Hidden.Should().BeTrue();
        roundTrip.Items[1].Descriptor.Should().BeEquivalentTo(new WidgetDescriptor.Pilot(90863271));
        roundTrip.Items[1].Hidden.Should().BeTrue();
        roundTrip.Items[2].Descriptor.Should().BeEquivalentTo(new WidgetDescriptor.Pilot(2123642033));
        roundTrip.Items[2].Hidden.Should().BeFalse();
    }

    [Fact]
    public async Task GetWidgetLayoutAsync_returns_null_for_post_plan_25_removed_session_config_kind()
    {
        // Post-plan-25 follow-up: WidgetDescriptor.SessionConfig was removed.
        // Stale JSON with the now-removed "session_config" kind falls through
        // to the unknown-kind branch — repo logs Warning and returns null;
        // caller falls back to default layout.
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "widget_layout",
            """{"items":[{"kind":"session_config","hidden":false}]}""");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        var layout = await repo.GetWidgetLayoutAsync();

        layout.Should().BeNull();
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Exception is System.Text.Json.JsonException);
    }

    [Fact]
    public async Task GetWidgetLayoutAsync_returns_null_for_pre_plan_24_session_kind()
    {
        // Plan 24 deleted WidgetDescriptor.Session; the old "session" kind discriminator
        // is no longer registered. Per MEMORY.md -> feedback_no_migrations_until_release,
        // stale JSON falls through unchanged: deserialize fails, repo logs Warning + returns null,
        // caller falls back to default layout.
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "widget_layout",
            """{"items":[{"kind":"session","hidden":false}]}""");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        var layout = await repo.GetWidgetLayoutAsync();

        layout.Should().BeNull();
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Exception is System.Text.Json.JsonException);
    }

    [Fact]
    public async Task GetWidgetLayoutAsync_returns_null_and_logs_warning_when_json_malformed()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "widget_layout", "{not-json");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        var layout = await repo.GetWidgetLayoutAsync();

        layout.Should().BeNull();
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Exception is System.Text.Json.JsonException);
    }

    [Fact]
    public async Task GetWidgetLayoutAsync_returns_null_and_logs_warning_when_unknown_kind_discriminator()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "widget_layout",
            """{"items":[{"kind":"asteroid","hidden":false}]}""");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        var layout = await repo.GetWidgetLayoutAsync();

        layout.Should().BeNull();
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Exception is System.Text.Json.JsonException);
    }

    [Fact]
    public async Task SetWidgetLayoutAsync_raises_SettingChanged_with_key_widget_layout()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetWidgetLayoutAsync(new WidgetLayout(new[]
        {
            new WidgetLayoutItem(new WidgetDescriptor.SessionStats(), Hidden: false),
        }));

        keys.Should().ContainSingle().Which.Should().Be("widget_layout");
    }

    [Fact]
    public async Task SetWidgetLayoutAsync_throws_on_null_value()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        Func<Task> act = async () => await repo.SetWidgetLayoutAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---- EnsureSeedDefaultsAsync --------------------------------------------------

    [Fact]
    public async Task EnsureSeedDefaultsAsync_seeds_ten_keys_on_fresh_db_skips_window_geometry()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.EnsureSeedDefaultsAsync();

        var keys = await db.QueryAsync(
            "SELECT key FROM setting ORDER BY key",
            r => r.GetString(0));

        // schema_version is seeded by SchemaInitializer (plan 11); the repo
        // defaults are theme / price_mode / refining_yield_percent /
        // selected_station_id (plan 14), auto_pause_idle_minutes +
        // auto_pause_sound_enabled (plan 37), auto_pause_sound_volume (plan 50),
        // auto_show_refined_materials (plan 43), and show_pilot_refined_output
        // (plan 49). window_geometry intentionally NOT seeded (round-2 3.1 = B).
        keys.Should().BeEquivalentTo(new[]
        {
            "auto_pause_idle_minutes",
            "auto_pause_sound_enabled",
            "auto_pause_sound_volume",
            "auto_show_refined_materials",
            "price_mode",
            "price_source",
            "refining_yield_percent",
            "schema_version",
            "selected_station_id",
            "show_pilot_refined_output",
            "theme",
        });

        (await repo.GetThemeAsync()).Should().Be(ThemeMode.Dark);
        (await repo.GetPriceModeAsync()).Should().Be(PriceMode.Buy);
        (await repo.GetPriceSourceAsync()).Should().Be(PriceSource.GoonMetrics);
        (await repo.GetRefiningYieldPercentAsync()).Should().Be(90.6);
        (await repo.GetWindowGeometryAsync()).Should().BeNull();
        (await repo.GetAboutWindowGeometryAsync()).Should().BeNull();
        (await repo.GetAutoPauseIdleMinutesAsync()).Should().Be(5);
        (await repo.GetAutoPauseSoundEnabledAsync()).Should().BeTrue();
        (await repo.GetShowPilotRefinedOutputAsync()).Should().BeTrue();
        (await repo.GetAutoPauseSoundVolumeAsync()).Should().Be(65);
    }

    [Fact]
    public async Task EnsureSeedDefaultsAsync_does_not_overwrite_existing_values()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        await repo.SetThemeAsync(ThemeMode.Light);
        await repo.SetRefiningYieldPercentAsync(75.0);

        await repo.EnsureSeedDefaultsAsync();

        (await repo.GetThemeAsync()).Should().Be(ThemeMode.Light);
        (await repo.GetRefiningYieldPercentAsync()).Should().Be(75.0);
    }

    [Fact]
    public async Task EnsureSeedDefaultsAsync_is_idempotent_when_run_twice()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.EnsureSeedDefaultsAsync();
        await repo.EnsureSeedDefaultsAsync();

        // Theme survives both invocations as the seeded default.
        (await repo.GetThemeAsync()).Should().Be(ThemeMode.Dark);
    }

    // ---- ReportsWindowGeometry (plan 25) ------------------------------------------

    [Fact]
    public async Task GetReportsWindowGeometryAsync_returns_null_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var geom = await repo.GetReportsWindowGeometryAsync();

        geom.Should().BeNull();
    }

    [Fact]
    public async Task SetReportsWindowGeometryAsync_then_GetReportsWindowGeometryAsync_round_trips()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var original = new WindowGeometry(
            Width: 1100, Height: 700, Left: 200, Top: 100, IsMaximized: false);

        await repo.SetReportsWindowGeometryAsync(original);
        var roundTrip = await repo.GetReportsWindowGeometryAsync();

        roundTrip.Should().Be(original);
    }

    [Fact]
    public async Task GetReportsWindowGeometryAsync_returns_null_and_logs_warning_when_json_malformed()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "reports_window_geometry", "{not-json");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        var geom = await repo.GetReportsWindowGeometryAsync();

        geom.Should().BeNull();
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Exception is System.Text.Json.JsonException);
    }

    [Fact]
    public async Task SetReportsWindowGeometryAsync_raises_SettingChanged_with_key_reports_window_geometry()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetReportsWindowGeometryAsync(new WindowGeometry(800, 600, 0, 0, false));

        keys.Should().ContainSingle().Which.Should().Be("reports_window_geometry");
    }

    [Fact]
    public async Task SetReportsWindowGeometryAsync_throws_on_null_value()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        Func<Task> act = async () => await repo.SetReportsWindowGeometryAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---- CrystalDiagramWindowGeometry --------------------------------------------

    [Fact]
    public async Task GetCrystalDiagramWindowGeometryAsync_returns_null_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var geom = await repo.GetCrystalDiagramWindowGeometryAsync();

        geom.Should().BeNull();
    }

    [Fact]
    public async Task SetCrystalDiagramWindowGeometryAsync_then_GetCrystalDiagramWindowGeometryAsync_round_trips()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var original = new WindowGeometry(
            Width: 900, Height: 650, Left: 150, Top: 75, IsMaximized: false);

        await repo.SetCrystalDiagramWindowGeometryAsync(original);
        var roundTrip = await repo.GetCrystalDiagramWindowGeometryAsync();

        roundTrip.Should().Be(original);
    }

    [Fact]
    public async Task GetCrystalDiagramWindowGeometryAsync_returns_null_and_logs_warning_when_json_malformed()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "crystal_diagram_window_geometry", "{not-json");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        var geom = await repo.GetCrystalDiagramWindowGeometryAsync();

        geom.Should().BeNull();
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Exception is System.Text.Json.JsonException);
    }

    [Fact]
    public async Task SetCrystalDiagramWindowGeometryAsync_raises_SettingChanged_with_key_crystal_diagram_window_geometry()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetCrystalDiagramWindowGeometryAsync(new WindowGeometry(800, 600, 0, 0, false));

        keys.Should().ContainSingle().Which.Should().Be("crystal_diagram_window_geometry");
    }

    [Fact]
    public async Task SetCrystalDiagramWindowGeometryAsync_throws_on_null_value()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        Func<Task> act = async () => await repo.SetCrystalDiagramWindowGeometryAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---- LifetimeTotalsWindowGeometry (plan 51) ----------------------------------

    [Fact]
    public async Task GetLifetimeTotalsWindowGeometryAsync_returns_null_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var geom = await repo.GetLifetimeTotalsWindowGeometryAsync();

        geom.Should().BeNull();
    }

    [Fact]
    public async Task SetLifetimeTotalsWindowGeometryAsync_then_GetLifetimeTotalsWindowGeometryAsync_round_trips()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var original = new WindowGeometry(
            Width: 700, Height: 640, Left: 120, Top: 60, IsMaximized: false);

        await repo.SetLifetimeTotalsWindowGeometryAsync(original);
        var roundTrip = await repo.GetLifetimeTotalsWindowGeometryAsync();

        roundTrip.Should().Be(original);
    }

    [Fact]
    public async Task GetLifetimeTotalsWindowGeometryAsync_returns_null_and_logs_warning_when_json_malformed()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "lifetime_totals_window_geometry", "{not-json");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        var geom = await repo.GetLifetimeTotalsWindowGeometryAsync();

        geom.Should().BeNull();
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Exception is System.Text.Json.JsonException);
    }

    [Fact]
    public async Task SetLifetimeTotalsWindowGeometryAsync_raises_SettingChanged_with_key_lifetime_totals_window_geometry()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetLifetimeTotalsWindowGeometryAsync(new WindowGeometry(800, 600, 0, 0, false));

        keys.Should().ContainSingle().Which.Should().Be("lifetime_totals_window_geometry");
    }

    [Fact]
    public async Task SetLifetimeTotalsWindowGeometryAsync_throws_on_null_value()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        Func<Task> act = async () => await repo.SetLifetimeTotalsWindowGeometryAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---- AboutWindowGeometry (plan 52) --------------------------------------------

    [Fact]
    public async Task GetAboutWindowGeometryAsync_returns_null_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var geom = await repo.GetAboutWindowGeometryAsync();

        geom.Should().BeNull();
    }

    [Fact]
    public async Task SetAboutWindowGeometryAsync_then_GetAboutWindowGeometryAsync_round_trips()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var original = new WindowGeometry(
            Width: 460, Height: 560, Left: 90, Top: 45, IsMaximized: false);

        await repo.SetAboutWindowGeometryAsync(original);
        var roundTrip = await repo.GetAboutWindowGeometryAsync();

        roundTrip.Should().Be(original);
    }

    [Fact]
    public async Task GetAboutWindowGeometryAsync_returns_null_and_logs_warning_when_json_malformed()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "about_window_geometry", "{not-json");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        var geom = await repo.GetAboutWindowGeometryAsync();

        geom.Should().BeNull();
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Exception is System.Text.Json.JsonException);
    }

    [Fact]
    public async Task SetAboutWindowGeometryAsync_raises_SettingChanged_with_key_about_window_geometry()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetAboutWindowGeometryAsync(new WindowGeometry(800, 600, 0, 0, false));

        keys.Should().ContainSingle().Which.Should().Be("about_window_geometry");
    }

    [Fact]
    public async Task SetAboutWindowGeometryAsync_throws_on_null_value()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        Func<Task> act = async () => await repo.SetAboutWindowGeometryAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---- CrystalDiagramSelectedAnomaly (plan 35) ----------------------------------

    [Fact]
    public async Task GetSelectedCrystalAnomalyAsync_returns_null_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var value = await repo.GetSelectedCrystalAnomalyAsync();

        value.Should().BeNull();
    }

    [Fact]
    public async Task SetSelectedCrystalAnomalyAsync_then_GetSelectedCrystalAnomalyAsync_round_trips()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetSelectedCrystalAnomalyAsync("griemeer");
        var roundTrip = await repo.GetSelectedCrystalAnomalyAsync();

        roundTrip.Should().Be("griemeer");
    }

    [Fact]
    public async Task SetSelectedCrystalAnomalyAsync_overwrites_existing_value()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetSelectedCrystalAnomalyAsync("griemeer");
        await repo.SetSelectedCrystalAnomalyAsync("veldspar");
        var roundTrip = await repo.GetSelectedCrystalAnomalyAsync();

        roundTrip.Should().Be("veldspar");
    }

    [Fact]
    public async Task SetSelectedCrystalAnomalyAsync_raises_SettingChanged_with_key_crystal_diagram_selected_anomaly()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetSelectedCrystalAnomalyAsync("griemeer");

        keys.Should().ContainSingle().Which.Should().Be("crystal_diagram_selected_anomaly");
    }

    [Fact]
    public async Task SetSelectedCrystalAnomalyAsync_throws_on_null_value()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        Func<Task> act = async () => await repo.SetSelectedCrystalAnomalyAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---- ReportsSidebarWidth (plan 25) --------------------------------------------

    [Fact]
    public async Task GetReportsSidebarWidthAsync_returns_null_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var value = await repo.GetReportsSidebarWidthAsync();

        value.Should().BeNull();
    }

    [Fact]
    public async Task SetReportsSidebarWidthAsync_then_GetReportsSidebarWidthAsync_round_trips()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetReportsSidebarWidthAsync(345.5);
        var roundTrip = await repo.GetReportsSidebarWidthAsync();

        roundTrip.Should().Be(345.5);
    }

    [Fact]
    public async Task GetReportsSidebarWidthAsync_returns_null_and_logs_warning_when_value_not_a_number()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "reports_sidebar_width", "wide");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        var value = await repo.GetReportsSidebarWidthAsync();

        value.Should().BeNull();
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("wide", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetReportsSidebarWidthAsync_raises_SettingChanged_with_key_reports_sidebar_width()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetReportsSidebarWidthAsync(280);

        keys.Should().ContainSingle().Which.Should().Be("reports_sidebar_width");
    }

    // ---- Reports column widths (plan 47) ------------------------------------------

    [Fact]
    public async Task GetReportsColumnWidthsAsync_returns_null_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        (await repo.GetReportsColumnWidthsAsync("ore")).Should().BeNull();
    }

    [Fact]
    public async Task ReportsColumnWidths_round_trip_is_independent_per_grid_key()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetReportsColumnWidthsAsync("ore", new Dictionary<int, double> { [0] = 120.5, [2] = 80 });

        var widths = await repo.GetReportsColumnWidthsAsync("ore");
        widths.Should().NotBeNull();
        widths!.Should().HaveCount(2);
        widths[0].Should().Be(120.5);
        widths[2].Should().Be(80);
        // a different grid key is unaffected.
        (await repo.GetReportsColumnWidthsAsync("refined")).Should().BeNull();
    }

    [Fact]
    public async Task SetReportsColumnWidthsAsync_overwrites_prior_value()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetReportsColumnWidthsAsync("ore", new Dictionary<int, double> { [0] = 100 });
        await repo.SetReportsColumnWidthsAsync("ore", new Dictionary<int, double> { [1] = 200 });

        var widths = await repo.GetReportsColumnWidthsAsync("ore");
        widths.Should().NotBeNull();
        widths!.Should().ContainKey(1).WhoseValue.Should().Be(200);
        widths.Should().NotContainKey(0);
    }

    [Fact]
    public async Task GetReportsColumnWidthsAsync_returns_null_and_logs_warning_when_json_malformed()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "reports_column_widths.ore", "{not-json");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        var widths = await repo.GetReportsColumnWidthsAsync("ore");

        widths.Should().BeNull();
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Exception is System.Text.Json.JsonException);
    }

    [Fact]
    public async Task SetReportsColumnWidthsAsync_raises_SettingChanged_with_scoped_key()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetReportsColumnWidthsAsync("compare.rates", new Dictionary<int, double> { [0] = 50 });

        keys.Should().ContainSingle().Which.Should().Be("reports_column_widths.compare.rates");
    }

    [Fact]
    public async Task ReportsColumnWidths_throws_on_null_args()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        Func<Task> nullGetKey = async () => await repo.GetReportsColumnWidthsAsync(null!);
        Func<Task> nullSetKey = async () => await repo.SetReportsColumnWidthsAsync(null!, new Dictionary<int, double>());
        Func<Task> nullSetVal = async () => await repo.SetReportsColumnWidthsAsync("ore", null!);

        await nullGetKey.Should().ThrowAsync<ArgumentNullException>();
        await nullSetKey.Should().ThrowAsync<ArgumentNullException>();
        await nullSetVal.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---- Reports text size (plan 47) ----------------------------------------------

    [Fact]
    public async Task GetReportsTextSizeAsync_returns_null_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        (await repo.GetReportsTextSizeAsync("ore")).Should().BeNull();
    }

    [Fact]
    public async Task ReportsTextSize_round_trip_persists_emphasis_by_name()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetReportsTextSizeAsync("ore", new Dictionary<string, StatEmphasis>
        {
            ["Title"] = StatEmphasis.Hero,
            ["Table"] = StatEmphasis.Header2,
        });

        // Stored as the enum NAME, not its ordinal, so reordering the enum is safe.
        var raw = await db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'reports_text_size.ore'");
        raw.Should().NotBeNull();
        raw!.Should().Contain("Hero").And.Contain("Header2");

        var map = await repo.GetReportsTextSizeAsync("ore");
        map.Should().NotBeNull();
        map!["Title"].Should().Be(StatEmphasis.Hero);
        map["Table"].Should().Be(StatEmphasis.Header2);
    }

    [Fact]
    public async Task SetReportsTextSizeAsync_overwrites_prior_value()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetReportsTextSizeAsync("ore", new Dictionary<string, StatEmphasis> { ["Title"] = StatEmphasis.Hero });
        await repo.SetReportsTextSizeAsync("ore", new Dictionary<string, StatEmphasis> { ["Table"] = StatEmphasis.Header1 });

        var map = await repo.GetReportsTextSizeAsync("ore");
        map.Should().NotBeNull();
        map!.Should().ContainKey("Table").WhoseValue.Should().Be(StatEmphasis.Header1);
        map.Should().NotContainKey("Title");
    }

    [Fact]
    public async Task GetReportsTextSizeAsync_returns_null_and_logs_warning_when_json_malformed()
    {
        await using var db = await NewSchemaAsync();
        await UpsertSettingAsync(db, "reports_text_size.ore", "{not-json");
        var log = new CapturingLogger<SqliteSettingsRepository>();
        var repo = NewRepo(db, log);

        var map = await repo.GetReportsTextSizeAsync("ore");

        map.Should().BeNull();
        log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Exception is System.Text.Json.JsonException);
    }

    [Fact]
    public async Task SetReportsTextSizeAsync_raises_SettingChanged_with_scoped_key()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetReportsTextSizeAsync("lifetime", new Dictionary<string, StatEmphasis> { ["Title"] = StatEmphasis.Hero });

        keys.Should().ContainSingle().Which.Should().Be("reports_text_size.lifetime");
    }

    [Fact]
    public async Task ReportsTextSize_throws_on_null_args()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        Func<Task> nullGetKey = async () => await repo.GetReportsTextSizeAsync(null!);
        Func<Task> nullSetKey = async () => await repo.SetReportsTextSizeAsync(null!, new Dictionary<string, StatEmphasis>());
        Func<Task> nullSetVal = async () => await repo.SetReportsTextSizeAsync("ore", null!);

        await nullGetKey.Should().ThrowAsync<ArgumentNullException>();
        await nullSetKey.Should().ThrowAsync<ArgumentNullException>();
        await nullSetVal.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---- ShowPilotRefinedOutput (plan 49 / issue #1) ------------------------------

    [Fact]
    public async Task GetShowPilotRefinedOutputAsync_returns_true_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var enabled = await repo.GetShowPilotRefinedOutputAsync();

        enabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetShowPilotRefinedOutputAsync_then_get_round_trips(bool value)
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetShowPilotRefinedOutputAsync(value);
        var roundTrip = await repo.GetShowPilotRefinedOutputAsync();

        roundTrip.Should().Be(value);
    }

    [Fact]
    public async Task SetShowPilotRefinedOutputAsync_raises_SettingChanged_with_key_show_pilot_refined_output()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetShowPilotRefinedOutputAsync(false);

        keys.Should().ContainSingle().Which.Should().Be("show_pilot_refined_output");
    }
}
