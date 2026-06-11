namespace EveDashboard.Core.Sessions;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Content-addressed fleet identity. A fleet's id is the lowercase-hex SHA-256
/// of its name plus its members (each member's pilot id and role), so two
/// fleets with the same name, pilots, and roles always resolve to the same id.
/// </summary>
/// <remarks>
/// <para>
/// Recipe (THE single source of truth — the repository's create path and its
/// duplicate check MUST both go through <see cref="Compute"/> so their ids
/// agree): start with <c>name</c>, then for each member <b>sorted by pilot id
/// ascending</b> append <c>(char)0x1F + pilotId + ":" + canonicalRole</c>, then
/// take <c>lowercase_hex( SHA256( UTF8(...) ) )</c>.
/// </para>
/// <para>
/// Sorting members by id makes the id independent of selection order; including
/// each member's canonical role makes a role-only difference a distinct fleet
/// (decision 1.2 = C). The delimiter is the ASCII Unit Separator (<c>0x1F</c>),
/// mirroring <see cref="SessionId"/>, so a name ending in digits cannot blur
/// into the first pilot id.
/// </para>
/// <para>
/// Unlike <see cref="SessionId"/> there is no import/migration recompute
/// requirement, so the recipe only needs internal consistency — but it is kept
/// as the single call site regardless.
/// </para>
/// </remarks>
public static class FleetId
{
    /// <summary>ASCII Unit Separator (<c>0x1F</c>) placed before each member segment.</summary>
    private const char Delimiter = (char)0x1F;

    /// <summary>
    /// Compute the content-addressed fleet id for the given name and members.
    /// </summary>
    /// <param name="name">The fleet's display name (already trimmed by the caller — see decision 3.4 = A).</param>
    /// <param name="members">The fleet's members as <c>(PilotId, Role)</c> pairs; order does not matter (sorted internally).</param>
    /// <returns>A 64-character lowercase hex SHA-256 string.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="name"/> or <paramref name="members"/> is null.</exception>
    public static string Compute(string name, IReadOnlyList<(long PilotId, PilotRole Role)> members)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(members);

        var sb = new StringBuilder(name);
        foreach (var (pilotId, role) in members.OrderBy(m => m.PilotId))
        {
            sb.Append(Delimiter);
            sb.Append(pilotId.ToString(CultureInfo.InvariantCulture));
            sb.Append(':');
            sb.Append(PilotRoleExtensions.ToCanonicalString(role));
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
