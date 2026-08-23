namespace EveDashboard.Mining.Historical;

using EveDashboard.Core.Domain;

/// <summary>
/// Outcome of one <see cref="IHistoricalGamelogReader.ReadAsync"/> pass over the
/// gamelog folder: the mining events that fell inside the requested window,
/// plus enough bookkeeping for the UI to explain what was (and was not) found.
/// </summary>
/// <param name="Events">Every parsed mining event whose timestamp lies inside the window, sorted by timestamp (stable within a file).</param>
/// <param name="FilesRead">Absolute paths of the gamelog files that were actually opened and scanned, in the order they were read.</param>
/// <param name="PilotsWithoutLogs">Requested pilot ids for which no gamelog file could contribute to the window (no file at all, or every file was pruned as outside the window). The UI warns about these so a silent "zero events" is never mistaken for "nothing mined".</param>
public sealed record HistoricalReadResult(
    IReadOnlyList<MiningEvent> Events,
    IReadOnlyList<string> FilesRead,
    IReadOnlyList<long> PilotsWithoutLogs);
