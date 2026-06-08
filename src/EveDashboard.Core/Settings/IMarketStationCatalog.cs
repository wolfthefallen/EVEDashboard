namespace EveDashboard.Core.Settings;

/// <summary>
/// CRUD surface over the <c>market_stations</c> table — the user-configurable
/// list of stations the app may fetch GoonMetrics prices from. Plan 18's ISK
/// pipeline resolves <see cref="ISettingsRepository.GetSelectedStationIdAsync"/>
/// against this catalog.
/// </summary>
/// <remarks>
/// Direct SQL on the table; no in-memory cache. Plan 17's Settings UI binds
/// to this catalog and to <see cref="ISettingsRepository"/> for the active
/// station's id.
/// </remarks>
public interface IMarketStationCatalog
{
    /// <summary>Return all rows ordered by <c>system_name</c> ASC.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All stations in the catalog, ordered by system name then station id.</returns>
    Task<IReadOnlyList<MarketStation>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Return the row with the given station id, or <c>null</c> if missing.</summary>
    /// <param name="stationId">Station id to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching station, or <c>null</c>.</returns>
    Task<MarketStation?> GetAsync(long stationId, CancellationToken ct = default);

    /// <summary>Insert a new row. Throws if <paramref name="station"/>'s id already exists.</summary>
    /// <param name="station">Station to insert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="station"/> is null.</exception>
    /// <exception cref="System.Data.Common.DbException">Thrown on PK collision.</exception>
    Task AddAsync(MarketStation station, CancellationToken ct = default);

    /// <summary>Update an existing row by station id. No-op if no matching row exists.</summary>
    /// <param name="station">Station to update; the row identified by <see cref="MarketStation.StationId"/> is updated.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="station"/> is null.</exception>
    Task UpdateAsync(MarketStation station, CancellationToken ct = default);

    /// <summary>Delete a row by station id. No-op if no matching row exists.</summary>
    /// <param name="stationId">Station id to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RemoveAsync(long stationId, CancellationToken ct = default);

    /// <summary>
    /// Seed the two default rows on first launch (per plan 14 round-2 1.5):
    /// Jita is re-seeded on every launch via <c>INSERT OR IGNORE</c>; C-J6MT
    /// is seeded once and never again. Cross-touches the <c>setting</c> table
    /// for the <c>non_sticky_station_defaults_seeded</c> sentinel that gates
    /// the C-J6MT seed.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task EnsureSeedDefaultsAsync(CancellationToken ct = default);
}
