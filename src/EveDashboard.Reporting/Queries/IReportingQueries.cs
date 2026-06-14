namespace EveDashboard.Reporting.Queries;

/// <summary>
/// Read-only queries materializing the Reports-window data shapes from the
/// session SQLite database. Plan 25's reporting layer entry point.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are expected to be safe for concurrent calls (the SQL
/// substrate uses WAL mode; multiple readers concurrent with the writer).
/// The repository itself holds no mutable state.
/// </para>
/// <para>
/// Sidebar consumers call <see cref="GetAllSessionSummariesAsync"/> once on
/// Reports-window open. Breakdown consumers call
/// <see cref="GetPilotsForSessionAsync"/> + <see cref="GetOreBreakdownAsync"/>
/// each time the user picks a session.
/// </para>
/// </remarks>
public interface IReportingQueries
{
    /// <summary>
    /// Enumerate every saved session with metadata for the Reports sidebar
    /// (name, start time, active duration, pilot count, total ISK) AND the
    /// all-time <see cref="LifetimeSummary"/> rollup across those same sessions
    /// (plan 46), both built in one pass. Sidebar rows are ordered by descending
    /// start time (newest first) — plan 38, since sha256 session ids are not
    /// chronological.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="SessionsReport"/> carrying one row per session in
    ///     the session SQLite <c>session</c> table plus the lifetime aggregate
    ///     (<see cref="LifetimeSummary.Empty"/> when there are no sessions).</returns>
    /// <remarks>
    /// Per-session <c>TotalIskMined</c> (and the lifetime totals) are computed by
    /// reusing <c>ISessionStatsRepository.GetSessionStatsAsync</c> for each
    /// session at query time. The lifetime rollup is summed from the very same
    /// per-session snapshots — no second pass, no cross-session pricing. Lazy /
    /// on-demand for plan 25; a pre-computed cache is plan 26+ work if real
    /// session counts make this perceptibly slow.
    /// </remarks>
    Task<SessionsReport> GetAllSessionSummariesAsync(CancellationToken ct = default);

    /// <summary>
    /// Enumerate the pilots associated with a saved session, with display
    /// names. Required because <c>CharacterStats</c> carries pilot id only —
    /// names live in the <c>pilot</c> table. Returned ordered by name (case-
    /// insensitive ASCII), matching the live <c>PerPilotWidgetCoordinator</c>'s
    /// pilot iteration.
    /// </summary>
    /// <param name="sessionId">Session id to query against.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One row per pilot in <c>session_pilot</c> for the given session id; empty when the session has no pilots or the session id is unknown.</returns>
    Task<IReadOnlyList<PilotSummaryRow>> GetPilotsForSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Enumerate the per-ore-type breakdown for a saved session, optionally
    /// scoped to one pilot. Returns rows ordered by m^3 descending. Used by
    /// the Reports per-ore widget at both session-level and per-pilot-level.
    /// </summary>
    /// <param name="sessionId">Session id to query against.</param>
    /// <param name="pilotId">When non-null, filter to that pilot's events only; when null, aggregate across all pilots in the session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One row per source <c>type_id</c> mined in the scoped slice; empty when no matching events.</returns>
    /// <remarks>
    /// ISK per type is sourced from <c>IIskCalculator.ComputeAsync(...)</c>'s
    /// per-input-type breakdown (plan 25 algorithm refactor — see
    /// <c>IskCalculationResult.IskByInputType</c>).
    /// </remarks>
    Task<IReadOnlyList<OreBreakdownRow>> GetOreBreakdownAsync(string sessionId, long? pilotId, CancellationToken ct = default);

    /// <summary>
    /// Enumerate the per-refined-material breakdown for a saved session,
    /// optionally scoped to one pilot (plan 43). Returns rows ordered by
    /// quantity descending, showing ALL refined materials (decision 1.3 = A —
    /// reporting ignores the live auto-show / hide). Used by the Reports
    /// "Refined output" widget at session- and per-pilot-level.
    /// </summary>
    /// <param name="sessionId">Session id to query against.</param>
    /// <param name="pilotId">When non-null, filter to that pilot's events only; when null, aggregate across all pilots in the session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One row per refined material produced in the scoped slice; empty when no matching events.</returns>
    /// <remarks>
    /// Quantities are sourced from <c>IIskCalculator.ComputeAsync(...)</c>'s
    /// <c>RefinedQuantitiesByMaterial</c> (yield-applied) and name-resolved via
    /// <c>ISdeLookup.TryGetRefinedMaterialName</c>.
    /// </remarks>
    Task<IReadOnlyList<RefineMatBreakdownRow>> GetRefinedMatBreakdownAsync(string sessionId, long? pilotId, CancellationToken ct = default);

    /// <summary>
    /// Materialise a session's <c>session_time</c> rows into a closed-form
    /// <see cref="SessionTimeline"/>: session start (earliest active interval),
    /// total active duration, and the ordered list of completed pause windows.
    /// Drives the active-vs-paused duration computation on the Excel export's
    /// Summary tab (plan 29 <c>ExcelSessionExporter</c>).
    /// </summary>
    /// <param name="sessionId">Session id to query against.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A populated <see cref="SessionTimeline"/>; <see cref="SessionTimeline.SessionStart"/> = <see cref="DateTimeOffset.MinValue"/> when the session has no active interval rows (degenerate case).</returns>
    /// <remarks>
    /// Open pauses (<c>stop_time IS NULL</c> on an <c>is_pause = 1</c> row) are
    /// dropped from <see cref="SessionTimeline.PauseWindows"/> — saved sessions
    /// have all intervals closed because Stop closes any open one.
    /// </remarks>
    Task<SessionTimeline> GetSessionTimelineAsync(string sessionId, CancellationToken ct = default);
}
