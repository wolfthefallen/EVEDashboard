namespace EveDashboard.Mining.Parsing;

using EveDashboard.Core.Domain;

/// <summary>
/// Parses a single EVE gamelog line into a typed <see cref="MiningEvent"/>.
/// Pure logic; no I/O. The implementation is stateless across calls except for
/// a per-instance dedup set used to log unresolved-product warnings exactly once.
/// </summary>
/// <remarks>
/// <para>
/// Plan 10's deliverable. Constructed once and shared across every character
/// being watched (per-call <c>characterId</c>; the parser itself is per-app, not
/// per-character). The watcher (plan 12) is the only production caller; tests
/// in <c>tests/EveDashboard.Mining.Tests/Parsing/</c> exercise the contract directly.
/// </para>
/// <para>
/// Recognized v1 line shapes (verified against real EVE gamelogs):
/// <list type="bullet">
///   <item><description><c>You mined N units of &lt;product&gt;</c> -> <see cref="MiningEventKind.Tick"/></description></item>
///   <item><description><c>You mined an additional N units of &lt;product&gt;</c> -> <see cref="MiningEventKind.Crit"/></description></item>
///   <item><description><c>Additional N units depleted from asteroid as residue</c> -> <see cref="MiningEventKind.Residue"/></description></item>
/// </list>
/// <see cref="MiningEventKind.Depleted"/> is intentionally NOT emitted in v1
/// (decision 10.5 in <c>plans/10-log-line-parser.md</c>: needs a real captured
/// log line; v2 follow-on plan).
/// </para>
/// </remarks>
public interface IGamelogLineParser
{
    /// <summary>
    /// Parse <paramref name="line"/> into a <see cref="MiningEvent"/>.
    /// </summary>
    /// <param name="line">A single log line, already split from the gamelog file by the caller.</param>
    /// <param name="characterId">EVE character id of the gamelog the line came from.</param>
    /// <returns>
    /// A populated <see cref="MiningEvent"/> if the line matches one of the v1
    /// mining shapes (tick, crit, residue); <c>null</c> otherwise. The
    /// <see cref="MiningEvent.TypeId"/> field is <c>null</c> when the parser
    /// recognized the shape but <see cref="EveDashboard.Core.Lookup.ISdeLookup"/>
    /// did not recognize the captured product name; downstream consumers
    /// (plan 12's writer) decide what to do with such events.
    /// Residue events always carry <c>ProductName = ""</c> and
    /// <c>TypeId = null</c> because the EVE residue line carries no ore name.
    /// </returns>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="line"/> is null.</exception>
    MiningEvent? Parse(string line, long characterId);
}
