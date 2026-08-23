namespace EveDashboard.Stats.Historical;

/// <summary>
/// Writes a saved session after the fact from gamelog events and/or existing
/// sessions, atomically, archiving the sources it folds in.
/// </summary>
public interface IHistoricalSessionBuilder
{
    /// <summary>
    /// Build and persist the session described by <paramref name="request"/> in one
    /// transaction: session + pilots + copied and parsed <c>log</c> rows + a
    /// synthesised <c>session_time</c> timeline; then archive the merged sources.
    /// Nothing is written if any step fails.
    /// </summary>
    /// <param name="request">The build request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Counts and the new session id.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown for a blank name, an empty roster, an inverted window, or an out-of-range idle threshold.</exception>
    /// <exception cref="InvalidOperationException">Thrown if a source to merge is missing, already archived, or still running; or if a session with the same name and start already exists.</exception>
    Task<HistoricalSessionResult> BuildAsync(HistoricalSessionRequest request, CancellationToken ct = default);
}
