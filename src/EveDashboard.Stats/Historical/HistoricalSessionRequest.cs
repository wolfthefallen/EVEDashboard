namespace EveDashboard.Stats.Historical;

using EveDashboard.Core.Domain;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Sessions.Historical;

/// <summary>
/// Everything the historical session builder needs to write one new session.
/// </summary>
/// <param name="Name">Session name (already template-expanded by the caller). Must not be blank.</param>
/// <param name="Notes">Optional free-form notes.</param>
/// <param name="WindowStart">Inclusive start of the window the user asked for (UTC).</param>
/// <param name="WindowStop">Inclusive end of the window the user asked for (UTC).</param>
/// <param name="Roster">The FINAL fleet roster (after any union with partially-matching sources): pilots, names, roles.</param>
/// <param name="ParsedEvents">Gamelog events read for the window (already window-filtered). Empty for a pure combine.</param>
/// <param name="SessionIdsToMerge">Saved sessions to fold in; their rows are copied and they are archived.</param>
/// <param name="Exclusions">Per-pilot windows claimed by sessions the user declined to combine.</param>
/// <param name="IdleMinutes">The auto-pause idle threshold used to synthesise pauses in untracked gaps (1..60).</param>
public sealed record HistoricalSessionRequest(
    string Name,
    string? Notes,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowStop,
    IReadOnlyList<FleetMember> Roster,
    IReadOnlyList<MiningEvent> ParsedEvents,
    IReadOnlyList<string> SessionIdsToMerge,
    IReadOnlyList<PilotExclusion> Exclusions,
    int IdleMinutes);
