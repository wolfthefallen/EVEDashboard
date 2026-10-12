namespace EveDashboard.MarketData.Pricing;

/// <summary>
/// One session to price, and the window its prices should reflect.
/// </summary>
/// <param name="SessionId">The freshly built session.</param>
/// <param name="WindowStart">Inclusive window start (UTC).</param>
/// <param name="WindowStop">Inclusive window end (UTC); the instant the averaged prices are stamped with.</param>
public sealed record HistoricalPricingTarget(string SessionId, DateTimeOffset WindowStart, DateTimeOffset WindowStop);

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

    /// <summary>
    /// Price several freshly built sessions in one pass, hitting the market once
    /// for the whole batch instead of once per session.
    /// </summary>
    /// <param name="targets">The sessions to price, each with its own window. Duplicate session ids are rejected.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One result per target, in the order given.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="targets"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="targets"/> contains duplicate session ids.</exception>
    /// <remarks>
    /// <para>
    /// Each session still gets its own rows - no session ever reads or alters
    /// another's prices. What is shared is the NETWORK call: the price fetcher
    /// throttles per (session, type), so pricing a month of days one at a time
    /// would fetch every refined material once per day. Here today's prices are
    /// fetched once, through the first session that needs them, and copied to the
    /// others that still have gaps.
    /// </para>
    /// <para>
    /// A batch also must not let one of its own sessions feed another: the
    /// averaging step ignores the rows of every session in the batch, not just
    /// the one being priced, so a later day cannot average an earlier day's
    /// freshly written average.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<HistoricalPricingResult>> PriceBatchAsync(
        IReadOnlyList<HistoricalPricingTarget> targets,
        CancellationToken ct = default);
}
