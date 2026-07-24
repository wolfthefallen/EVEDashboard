namespace EveDashboard.Core.Settings;

/// <summary>
/// Which market-price service the ISK pipeline fetches from. Persisted to
/// <c>setting.price_source</c> as the lowercase string <c>"goonmetrics"</c> or
/// <c>"esi"</c>. Default is <see cref="GoonMetrics"/> so existing users see no
/// change on upgrade.
/// </summary>
public enum PriceSource
{
    /// <summary>The third-party GoonMetrics aggregator (default; the original v1 source).</summary>
    GoonMetrics,

    /// <summary>Official CCP data via the public ESI market-orders endpoint (no login).</summary>
    Esi,
}
