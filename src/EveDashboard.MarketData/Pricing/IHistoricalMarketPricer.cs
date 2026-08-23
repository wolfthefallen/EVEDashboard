namespace EveDashboard.MarketData.Pricing;

/// <summary>
/// Gives a session built after the fact its OWN <c>market_data</c> rows, so its
/// ISK is reproducible like any live session's: averaged from the prices the app
/// captured inside the window when it has them, otherwise fetched today.
/// </summary>
public interface IHistoricalMarketPricer
{
    /// <summary>
    /// Price <paramref name="sessionId"/> for the window. Never modifies or
    /// removes another session's rows - it only INSERTs rows stamped with this
    /// session id.
    /// </summary>
    /// <param name="sessionId">The freshly built session.</param>
    /// <param name="windowStart">Inclusive window start (UTC).</param>
    /// <param name="windowStop">Inclusive window end (UTC).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Where the prices came from and how many materials each path covered.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="sessionId"/> is null.</exception>
    Task<HistoricalPricingResult> PriceAsync(string sessionId, DateTimeOffset windowStart, DateTimeOffset windowStop, CancellationToken ct = default);
}
