namespace EveDashboard.Stats.Writer;

using System.Globalization;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;

/// <summary>
/// The two conventions every writer of <c>log</c> / <c>session_time</c> rows
/// must share: the on-disk timestamp shape and the denormalised
/// <c>volume_m3</c> rule. Used by the live <see cref="SessionEventWriter"/> and
/// by the historical session builder so rows from both paths are
/// indistinguishable to every reader.
/// </summary>
internal static class MiningEventRow
{
    /// <summary>
    /// Timestamp shape persisted in <c>log.eve_timestamp</c> and
    /// <c>session_time.start_time</c> / <c>stop_time</c>: UTC, millisecond
    /// precision, trailing <c>Z</c>.
    /// </summary>
    public const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    /// <summary>Format a UTC instant the way the session store expects it.</summary>
    /// <param name="value">Instant to format (converted to UTC).</param>
    /// <returns>The persisted string form.</returns>
    public static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Denormalised <c>volume_m3</c> for a mining event: units x the SDE volume per
    /// unit when the type is known; <c>null</c> for Residue (no type) or when the
    /// SDE has no product for the id.
    /// </summary>
    /// <param name="sde">SDE lookup.</param>
    /// <param name="typeId">Ore type id, or <c>null</c>.</param>
    /// <param name="amount">Units mined / lost.</param>
    /// <returns>Volume in m3, or <c>null</c>.</returns>
    public static double? VolumeFor(ISdeLookup sde, long? typeId, long amount)
    {
        if (typeId is not { } id) return null;
        var product = sde.TryGetProduct(id);
        return product is null ? null : amount * product.VolumePerUnitM3;
    }
}
