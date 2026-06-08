namespace EveDashboard.Core.Sessions;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Content-addressed session identity. A session's id is the lowercase-hex
/// SHA-256 of its name and start time, so the same logical session always
/// resolves to the same id whether it is created live, rebuilt by the startup
/// migration, or re-imported from a file.
/// </summary>
/// <remarks>
/// <para>
/// Recipe (THE single source of truth — live creation, the startup migration,
/// and import MUST all go through <see cref="Compute"/> so their ids agree):
/// <c>lowercase_hex( SHA256( UTF8( name + (char)0x1F + startIso ) ) )</c>.
/// </para>
/// <para>
/// The delimiter is the ASCII Unit Separator (<c>0x1F</c>) so a name ending in
/// digits cannot blur into the timestamp — <c>("ab", "c")</c> and
/// <c>("a", "bc")</c> hash differently.
/// </para>
/// <para>
/// <c>startIso</c> is the session's first <c>session_time.start_time</c> string
/// EXACTLY as persisted (the value the live path wrote via its
/// <c>ToSqliteIso</c> helper — ISO-8601 UTC, millisecond precision). The hash is
/// over the raw stored string, so every consumer must pass the verbatim
/// persisted value.
/// </para>
/// </remarks>
public static class SessionId
{
    /// <summary>ASCII Unit Separator (<c>0x1F</c>) placed between name and start time.</summary>
    private const char Delimiter = (char)0x1F;

    /// <summary>
    /// Placeholder id for a <c>market_data</c> row the startup migration could
    /// not associate with any session (no session active at fetch time, or an
    /// ambiguous overlap). 64 <c>'0'</c> characters — the same width as a real
    /// id, but never equal to any <see cref="Compute"/> output, so such rows
    /// stay in the DB yet feed no session's ISK.
    /// </summary>
    public static readonly string Unassociated = new('0', 64);

    /// <summary>
    /// Compute the content-addressed session id for the given name and start time.
    /// </summary>
    /// <param name="name">The session's stored <c>session.name</c> (already template-expanded).</param>
    /// <param name="startIso">The session's first <c>session_time.start_time</c> string, verbatim as persisted (ISO-8601 UTC).</param>
    /// <returns>A 64-character lowercase hex SHA-256 string.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="name"/> or <paramref name="startIso"/> is null.</exception>
    public static string Compute(string name, string startIso)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(startIso);
        var bytes = Encoding.UTF8.GetBytes(name + Delimiter + startIso);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>
    /// True if <paramref name="value"/> has the shape of a <see cref="Compute"/>
    /// output: non-null, exactly 64 characters, every character a lowercase hex
    /// digit. Used to distinguish a sha256 id from a legacy integer id on import,
    /// and (structurally) to tell whether a database has already been migrated.
    /// </summary>
    /// <param name="value">Candidate id.</param>
    /// <returns><c>true</c> if it looks like a 64-char lowercase-hex hash; otherwise <c>false</c>.</returns>
    public static bool LooksLikeSha256(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }
        foreach (var c in value)
        {
            var isHex = c is (>= '0' and <= '9') or (>= 'a' and <= 'f');
            if (!isHex)
            {
                return false;
            }
        }
        return true;
    }
}
