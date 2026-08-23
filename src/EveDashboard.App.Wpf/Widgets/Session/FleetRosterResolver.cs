namespace EveDashboard.App.Wpf.Widgets.Session;

using EveDashboard.Core.Domain;
using EveDashboard.Core.Sessions;

/// <summary>
/// Pure resolver that pairs a fleet roster against a discovered-character
/// snapshot at Start. Each roster member whose
/// pilot id is present in the snapshot is resolved to its live
/// <see cref="Character"/> + fleet role; members with no current gamelog are
/// reported as skipped. No I/O — the caller supplies the snapshot — so this is
/// unit-testable in isolation.
/// </summary>
public static class FleetRosterResolver
{
    /// <summary>
    /// Resolve <paramref name="roster"/> against <paramref name="discoveredById"/>.
    /// </summary>
    /// <param name="roster">The selected fleet's members.</param>
    /// <param name="discoveredById">Discovered characters keyed by character id (a fresh snapshot).</param>
    /// <returns>
    /// <c>Resolved</c>: the discoverable members paired with their fleet role, in
    /// roster order. <c>Skipped</c>: the pilot ids with no current gamelog.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="roster"/> or <paramref name="discoveredById"/> is null.</exception>
    public static (IReadOnlyList<(Character Char, PilotRole Role)> Resolved, IReadOnlyList<long> Skipped) Resolve(
        IReadOnlyList<FleetMember> roster,
        IReadOnlyDictionary<long, Character> discoveredById)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(discoveredById);

        var resolved = new List<(Character Char, PilotRole Role)>(roster.Count);
        var skipped = new List<long>();
        foreach (var member in roster)
        {
            if (discoveredById.TryGetValue(member.PilotId, out var character))
            {
                resolved.Add((character, member.Role));
            }
            else
            {
                skipped.Add(member.PilotId);
            }
        }
        return (resolved, skipped);
    }
}
