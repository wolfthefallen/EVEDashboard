namespace EveDashboard.Stats.Settings;

using System.Data.Common;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="IMarketStationCatalog"/> implementation backed by an
/// injected <see cref="ISqliteDatabase"/>. Direct SQL CRUD over the
/// <c>market_stations</c> table.
/// </summary>
/// <remarks>
/// <para>
/// Holds no mutable state — safe to register as a singleton in DI alongside
/// the singleton <see cref="ISqliteDatabase"/>.
/// </para>
/// <para>
/// <see cref="EnsureSeedDefaultsAsync"/> implements a hybrid seeding
/// policy: Jita is re-seeded on every launch via
/// <c>INSERT OR IGNORE</c>; C-J6MT is seeded once and never again, gated by
/// the <c>non_sticky_station_defaults_seeded</c> sentinel in the
/// <c>setting</c> table.
/// </para>
/// </remarks>
public sealed class SqliteMarketStationCatalog : IMarketStationCatalog
{
    private const long JitaStationId = 60003760L;
    private const long Cj6MtStationId = 1049588174021L;
    private const string SeededSentinelKey = "non_sticky_station_defaults_seeded";

    private readonly ISqliteDatabase _db;
    private readonly ILogger<SqliteMarketStationCatalog> _log;

    /// <summary>Construct a catalog over the given SQLite database.</summary>
    /// <param name="db">SQLite database wrapper. Typically the singleton
    ///     <c>SqliteDatabase</c> registered at app startup against the
    ///     session SQLite file.</param>
    /// <param name="log">Logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="db"/> or <paramref name="log"/> is null.
    /// </exception>
    public SqliteMarketStationCatalog(
        ISqliteDatabase db,
        ILogger<SqliteMarketStationCatalog> log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(log);
        _db = db;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketStation>> GetAllAsync(CancellationToken ct = default) =>
        await _db.QueryAsync(
            "SELECT station_id, system_name, station_name, region_id, region_name " +
            "FROM market_stations ORDER BY system_name ASC, station_id ASC",
            MapRow, null, ct);

    /// <inheritdoc />
    public async Task<MarketStation?> GetAsync(long stationId, CancellationToken ct = default)
    {
        var rows = await _db.QueryAsync(
            "SELECT station_id, system_name, station_name, region_id, region_name " +
            "FROM market_stations WHERE station_id = $sid LIMIT 1",
            MapRow,
            new Dictionary<string, object?> { ["$sid"] = stationId }, ct);
        return rows.Count == 0 ? null : rows[0];
    }

    /// <inheritdoc />
    public Task AddAsync(MarketStation station, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(station);
        return _db.ExecuteNonQueryAsync(
            "INSERT INTO market_stations (station_id, system_name, station_name, region_id, region_name) " +
            "VALUES ($sid, $sys, $name, $rid, $rname)",
            ParamsFor(station), ct);
    }

    /// <inheritdoc />
    public Task UpdateAsync(MarketStation station, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(station);
        return _db.ExecuteNonQueryAsync(
            "UPDATE market_stations SET system_name = $sys, station_name = $name, " +
            "region_id = $rid, region_name = $rname WHERE station_id = $sid",
            ParamsFor(station), ct);
    }

    /// <inheritdoc />
    public Task RemoveAsync(long stationId, CancellationToken ct = default) =>
        _db.ExecuteNonQueryAsync(
            "DELETE FROM market_stations WHERE station_id = $sid",
            new Dictionary<string, object?> { ["$sid"] = stationId }, ct);

    /// <inheritdoc />
    public async Task EnsureSeedDefaultsAsync(CancellationToken ct = default)
    {
        // The four NPC empire trade hubs - sticky defaults, re-seeded every launch
        // via INSERT OR IGNORE (station ids / regions verified against ESI). These
        // are universal "always available" hubs (unlike C-J6MT below, which is a
        // one-time convenience seed).
        await _db.ExecuteNonQueryAsync(
            "INSERT OR IGNORE INTO market_stations (station_id, system_name, station_name, region_id, region_name) " +
            "VALUES ($sid, 'Jita', 'IV - Moon 4 - Caldari Navy Assembly Plant', 10000002, 'The Forge')",
            new Dictionary<string, object?> { ["$sid"] = JitaStationId }, ct);
        await _db.ExecuteNonQueryAsync(
            "INSERT OR IGNORE INTO market_stations (station_id, system_name, station_name, region_id, region_name) " +
            "VALUES (60008494, 'Amarr', 'VIII (Oris) - Emperor Family Academy', 10000043, 'Domain')", null, ct);
        await _db.ExecuteNonQueryAsync(
            "INSERT OR IGNORE INTO market_stations (station_id, system_name, station_name, region_id, region_name) " +
            "VALUES (60011866, 'Dodixie', 'IX - Moon 20 - Federation Navy Assembly Plant', 10000032, 'Sinq Laison')", null, ct);
        await _db.ExecuteNonQueryAsync(
            "INSERT OR IGNORE INTO market_stations (station_id, system_name, station_name, region_id, region_name) " +
            "VALUES (60004588, 'Rens', 'VI - Moon 8 - Brutor Tribe Treasury', 10000030, 'Heimatar')", null, ct);

        // C-J6MT - non-sticky default; seeded once, then never again.
        var sentinel = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = $k",
            new Dictionary<string, object?> { ["$k"] = SeededSentinelKey }, ct);
        if (sentinel is null)
        {
            await _db.ExecuteNonQueryAsync(
                "INSERT OR IGNORE INTO market_stations (station_id, system_name, station_name, region_id, region_name) " +
                "VALUES ($sid, 'C-J6MT', '1st Taj Mahgoon', 10000009, 'Insmother')",
                new Dictionary<string, object?> { ["$sid"] = Cj6MtStationId }, ct);
            await _db.ExecuteNonQueryAsync(
                "INSERT OR IGNORE INTO setting (key, value) VALUES ($k, '1')",
                new Dictionary<string, object?> { ["$k"] = SeededSentinelKey }, ct);
            _log.LogDebug(
                "Seeded non-sticky market station defaults (C-J6MT) and wrote sentinel '{Key}'.",
                SeededSentinelKey);
        }
    }

    private static MarketStation MapRow(DbDataReader r) =>
        new(
            StationId: r.GetInt64(0),
            SystemName: r.GetString(1),
            StationName: r.GetString(2),
            RegionId: r.IsDBNull(3) ? null : r.GetInt64(3),
            RegionName: r.IsDBNull(4) ? null : r.GetString(4));

    private static IReadOnlyDictionary<string, object?> ParamsFor(MarketStation s) =>
        new Dictionary<string, object?>
        {
            ["$sid"] = s.StationId,
            ["$sys"] = s.SystemName,
            ["$name"] = s.StationName,
            ["$rid"] = s.RegionId,
            ["$rname"] = s.RegionName,
        };
}
