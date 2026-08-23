namespace EveDashboard.SessionIO.Import;

/// <summary>
/// Per-table count of rows inserted by an import.
/// </summary>
/// <param name="Pilots">Pilot rows successfully INSERTed (newly added — does NOT include skipped existing rows).</param>
/// <param name="MarketStations">Station rows successfully INSERTed.</param>
/// <param name="SessionTimes">Session_time rows inserted (always equal to the source count for the imported session).</param>
/// <param name="SessionPilots">Session_pilot rows inserted.</param>
/// <param name="Logs">Log rows inserted.</param>
/// <param name="MarketData">Market_data rows inserted.</param>
public sealed record ImportRowCounts(
    int Pilots,
    int MarketStations,
    int SessionTimes,
    int SessionPilots,
    int Logs,
    int MarketData);

/// <summary>
/// Returned from <c>SessionTableWriter.WriteAsync</c> on a successful import.
/// </summary>
/// <param name="NewSessionId">The content-addressed <c>session.session_id</c> (sha256 hex) of the imported session — taken from the file when already a sha256, else recomputed from name + first start_time.</param>
/// <param name="SessionName">Name of the imported session (echoed for the UI status line).</param>
/// <param name="Warnings">Pilot / station name mismatches surfaced by the collision policy. Empty when no clashes.</param>
/// <param name="RowCounts">Per-table insert counts (informational; useful for log summaries). All zero when <paramref name="AlreadyPresent"/> is true.</param>
/// <param name="AlreadyPresent">True when a session with this <see cref="NewSessionId"/> already existed, so the import was a skip-with-warning no-op (nothing written).</param>
public sealed record ImportResult(
    string                              NewSessionId,
    string                              SessionName,
    IReadOnlyList<NameMismatchWarning>  Warnings,
    ImportRowCounts                     RowCounts,
    bool                                AlreadyPresent = false);
