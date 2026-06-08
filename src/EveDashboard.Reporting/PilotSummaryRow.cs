namespace EveDashboard.Reporting;

/// <summary>
/// One pilot in a saved session, with display name and role. Used by the
/// Reports breakdown content to know which pilots belong to a session — the
/// <see cref="EveDashboard.Stats.Queries.CharacterStats"/> snapshot carries pilot
/// id only (no name). Mirrors <c>PerPilotWidgetCoordinator.EnumerateSessionPilotsSql</c>'s
/// shape for the live coordinator.
/// </summary>
/// <param name="PilotId">EVE character id; matches <c>pilot.pilot_id</c> and the gamelog filename.</param>
/// <param name="PilotName">EVE character display name.</param>
/// <param name="Role">Plan 35: the pilot's current fleet role string (canonical: Miner / Booster / Cyno / Combat). Consumed by Excel Summary block.</param>
public sealed record PilotSummaryRow(
    long   PilotId,
    string PilotName,
    string Role);
