namespace EveDashboard.Core.Domain;

/// <summary>
/// One mining-related event parsed from a single EVE gamelog line.
/// </summary>
/// <param name="Kind">Which kind of event this is — see <see cref="MiningEventKind"/>.</param>
/// <param name="EveTimestamp">
/// Event time as recorded in the log line's <c>[YYYY.MM.DD HH:MM:SS]</c> prefix.
/// EVE log time is UTC; this <see cref="DateTimeOffset"/> is constructed with
/// <see cref="TimeSpan.Zero"/> offset.
/// </param>
/// <param name="CharacterId">The character whose gamelog this line came from.</param>
/// <param name="ProductName">Display name as written in the log line (e.g. <c>"Veldspar"</c>). Always populated by the parser.</param>
/// <param name="TypeId">
/// EVE type ID of the product, or <c>null</c> if the SDE lookup did not recognize
/// <paramref name="ProductName"/>. Note that <c>0</c> is a valid EVE type ID
/// (the <c>#System</c> placeholder) and must not be treated as a sentinel —
/// always use the nullability of the field as the "is resolved?" signal.
/// </param>
/// <param name="Amount">
/// Raw count from the log line (units, not m³). For <see cref="MiningEventKind.Tick"/>
/// this is units mined; for <see cref="MiningEventKind.Crit"/> the bonus units;
/// for <see cref="MiningEventKind.Residue"/> the units depleted as scrap; for
/// <see cref="MiningEventKind.Depleted"/> always <c>0</c>.
/// </param>
public sealed record MiningEvent(
    MiningEventKind Kind,
    DateTimeOffset EveTimestamp,
    long CharacterId,
    string ProductName,
    long? TypeId,
    long Amount);
