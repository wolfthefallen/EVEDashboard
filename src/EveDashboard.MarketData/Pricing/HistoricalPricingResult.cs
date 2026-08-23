namespace EveDashboard.MarketData.Pricing;

/// <summary>Where a historical session's prices came from.</summary>
public enum HistoricalPriceBasis
{
    /// <summary>Every priced material was averaged from market rows captured inside the window at the selected station.</summary>
    AveragedFromWindow,

    /// <summary>No usable market rows existed inside the window; every material was priced with a fresh fetch (today's prices).</summary>
    FetchedToday,

    /// <summary>Some materials were averaged from the window, the rest were filled with a fresh fetch.</summary>
    Mixed,
}

/// <summary>
/// Outcome of pricing a historical session, for the UI notice and the log.
/// </summary>
/// <param name="SessionId">The session that received its own <c>market_data</c> rows.</param>
/// <param name="StationId">The pricing station used (the user's selected station).</param>
/// <param name="Basis">Where the prices came from.</param>
/// <param name="TypesAveraged">Refined-material type ids priced by averaging rows captured inside the window.</param>
/// <param name="RowsAveraged">How many captured rows fed those averages.</param>
/// <param name="TypesFetchedNow">Type ids that had no usable window rows and were fetched at today's prices.</param>
/// <param name="TypesUnpriced">Type ids that could not be priced either way (the source returned nothing for them); they value at 0 ISK.</param>
public sealed record HistoricalPricingResult(
    string SessionId,
    long StationId,
    HistoricalPriceBasis Basis,
    int TypesAveraged,
    int RowsAveraged,
    int TypesFetchedNow,
    int TypesUnpriced);
