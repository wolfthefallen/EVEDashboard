namespace EveDashboard.Core.Pricing;

/// <summary>
/// Orchestrates GoonMetrics fetches and writes returned rows to the
/// <c>market_data</c> table. Implementations subscribe to
/// <see cref="EveDashboard.Core.Sessions.IActiveSessionContext.ActiveSessionChanged"/>
/// for session-start prefetch and expose
/// <see cref="EnsurePricesAsync"/> for synchronous lazy on-demand fetches
/// from inside <see cref="IIskCalculator"/>.
/// </summary>
public interface IMarketPriceFetcher
{
    /// <summary>
    /// Fetch the prefetch type id list (the union of refined-material type ids
    /// derivable from any ore — see
    /// <see cref="EveDashboard.Core.Lookup.ISdeLookup.GetRefinedMaterialTypeIds"/>)
    /// for the configured selected station and INSERT returned rows into
    /// <c>market_data</c>.
    /// </summary>
    /// <param name="sessionId">Plan 38: the starting session's sha256 id; every inserted <c>market_data</c> row is stamped with it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Called via <see cref="EveDashboard.Core.Sessions.IActiveSessionContext.ActiveSessionChanged"/>
    /// when the active session transitions to <see cref="EveDashboard.Core.Sessions.SessionRunState.Running"/>.
    /// Plan 38: this start fetch is UNCONDITIONAL — the 60-second throttle is
    /// bypassed so the new session captures its own accurate prices from t0,
    /// even if another session fetched the same type ids seconds earlier.
    /// </remarks>
    Task FetchForSessionStartAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Ensure this session's <c>market_data</c> contains a non-throttled row for
    /// each requested type id at the given station; fetch the missing ones
    /// synchronously and stamp them with <paramref name="sessionId"/>.
    /// </summary>
    /// <param name="typeIds">Type ids the caller needs prices for.</param>
    /// <param name="stationId">Station to price against.</param>
    /// <param name="sessionId">Plan 38: the active session's sha256 id; inserted rows are stamped with it and the throttle is scoped to it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Called from <see cref="IIskCalculator"/> (LIVE sessions only) when an ISK
    /// calc finds a missing price. After this returns, the caller re-reads
    /// <c>market_data</c>. The 60-second throttle still applies, scoped to
    /// <paramref name="sessionId"/> — repeated missed lookups for an unstocked
    /// type id will not spam HTTP.
    /// </remarks>
    Task EnsurePricesAsync(IReadOnlyList<long> typeIds, long stationId, string sessionId, CancellationToken ct = default);
}
