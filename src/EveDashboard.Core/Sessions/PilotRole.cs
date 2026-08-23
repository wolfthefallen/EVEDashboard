namespace EveDashboard.Core.Sessions;

/// <summary>
/// Fleet role a pilot occupies within a mining session. Drives aggregate
/// filtering (Cyno / Combat events excluded from session totals; Booster gated
/// on session-wide Booster m^3) and per-pilot widget chrome (border color +
/// name tag).
/// </summary>
/// <remarks>
/// String round-trip is via <see cref="PilotRoleExtensions.Parse"/> and
/// <see cref="PilotRoleExtensions.ToCanonicalString"/>. The canonical strings
/// match the <c>CHECK</c> constraint on <c>session_pilot.role</c> and
/// <c>log.role</c> exactly (case-sensitive) so the DB and the C# enum stay in
/// lockstep.
/// </remarks>
public enum PilotRole
{
    /// <summary>Default role. Mining events are counted in session totals.</summary>
    Miner,

    /// <summary>Boost / fleet-support role. Events are excluded from totals until the pilot mines any volume themselves (the m^3 gate).</summary>
    Booster,

    /// <summary>Cynosural-field role. Events are always excluded from totals.</summary>
    Cyno,

    /// <summary>Combat / escort role. Events are always excluded from totals.</summary>
    Combat,
}

/// <summary>
/// Strict-case string round-trip helpers for <see cref="PilotRole"/>.
/// </summary>
/// <remarks>
/// The canonical strings (<c>"Miner"</c>, <c>"Booster"</c>, <c>"Cyno"</c>,
/// <c>"Combat"</c>) match the <c>CHECK (role IN (...))</c> constraint on
/// <c>session_pilot.role</c> and <c>log.role</c>. Parsing is case-sensitive
/// to ensure round-trips into the DB always re-parse identically.
/// </remarks>
public static class PilotRoleExtensions
{
    /// <summary>
    /// Parse a canonical role string into a <see cref="PilotRole"/>.
    /// </summary>
    /// <param name="value">One of <c>"Miner"</c>, <c>"Booster"</c>, <c>"Cyno"</c>, <c>"Combat"</c> (case-sensitive).</param>
    /// <returns>The matching enum value.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="value"/> is not one of the four canonical strings.</exception>
    public static PilotRole Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value switch
        {
            "Miner"   => PilotRole.Miner,
            "Booster" => PilotRole.Booster,
            "Cyno"    => PilotRole.Cyno,
            "Combat"  => PilotRole.Combat,
            _         => throw new ArgumentException(
                $"Unknown PilotRole string '{value}'. Expected one of: Miner, Booster, Cyno, Combat.",
                nameof(value)),
        };
    }

    /// <summary>
    /// Convert a <see cref="PilotRole"/> back to its canonical string.
    /// </summary>
    /// <param name="role">Enum value to format.</param>
    /// <returns>One of <c>"Miner"</c>, <c>"Booster"</c>, <c>"Cyno"</c>, <c>"Combat"</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="role"/> is not a defined enum value.</exception>
    public static string ToCanonicalString(PilotRole role) => role switch
    {
        PilotRole.Miner   => "Miner",
        PilotRole.Booster => "Booster",
        PilotRole.Cyno    => "Cyno",
        PilotRole.Combat  => "Combat",
        _                 => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown PilotRole enum value."),
    };
}
