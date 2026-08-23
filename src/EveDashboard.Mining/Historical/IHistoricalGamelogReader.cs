namespace EveDashboard.Mining.Historical;

/// <summary>
/// Reads mining events for a past date/time window straight from the gamelog
/// files on disk - the data source for building a session after the fact
/// ("I forgot to press Start") - using the same line parser as the live watcher
/// so historical and live events are identical in shape.
/// </summary>
public interface IHistoricalGamelogReader
{
    /// <summary>
    /// Scan the gamelog folder (including its <c>old\</c> subfolder) for the
    /// given pilots and return every mining event whose timestamp lies inside
    /// <paramref name="windowStart"/>..<paramref name="windowStop"/> (inclusive).
    /// </summary>
    /// <param name="pilotIds">EVE character ids whose gamelogs should be read. Ids with no matching file are reported in <see cref="HistoricalReadResult.PilotsWithoutLogs"/>, never thrown.</param>
    /// <param name="windowStart">Inclusive start of the window (UTC).</param>
    /// <param name="windowStop">Inclusive end of the window (UTC). Must not precede <paramref name="windowStart"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The events inside the window plus the files read and the pilots that had nothing to read.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="pilotIds"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="windowStop"/> is earlier than <paramref name="windowStart"/>.</exception>
    Task<HistoricalReadResult> ReadAsync(
        IReadOnlyCollection<long> pilotIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowStop,
        CancellationToken ct = default);
}
