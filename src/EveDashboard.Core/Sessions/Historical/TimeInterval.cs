namespace EveDashboard.Core.Sessions.Historical;

/// <summary>A closed time interval (UTC).</summary>
/// <param name="Start">Inclusive start.</param>
/// <param name="Stop">Inclusive end; never earlier than <paramref name="Start"/>.</param>
public sealed record TimeInterval(DateTimeOffset Start, DateTimeOffset Stop);
