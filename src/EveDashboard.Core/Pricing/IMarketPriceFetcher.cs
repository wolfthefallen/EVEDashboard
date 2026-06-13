namespace EveDashboard.Core.Pricing;

using EveDashboard.Core.Settings;

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
    /// when the active session transitions to <see cref="EveDashboard.Core.Sessions.SessionRunState.Running"/>
    /// (a fresh Start, a Resume, or restore-on-launch), and on a mid-session
    /// pricing-station change. This fetch is UNCONDITIONAL — it re-pulls every
    /// refined-material type id regardless of any already-captured price, so the
    /// session captures its own prices from t0 and re-prices to a newly-selected
    /// hub. It also re-stamps the live path's attempt throttle for the whole set.
    /// </remarks>
    Task FetchForSessionStartAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Live-session gaps-only fetch: for each requested type id, fetch + INSERT a
    /// price ONLY when this session has no usable price for it under
    /// <paramref name="priceMode"/>, and only when no fetch was attempted for that
    /// (session, type id) within the last 15 minutes.
    /// </summary>
    /// <param name="typeIds">Type ids the caller needs prices for.</param>
    /// <param name="stationId">Station to price against (which hub to query; not a read filter).</param>
    /// <param name="sessionId">The active session's sha256 id; inserted rows are stamped with it and the per-(session, type) attempt throttle is scoped to it.</param>
    /// <param name="priceMode">Active price mode. Coverage is mode-aware: Buy needs a usable buy price, Sell a usable sell price, Split needs either (a Split gap requires both sides 0/null).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Called from <see cref="IIskCalculator"/> (LIVE sessions only). After this
    /// returns, the caller re-reads <c>market_data</c>. A material that already has a
    /// usable price for the mode is never re-fetched; an uncovered material (no
    /// price, or 0/null for the mode) is fetched at most once per 15 minutes per
    /// session, tracked by an in-memory attempt map (no sentinel rows are written),
    /// so a genuinely-priceless type id never spams HTTP.
    /// </remarks>
    Task EnsurePricesAsync(IReadOnlyList<long> typeIds, long stationId, string sessionId, PriceMode priceMode, CancellationToken ct = default);
}
