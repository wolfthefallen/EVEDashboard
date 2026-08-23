namespace EveDashboard.SessionIO.Tests.Tables;

using EveDashboard.Persistence.Storage;
using EveDashboard.SessionIO.Tables;
using EveDashboard.Stats.Schema;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Verifies <see cref="SessionTableReader"/> produces the right rows and
/// exports only the <c>market_data</c> rows stamped with the session's own id.
/// </summary>
public sealed class SessionTableReaderTests : IAsyncLifetime
{
    private const string InMemory = "Data Source=:memory:";
    private SqliteDatabase _db = null!;
    private SessionTableReader _reader = null!;

    public async Task InitializeAsync()
    {
        _db = new SqliteDatabase(InMemory, NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(_db);
        _reader = new SessionTableReader(_db, NullLogger<SessionTableReader>.Instance);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ReadAsync_returns_empty_session_list_for_unknown_session_id()
    {
        var data = await _reader.ReadAsync(sessionId: "sess-missing");

        data.Session.Should().BeEmpty();
        data.SessionTimes.Should().BeEmpty();
        data.Pilots.Should().BeEmpty();
        data.SessionPilots.Should().BeEmpty();
        data.Logs.Should().BeEmpty();
        data.MarketStations.Should().BeEmpty();
        data.MarketData.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_returns_session_metadata_pilots_and_logs()
    {
        var sid = await InsertSessionAsync("Test session", notes: "first run");
        await InsertSessionTimeAsync(sid, "2026-05-07T10:00:00Z", "2026-05-07T11:00:00Z", isPause: 0);
        await InsertPilotAsync(pilotId: 90_000_001L, name: "Asgar");
        await InsertPilotAsync(pilotId: 90_000_002L, name: "Bjorn");
        await InsertSessionPilotAsync(sid, 90_000_001L);
        await InsertSessionPilotAsync(sid, 90_000_002L);
        await InsertLogAsync(sid, 90_000_001L, "2026-05-07T10:01:00Z", "Tick", typeId: 1230, units: 100, volumeM3: 10.0);
        await InsertLogAsync(sid, 90_000_002L, "2026-05-07T10:02:00Z", "Tick", typeId: 1228, units: 200, volumeM3: 30.0);

        var data = await _reader.ReadAsync(sid);

        data.Session.Should().HaveCount(1);
        data.Session[0].Name.Should().Be("Test session");
        data.Session[0].Notes.Should().Be("first run");
        data.SessionTimes.Should().HaveCount(1);
        data.SessionTimes[0].IsPause.Should().Be(0);
        data.Pilots.Should().HaveCount(2).And.Contain(p => p.PilotId == 90_000_001L && p.Name == "Asgar");
        data.SessionPilots.Should().HaveCount(2);
        data.Logs.Should().HaveCount(2);
        data.Logs[0].EventKind.Should().Be("Tick");
        data.Logs[0].TypeId.Should().Be(1230);
    }

    [Fact]
    public async Task ReadAsync_excludes_pilots_from_other_sessions()
    {
        var sidA = await InsertSessionAsync("session A");
        var sidB = await InsertSessionAsync("session B");
        await InsertPilotAsync(pilotId: 90_000_001L, name: "Asgar");
        await InsertPilotAsync(pilotId: 90_000_002L, name: "Bjorn");
        await InsertSessionPilotAsync(sidA, 90_000_001L);
        await InsertSessionPilotAsync(sidB, 90_000_002L);

        var data = await _reader.ReadAsync(sidA);

        data.Pilots.Should().ContainSingle().Which.PilotId.Should().Be(90_000_001L);
    }

    [Fact]
    public async Task ReadAsync_market_data_returns_only_rows_owned_by_the_session()
    {
        // Prices are session-bound. The export carries exactly the
        // session's own market_data rows (WHERE session_id = $sid) — rows owned
        // by another session or by the zero-sentinel are never selected.
        var sid = await InsertSessionAsync("Pricing test");
        await InsertSessionTimeAsync(sid, "2026-05-07T10:00:00Z", "2026-05-07T11:00:00Z", isPause: 0);
        await InsertMarketStationAsync(60003760L, "Jita", "IV - Moon 4", regionId: 10000002, regionName: "The Forge");

        // Two rows owned by this session — both must export.
        await InsertMarketDataAsync(typeId: 34, stationId: 60003760L, fetchedAt: "2026-05-07T10:30:00Z", iskBuy: 6.10, iskSell: 6.55, sessionId: sid);
        await InsertMarketDataAsync(typeId: 35, stationId: 60003760L, fetchedAt: "2026-05-07T09:30:00Z", iskBuy: 4.00, iskSell: 4.20, sessionId: sid);

        // Rows owned by a different session and the zero-sentinel — must NOT export.
        await InsertMarketDataAsync(typeId: 34, stationId: 60003760L, fetchedAt: "2026-05-07T12:00:00Z", iskBuy: 7.00, iskSell: 7.50, sessionId: "sess-other");
        await InsertMarketDataAsync(typeId: 36, stationId: 60003760L, fetchedAt: "2026-05-07T10:45:00Z", iskBuy: 1.00, iskSell: 1.10, sessionId: new string('0', 64));

        var data = await _reader.ReadAsync(sid);

        data.MarketData.Should().HaveCount(2, "only the two rows stamped with this session's id are in scope");
        data.MarketData.Should().Contain(md => md.TypeId == 34 && md.FetchedAt == "2026-05-07T10:30:00Z");
        data.MarketData.Should().Contain(md => md.TypeId == 35 && md.FetchedAt == "2026-05-07T09:30:00Z");
        data.MarketData.Should().OnlyContain(md => md.SessionId == sid);
        data.MarketStations.Should().ContainSingle().Which.StationId.Should().Be(60003760L);
    }

    [Fact]
    public async Task ReadAsync_market_data_excludes_unrelated_stations()
    {
        var sid = await InsertSessionAsync("Single-station session");
        await InsertSessionTimeAsync(sid, "2026-05-07T10:00:00Z", "2026-05-07T11:00:00Z", isPause: 0);
        await InsertMarketStationAsync(60003760L, "Jita", "IV - Moon 4");
        await InsertMarketStationAsync(1049588174021L, "C-J6MT", "1st Taj Mahgoon");

        // Type 34 priced at Jita only; should not pull C-J6MT into the export.
        await InsertMarketDataAsync(typeId: 34, stationId: 60003760L, fetchedAt: "2026-05-07T10:30:00Z", iskBuy: 6.00, iskSell: 6.50, sessionId: sid);

        var data = await _reader.ReadAsync(sid);

        data.MarketStations.Should().ContainSingle().Which.StationId.Should().Be(60003760L);
    }

    // ----- fixture helpers (mirror SqliteReportingQueriesTests pattern) -----

    private async Task<string> InsertSessionAsync(string name, string? notes = null)
    {
        // Content-addressed sha256 hex session id; distinct names hash distinct.
        var sid = EveDashboard.Core.Sessions.SessionId.Compute(name, "2026-05-07T10:00:00.000Z");
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($sid, $n, $notes)",
            new Dictionary<string, object?> { ["$sid"] = sid, ["$n"] = name, ["$notes"] = notes });
        return sid;
    }

    private Task InsertSessionTimeAsync(string sid, string start, string? stop, int isPause) =>
        _db.ExecuteNonQueryAsync(
            "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) " +
            "VALUES ($sid, $start, $stop, $is_pause)",
            new Dictionary<string, object?>
            {
                ["$sid"] = sid,
                ["$start"] = start,
                ["$stop"] = stop,
                ["$is_pause"] = isPause,
            });

    private Task InsertPilotAsync(long pilotId, string name) =>
        _db.ExecuteNonQueryAsync(
            "INSERT OR IGNORE INTO pilot (pilot_id, name) VALUES ($pid, $n)",
            new Dictionary<string, object?> { ["$pid"] = pilotId, ["$n"] = name });

    private Task InsertSessionPilotAsync(string sid, long pilotId) =>
        _db.ExecuteNonQueryAsync(
            "INSERT INTO session_pilot (session_id, pilot_id) VALUES ($sid, $pid)",
            new Dictionary<string, object?> { ["$sid"] = sid, ["$pid"] = pilotId });

    private Task InsertLogAsync(string sid, long pilotId, string ts, string kind, long? typeId, long? units, double? volumeM3) =>
        _db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3) " +
            "VALUES ($sid, $pid, $ts, $kind, $tid, $u, $v)",
            new Dictionary<string, object?>
            {
                ["$sid"] = sid,
                ["$pid"] = pilotId,
                ["$ts"] = ts,
                ["$kind"] = kind,
                ["$tid"] = typeId,
                ["$u"] = units,
                ["$v"] = volumeM3,
            });

    private Task InsertMarketStationAsync(long stationId, string systemName, string stationName, long? regionId = null, string? regionName = null) =>
        _db.ExecuteNonQueryAsync(
            "INSERT INTO market_stations (station_id, system_name, station_name, region_id, region_name) " +
            "VALUES ($sid, $sys, $st, $rid, $rn)",
            new Dictionary<string, object?>
            {
                ["$sid"] = stationId,
                ["$sys"] = systemName,
                ["$st"] = stationName,
                ["$rid"] = regionId,
                ["$rn"] = regionName,
            });

    private Task InsertMarketDataAsync(long typeId, long stationId, string fetchedAt, double? iskBuy, double? iskSell, string sessionId) =>
        _db.ExecuteNonQueryAsync(
            "INSERT INTO market_data (type_id, station_id, session_id, fetched_at, isk_buy, isk_sell) " +
            "VALUES ($tid, $sid, $sess, $f, $b, $s)",
            new Dictionary<string, object?>
            {
                ["$tid"]  = typeId,
                ["$sid"]  = stationId,
                ["$sess"] = sessionId,
                ["$f"]    = fetchedAt,
                ["$b"]    = iskBuy,
                ["$s"]    = iskSell,
            });
}
