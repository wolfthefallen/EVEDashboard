namespace EveDashboard.Mining.Historical;

/// <summary>
/// One pilot's gamelog footprint on disk.
/// </summary>
/// <param name="PilotId">EVE character id, taken from the log filenames.</param>
/// <param name="Name">Character name from the newest log's <c>Listener:</c> header, or the id as text when no header could be read.</param>
/// <param name="FirstLogStart">Filename timestamp of the pilot's oldest log (UTC) - how far back their history reaches.</param>
/// <param name="LastActivity">Newest last-write time among the pilot's logs (UTC). A coarse "when were they last playing", used to bound a suggested window, not to decide what to parse.</param>
/// <param name="FileCount">How many log files were found for the pilot.</param>
public sealed record GamelogInventoryEntry(
    long PilotId,
    string Name,
    DateTimeOffset FirstLogStart,
    DateTimeOffset LastActivity,
    int FileCount);

/// <summary>
/// What the gamelog folder holds: every pilot with logs, and the overall span
/// those logs cover. Answers "what history could I build from?" without parsing
/// a single mining line.
/// </summary>
/// <param name="Pilots">One entry per pilot, ordered by <see cref="GamelogInventoryEntry.PilotId"/> so the result is deterministic.</param>
/// <param name="EarliestLogStart">Oldest log's filename timestamp, or <c>null</c> when no logs were found.</param>
/// <param name="LatestActivity">Newest log last-write time, or <c>null</c> when no logs were found.</param>
public sealed record GamelogInventory(
    IReadOnlyList<GamelogInventoryEntry> Pilots,
    DateTimeOffset? EarliestLogStart,
    DateTimeOffset? LatestActivity);
