namespace EveDashboard.Core.Sessions.Historical;

/// <summary>
/// A time range inside the historical window during which one pilot's gamelog
/// events must NOT be included, because that pilot already belongs to another
/// (declined) session for that time.
/// </summary>
/// <param name="PilotId">EVE character id the exclusion applies to.</param>
/// <param name="Start">Inclusive start (UTC), already clipped to the window.</param>
/// <param name="Stop">Inclusive end (UTC), already clipped to the window.</param>
/// <param name="SessionId">The declined session that owns this pilot's time - kept so the UI can say "because of session X".</param>
public sealed record PilotExclusion(long PilotId, DateTimeOffset Start, DateTimeOffset Stop, string SessionId)
{
    /// <summary>True when <paramref name="at"/> falls inside this exclusion (inclusive on both ends).</summary>
    /// <param name="at">Event timestamp (UTC).</param>
    /// <returns>Whether the event must be dropped.</returns>
    public bool Covers(DateTimeOffset at) => at >= Start && at <= Stop;
}
