namespace EveDashboard.Core.Sessions.Historical;

/// <summary>
/// Answers "what role did this pilot last have" from the saved sessions, so a
/// roster discovered from the gamelogs can inherit the roles the user already
/// chose instead of defaulting everyone to <see cref="PilotRole.Miner"/>.
/// </summary>
/// <remarks>
/// Roles change what gets counted - a Booster's mining is excluded from session
/// totals until they mine volume themselves, and Cyno / Combat events are always
/// excluded - so a booster silently treated as a miner would make a historical
/// day disagree with every live session the user has ever run.
/// </remarks>
public static class PilotRoleHistory
{
    /// <summary>
    /// Fold the saved spans into each pilot's most recently used role.
    /// </summary>
    /// <param name="spans">Saved session spans with their pilots, newest or oldest first - order does not matter.</param>
    /// <returns>The role from the latest-starting session each pilot appears in. A pilot absent from every span is absent from the result, and the caller supplies the default.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="spans"/> is null.</exception>
    /// <remarks>
    /// Ties on start time are broken by session id, the same ordering
    /// <c>SqliteSessionSpanQuery</c> returns, so the answer is deterministic.
    /// Archived sessions are not in the store's span query, so a pilot whose only
    /// appearance was in a session that has since been combined away falls back to
    /// the caller's default.
    /// </remarks>
    public static IReadOnlyDictionary<long, PilotRole> Resolve(IReadOnlyList<SessionSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        var latest = new Dictionary<long, (DateTimeOffset Start, string SessionId, PilotRole Role)>();
        foreach (var span in spans)
        {
            foreach (var pilot in span.Pilots)
            {
                if (latest.TryGetValue(pilot.PilotId, out var held) &&
                    (held.Start > span.Start ||
                     (held.Start == span.Start && string.CompareOrdinal(held.SessionId, span.SessionId) >= 0)))
                {
                    continue;
                }
                latest[pilot.PilotId] = (span.Start, span.SessionId, pilot.Role);
            }
        }

        return latest.ToDictionary(kv => kv.Key, kv => kv.Value.Role);
    }
}
