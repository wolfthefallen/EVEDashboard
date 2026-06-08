namespace EveDashboard.Stats.Tests.Schema;

using EveDashboard.Core.Sessions;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Schema;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Tests for <see cref="LegacySessionIdMigration"/> (plan 38): detection,
/// sha256 id rewrite across the FK tables, and best-effort <c>market_data</c>
/// association by parsed-timestamp containment (with the zero-sentinel for
/// ambiguous / unowned rows).
/// </summary>
public class LegacySessionIdMigrationTests
{
    private static SqliteDatabase OpenInMemory() =>
        new("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);

    /// <summary>The pre-plan-38 schema: integer session_id PKs, market_data with NO session_id.</summary>
    private const string LegacyDdl = """
        CREATE TABLE session (session_id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, notes TEXT);
        CREATE TABLE session_time (session_time_id INTEGER PRIMARY KEY AUTOINCREMENT, session_id INTEGER NOT NULL REFERENCES session(session_id), start_time TEXT NOT NULL, stop_time TEXT, is_pause INTEGER NOT NULL CHECK (is_pause IN (0,1)));
        CREATE TABLE pilot (pilot_id INTEGER PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE session_pilot (session_id INTEGER NOT NULL REFERENCES session(session_id), pilot_id INTEGER NOT NULL REFERENCES pilot(pilot_id), role TEXT NOT NULL DEFAULT 'Miner' CHECK (role IN ('Miner','Booster','Cyno','Combat')), PRIMARY KEY (session_id, pilot_id));
        CREATE TABLE log (log_id INTEGER PRIMARY KEY AUTOINCREMENT, session_id INTEGER NOT NULL REFERENCES session(session_id), pilot_id INTEGER NOT NULL REFERENCES pilot(pilot_id), eve_timestamp TEXT NOT NULL, event_kind TEXT NOT NULL CHECK (event_kind IN ('Tick','Crit','Residue','Depleted')), type_id INTEGER, units INTEGER, volume_m3 REAL, role TEXT NOT NULL DEFAULT 'Miner' CHECK (role IN ('Miner','Booster','Cyno','Combat')));
        CREATE TABLE setting (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE market_stations (station_id INTEGER PRIMARY KEY, system_name TEXT NOT NULL, station_name TEXT NOT NULL, region_id INTEGER, region_name TEXT);
        CREATE TABLE market_data (marketdata_id INTEGER PRIMARY KEY AUTOINCREMENT, type_id INTEGER NOT NULL, station_id INTEGER NOT NULL REFERENCES market_stations(station_id), fetched_at TEXT NOT NULL, isk_buy REAL, isk_sell REAL);
        INSERT INTO market_stations (station_id, system_name, station_name) VALUES (60003760, 'Jita', 'IV - Moon 4');
        """;

    private static async Task SeedLegacyAsync(SqliteDatabase db) =>
        await db.ExecuteScriptAsync(LegacyDdl);

    private static Task InsertSessionAsync(SqliteDatabase db, long id, string name) =>
        db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($id, $n, NULL)",
            new Dictionary<string, object?> { ["$id"] = id, ["$n"] = name });

    private static Task InsertTimeAsync(SqliteDatabase db, long sessionId, string start, string? stop, int isPause = 0) =>
        db.ExecuteNonQueryAsync(
            "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) VALUES ($s, $start, $stop, $p)",
            new Dictionary<string, object?> { ["$s"] = sessionId, ["$start"] = start, ["$stop"] = (object?)stop ?? DBNull.Value, ["$p"] = isPause });

    private static Task InsertMarketAsync(SqliteDatabase db, long typeId, string fetchedAt) =>
        db.ExecuteNonQueryAsync(
            "INSERT INTO market_data (type_id, station_id, fetched_at, isk_buy, isk_sell) VALUES ($t, 60003760, $f, 5.0, 6.0)",
            new Dictionary<string, object?> { ["$t"] = typeId, ["$f"] = fetchedAt });

    private static Task<string?> ScalarStringAsync(SqliteDatabase db, string sql) =>
        db.QueryScalarAsync<string>(sql);

    [Fact]
    public async Task IsLegacyAsync_true_for_legacy_db_and_false_after_migration()
    {
        await using var db = OpenInMemory();
        await SeedLegacyAsync(db);
        await InsertSessionAsync(db, 1, "KDG-TA");
        await InsertTimeAsync(db, 1, "2026-05-05T22:10:35.203Z", "2026-05-06T01:35:58.925Z");

        (await LegacySessionIdMigration.IsLegacyAsync(db)).Should().BeTrue();

        await LegacySessionIdMigration.MigrateAsync(db);

        (await LegacySessionIdMigration.IsLegacyAsync(db)).Should().BeFalse("the session_id column is now TEXT");
    }

    [Fact]
    public async Task IsLegacyAsync_false_for_fresh_db()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);
        (await LegacySessionIdMigration.IsLegacyAsync(db)).Should().BeFalse();
    }

    [Fact]
    public async Task MigrateAsync_rewrites_session_id_to_sha256_across_fk_tables()
    {
        await using var db = OpenInMemory();
        await SeedLegacyAsync(db);
        const string name = "KDG-TA";
        const string start = "2026-05-05T22:10:35.203Z";
        await InsertSessionAsync(db, 1, name);
        await InsertTimeAsync(db, 1, start, "2026-05-06T01:35:58.925Z");
        await db.ExecuteNonQueryAsync("INSERT INTO pilot (pilot_id, name) VALUES (90000001, 'Pilot One')");
        await db.ExecuteNonQueryAsync("INSERT INTO session_pilot (session_id, pilot_id, role) VALUES (1, 90000001, 'Miner')");
        await db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3, role) " +
            "VALUES (1, 90000001, '2026-05-05T22:11:00.000Z', 'Tick', 34, 100, 10.0, 'Miner')");

        await LegacySessionIdMigration.MigrateAsync(db);

        var expected = SessionId.Compute(name, start);
        (await ScalarStringAsync(db, "SELECT session_id FROM session")).Should().Be(expected);
        (await ScalarStringAsync(db, "SELECT session_id FROM session_time")).Should().Be(expected);
        (await ScalarStringAsync(db, "SELECT session_id FROM session_pilot")).Should().Be(expected);
        (await ScalarStringAsync(db, "SELECT session_id FROM log")).Should().Be(expected);
    }

    [Fact]
    public async Task MigrateAsync_associates_market_data_inside_exactly_one_window()
    {
        await using var db = OpenInMemory();
        await SeedLegacyAsync(db);
        const string name = "S1";
        const string start = "2026-05-05T22:00:00.000Z";
        await InsertSessionAsync(db, 1, name);
        await InsertTimeAsync(db, 1, start, "2026-05-05T23:00:00.000Z");
        await InsertMarketAsync(db, 34, "2026-05-05T22:30:00.000Z");   // inside the window

        await LegacySessionIdMigration.MigrateAsync(db);

        (await ScalarStringAsync(db, "SELECT session_id FROM market_data"))
            .Should().Be(SessionId.Compute(name, start));
    }

    [Fact]
    public async Task MigrateAsync_zeroes_market_data_in_a_between_sessions_gap()
    {
        await using var db = OpenInMemory();
        await SeedLegacyAsync(db);
        await InsertSessionAsync(db, 1, "S1");
        await InsertTimeAsync(db, 1, "2026-05-05T22:00:00.000Z", "2026-05-05T23:00:00.000Z");
        await InsertMarketAsync(db, 34, "2026-05-06T05:00:00.000Z");   // long after the session ended

        await LegacySessionIdMigration.MigrateAsync(db);

        (await ScalarStringAsync(db, "SELECT session_id FROM market_data"))
            .Should().Be(SessionId.Unassociated);
    }

    [Fact]
    public async Task MigrateAsync_zeroes_market_data_inside_two_overlapping_windows()
    {
        await using var db = OpenInMemory();
        await SeedLegacyAsync(db);
        await InsertSessionAsync(db, 1, "A");
        await InsertTimeAsync(db, 1, "2026-05-05T22:00:00.000Z", "2026-05-05T23:00:00.000Z");
        await InsertSessionAsync(db, 2, "B");   // distinct name => distinct hash, overlapping time
        await InsertTimeAsync(db, 2, "2026-05-05T22:30:00.000Z", "2026-05-05T23:30:00.000Z");
        await InsertMarketAsync(db, 34, "2026-05-05T22:45:00.000Z");   // inside BOTH windows

        await LegacySessionIdMigration.MigrateAsync(db);

        (await ScalarStringAsync(db, "SELECT session_id FROM market_data"))
            .Should().Be(SessionId.Unassociated);
    }

    [Fact]
    public async Task MigrateAsync_associates_across_timestamp_format_drift()
    {
        await using var db = OpenInMemory();
        await SeedLegacyAsync(db);
        const string name = "S1";
        const string start = "2026-05-05T22:00:00.000Z";   // 'Z' form, ms precision
        await InsertSessionAsync(db, 1, name);
        await InsertTimeAsync(db, 1, start, "2026-05-05T23:00:00.000Z");
        // market fetched_at in the offset form with 7 fractional digits (as the
        // real GoonMetrics fetcher writes it) — would mis-sort lexically.
        await InsertMarketAsync(db, 34, "2026-05-05T22:30:00.0000000+00:00");

        await LegacySessionIdMigration.MigrateAsync(db);

        (await ScalarStringAsync(db, "SELECT session_id FROM market_data"))
            .Should().Be(SessionId.Compute(name, start), "parsed-timestamp comparison ignores Z-vs-offset drift");
    }

    [Fact]
    public async Task MigrateAsync_throws_on_hash_collision_between_two_sessions()
    {
        await using var db = OpenInMemory();
        await SeedLegacyAsync(db);
        // Same name AND same first start_time => identical sha256 => refuse to merge.
        await InsertSessionAsync(db, 1, "Dup");
        await InsertTimeAsync(db, 1, "2026-05-05T22:00:00.000Z", "2026-05-05T23:00:00.000Z");
        await InsertSessionAsync(db, 2, "Dup");
        await InsertTimeAsync(db, 2, "2026-05-05T22:00:00.000Z", "2026-05-05T23:00:00.000Z");

        Func<Task> act = async () => await LegacySessionIdMigration.MigrateAsync(db);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
