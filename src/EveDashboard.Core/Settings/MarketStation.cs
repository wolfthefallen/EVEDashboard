namespace EveDashboard.Core.Settings;

/// <summary>
/// One row from the <c>market_stations</c> table — a station the app may
/// fetch GoonMetrics prices from.
/// </summary>
/// <param name="StationId">EVE station id (PK; e.g. <c>60003760</c> for Jita IV-Moon-4).</param>
/// <param name="SystemName">Solar system display name (e.g. <c>"Jita"</c>).</param>
/// <param name="StationName">EVE station display name without the system prefix
///     (e.g. <c>"IV - Moon 4 - Caldari Navy Assembly Plant"</c>).</param>
/// <param name="RegionId">Region id, if known.</param>
/// <param name="RegionName">Region display name, if known (e.g. <c>"The Forge"</c>).</param>
public sealed record MarketStation(
    long StationId,
    string SystemName,
    string StationName,
    long? RegionId,
    string? RegionName);
