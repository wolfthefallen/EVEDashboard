namespace EveDashboard.MarketData.Pricing;

using EveDashboard.Core.Pricing;
using EveDashboard.Core.Settings;

/// <summary>
/// <see cref="IMarketPriceClient"/> that delegates to either the GoonMetrics or
/// the official-ESI client, chosen per call from the persisted
/// <c>price_source</c> setting. Reading the setting on every fetch makes the
/// choice live-switchable — a change in Settings takes effect on the next fetch
/// without an app restart.
/// </summary>
/// <remarks>
/// Pure delegation — the concrete clients own the HTTP + parse and the fetcher
/// owns persistence. The default (<see cref="PriceSource.GoonMetrics"/>) is the
/// original v1 behaviour, so upgrading users see no change unless they opt in.
/// </remarks>
public sealed class SelectablePriceClient : IMarketPriceClient
{
    private readonly ISettingsRepository _settings;
    private readonly IMarketPriceClient _goonMetrics;
    private readonly IMarketPriceClient _esi;

    /// <summary>Construct the router over the two concrete price clients.</summary>
    /// <param name="settings">Settings repository read for the active <c>price_source</c>.</param>
    /// <param name="goonMetrics">The GoonMetrics-backed client (default source).</param>
    /// <param name="esi">The official-ESI-backed client.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public SelectablePriceClient(
        ISettingsRepository settings,
        IMarketPriceClient goonMetrics,
        IMarketPriceClient esi)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(goonMetrics);
        ArgumentNullException.ThrowIfNull(esi);
        _settings = settings;
        _goonMetrics = goonMetrics;
        _esi = esi;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketPrice>> FetchPricesAsync(
        long                stationId,
        IReadOnlyList<long> typeIds,
        CancellationToken   ct = default)
    {
        var source = await _settings.GetPriceSourceAsync(ct).ConfigureAwait(false);
        var client = source == PriceSource.Esi ? _esi : _goonMetrics;
        return await client.FetchPricesAsync(stationId, typeIds, ct).ConfigureAwait(false);
    }
}
