namespace EveDashboard.MarketData.Pricing;

using EveDashboard.Core.Pricing;
using EveDashboard.Core.Settings;

/// <summary>One line of a haul: an item type + how many units are being hauled.</summary>
/// <param name="TypeId">EVE type id.</param>
/// <param name="Name">Display name (from the session or the paste).</param>
/// <param name="Quantity">Number of units.</param>
public sealed record HaulLine(long TypeId, string Name, long Quantity);

/// <summary>A haul line valued at one station.</summary>
/// <param name="TypeId">EVE type id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Quantity">Units hauled.</param>
/// <param name="UnitPrice">Per-unit price at this station for the active mode, or <c>null</c> if unpriced there.</param>
/// <param name="LineValue"><see cref="UnitPrice"/> * <see cref="Quantity"/>, or <c>null</c> if unpriced.</param>
public sealed record HaulLineValue(long TypeId, string Name, long Quantity, double? UnitPrice, double? LineValue);

/// <summary>The whole haul valued at one station.</summary>
/// <param name="StationId">EVE station id.</param>
/// <param name="StationLabel">Human label (<c>"System - Station"</c>).</param>
/// <param name="TotalValue">Sum of the priced line values (unpriced lines contribute 0).</param>
/// <param name="UnpricedCount">How many lines had no price at this station.</param>
/// <param name="Lines">Per-line detail.</param>
public sealed record StationHaulValue(
    long StationId, string StationLabel, double TotalValue, int UnpricedCount, IReadOnlyList<HaulLineValue> Lines);

/// <summary>The valuation across all requested stations, best total first.</summary>
/// <param name="PriceMode">The mode the values were computed for.</param>
/// <param name="Stations">Per-station results, ordered by <see cref="StationHaulValue.TotalValue"/> descending.</param>
public sealed record HaulValuation(PriceMode PriceMode, IReadOnlyList<StationHaulValue> Stations);

/// <summary>
/// Values a haul (a set of type-id + quantity lines) across several market
/// stations at once, so the user can see where it is worth the most. Calls the
/// <see cref="IMarketPriceClient"/> directly (respecting the user's selected
/// price source) — it does NOT go through the session-bound, throttled fetcher,
/// so an ad-hoc appraisal never writes to <c>market_data</c> or pollutes a
/// session's price history.
/// </summary>
public sealed class HaulValuationCalculator
{
    private readonly IMarketPriceClient _client;

    /// <summary>Construct the calculator over a price client (typically the plan-53 router).</summary>
    /// <param name="client">Price client used to fetch per-station prices.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="client"/> is null.</exception>
    public HaulValuationCalculator(IMarketPriceClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <summary>
    /// Value <paramref name="haul"/> at each of <paramref name="stations"/> using
    /// <paramref name="mode"/>. Duplicate type ids in the haul are collapsed
    /// (quantities summed) before pricing. A line with no order on a station
    /// contributes 0 to that station's total and is flagged unpriced.
    /// </summary>
    /// <param name="haul">The items being hauled.</param>
    /// <param name="stations">Stations to price at (id + display label).</param>
    /// <param name="mode">Buy / Sell / Split.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Per-station values, best total first.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="haul"/> or <paramref name="stations"/> is null.</exception>
    public async Task<HaulValuation> ValueAsync(
        IReadOnlyList<HaulLine> haul,
        IReadOnlyList<(long StationId, string Label)> stations,
        PriceMode mode,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(haul);
        ArgumentNullException.ThrowIfNull(stations);

        // Collapse duplicate type ids (sum quantities), keep first-seen name + order.
        var merged = new List<HaulLine>();
        var indexByType = new Dictionary<long, int>();
        foreach (var line in haul)
        {
            if (indexByType.TryGetValue(line.TypeId, out var i))
            {
                var cur = merged[i];
                merged[i] = cur with { Quantity = cur.Quantity + line.Quantity };
            }
            else
            {
                indexByType[line.TypeId] = merged.Count;
                merged.Add(line);
            }
        }

        var typeIds = merged.Select(l => l.TypeId).ToList();
        var results = new List<StationHaulValue>(stations.Count);
        foreach (var (stationId, label) in stations)
        {
            var prices = typeIds.Count == 0
                ? Array.Empty<MarketPrice>()
                : await _client.FetchPricesAsync(stationId, typeIds, ct).ConfigureAwait(false);
            var priceByType = new Dictionary<long, MarketPrice>();
            foreach (var p in prices) priceByType[p.TypeId] = p;

            var lines = new List<HaulLineValue>(merged.Count);
            double total = 0;
            var unpriced = 0;
            foreach (var line in merged)
            {
                double? unit = priceByType.TryGetValue(line.TypeId, out var mp)
                    ? PriceModeSelector.Pick(mp.IskBuy, mp.IskSell, mode)
                    : null;
                double? lineValue = unit is double u ? u * line.Quantity : (double?)null;
                if (lineValue is double lv) total += lv; else unpriced++;
                lines.Add(new HaulLineValue(line.TypeId, line.Name, line.Quantity, unit, lineValue));
            }
            results.Add(new StationHaulValue(stationId, label, total, unpriced, lines));
        }

        results.Sort((a, b) => b.TotalValue.CompareTo(a.TotalValue));
        return new HaulValuation(mode, results);
    }
}
