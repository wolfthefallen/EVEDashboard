namespace EveDashboard.SessionIO.Tests.Tables;

using EveDashboard.Persistence.Storage;
using EveDashboard.SessionIO.Tables;
using EveDashboard.Stats.Schema;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Verifies <see cref="SessionTableDeleter"/> removes the session and its
/// FK-cascading rows, leaves the user's "no direct FK" tables untouched, and
/// is a no-op when the session id is unknown.
/// </summary>
public sealed class SessionTableDeleterTests : IAsyncLifetime
{
    private const string InMemory = "Data Source=:memory:";
    private SqliteDatabase _db = null!;
    private SessionTableDeleter _deleter = null!;

    public async Task InitializeAsync()
    {
        _db = new SqliteDatabase(InMemory, NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(_db);
        _deleter = new SessionTableDeleter(_db, NullLogger<SessionTableDeleter>.Instance);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DeleteAsync_removes_session_and_its_fk_cascading_rows()
    {
        var sid = await SeedSessionAsync(name: "victim");
        await SeedSessionAsync(name: "survivor"); // second session — must NOT be touched.

        var deleted = await _deleter.DeleteAsync(sid);
        deleted.Should().BeGreaterThan(0);

        // Victim is gone from every direct-FK table.
        (await CountWhereSessionIdAsync("session", sid)).Should().Be(0);
        (await CountWhereSessionIdAsync("session_time", sid)).Should().Be(0);
        (await CountWhereSessionIdAsync("session_pilot", sid)).Should().Be(0);
        (await CountWhereSessionIdAsync("log", sid)).Should().Be(0);

        // Survivor is intact.
        var survivors = await _db.QueryScalarAsync<long>("SELECT COUNT(*) FROM session WHERE name = 'survivor'");
        survivors.Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_leaves_pilot_rows_alone()
    {
        var sid = await SeedSessionAsync(name: "victim");
        var pilotsBefore = await _db.QueryScalarAsync<long>("SELECT COUNT(*) FROM pilot");
        pilotsBefore.Should().BeGreaterThan(0);

        await _deleter.DeleteAsync(sid);

        var pilotsAfter = await _db.QueryScalarAsync<long>("SELECT COUNT(*) FROM pilot");
        pilotsAfter.Should().Be(pilotsBefore, "pilots are EVE-stable ids; not a direct session FK; not deleted by design");
    }

    [Fact]
    public async Task DeleteAsync_leaves_market_data_and_market_stations_alone()
    {
        var sid = await SeedSessionAsync(name: "victim");
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO market_stations (station_id, system_name, station_name) VALUES (60003760, 'Jita', 'IV - Moon 4')");
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO market_data (type_id, station_id, session_id, fetched_at, isk_buy, isk_sell) " +
            "VALUES (34, 60003760, $sid, '2026-05-07T10:30:00Z', 6.0, 6.5)",
            new Dictionary<string, object?> { ["$sid"] = sid });

        await _deleter.DeleteAsync(sid);

        var stations = await _db.QueryScalarAsync<long>("SELECT COUNT(*) FROM market_stations");
        var prices   = await _db.QueryScalarAsync<long>("SELECT COUNT(*) FROM market_data");
        stations.Should().Be(1);
        prices.Should().Be(1, "market_data is not a direct session FK and is not deleted by design");
    }

    [Fact]
    public async Task DeleteAsync_returns_zero_when_session_id_unknown()
    {
        var deleted = await _deleter.DeleteAsync(sessionId: "sess-unknown");
        deleted.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_is_idempotent_on_already_deleted_session()
    {
        var sid = await SeedSessionAsync(name: "victim");

        var first = await _deleter.DeleteAsync(sid);
        first.Should().BeGreaterThan(0);

        var second = await _deleter.DeleteAsync(sid);
        second.Should().Be(0, "re-deleting an already-deleted session is a no-op");
    }

    // ----- helpers -----

    private async Task<string> SeedSessionAsync(string name)
    {
        // Plan 38: content-addressed sha256 hex session id; distinct names hash distinct.
        var sid = EveDashboard.Core.Sessions.SessionId.Compute(name, "2026-05-07T10:00:00.000Z");
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($sid, $n, NULL)",
            new Dictionary<string, object?> { ["$sid"] = sid, ["$n"] = name });

        await _db.ExecuteNonQueryAsync(
            "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) " +
            "VALUES ($sid, '2026-05-07T10:00:00Z', '2026-05-07T11:00:00Z', 0)",
            new Dictionary<string, object?> { ["$sid"] = sid });
        await _db.ExecuteNonQueryAsync(
            "INSERT OR IGNORE INTO pilot (pilot_id, name) VALUES (90000001, 'Asgar')");
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO session_pilot (session_id, pilot_id) VALUES ($sid, 90000001)",
            new Dictionary<string, object?> { ["$sid"] = sid });
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3) " +
            "VALUES ($sid, 90000001, '2026-05-07T10:01:00Z', 'Tick', 1230, 100, 10.0)",
            new Dictionary<string, object?> { ["$sid"] = sid });
        return sid;
    }

    private Task<long> CountWhereSessionIdAsync(string table, string sessionId) =>
        _db.QueryScalarAsync<long>(
            $"SELECT COUNT(*) FROM {table} WHERE session_id = $sid",
            new Dictionary<string, object?> { ["$sid"] = sessionId });
}
