namespace EveDashboard.Core.Domain;

/// <summary>
/// Discriminator for the four v1 mining-related events emitted by the EVE gamelog.
/// </summary>
/// <remarks>
/// Each kind corresponds to a distinct log-line shape recognized by the parser.
/// The kind alone says what happened (a mining tick, a crit, a residue loss,
/// a depleted source); the *product* mined is carried separately as a type ID
/// resolved against the SDE on <see cref="MiningEvent.TypeId"/>. The
/// in-space-compression line is intentionally not parsed in v1.
/// </remarks>
public enum MiningEventKind
{
    /// <summary>"You mined N units of &lt;product&gt;" — a normal mining tick.</summary>
    Tick,

    /// <summary>"You mined an additional N units of &lt;product&gt;" — a critical-hit bonus tick.</summary>
    Crit,

    /// <summary>"Additional N units depleted from asteroid as residue" — T2 crystal scrap loss.</summary>
    Residue,

    /// <summary>"The asteroid is depleted" — source exhausted. Carries no amount.</summary>
    Depleted,
}
