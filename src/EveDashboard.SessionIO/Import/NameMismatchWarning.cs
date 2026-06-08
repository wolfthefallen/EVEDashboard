namespace EveDashboard.SessionIO.Import;

/// <summary>
/// Discriminator for which entity surfaced a name mismatch on import.
/// </summary>
public enum NameMismatchKind
{
    /// <summary>The mismatch was on a <c>pilot.name</c>.</summary>
    Pilot,

    /// <summary>The mismatch was on a <c>market_stations.station_name</c> (or composite name).</summary>
    Station,
}

/// <summary>
/// One name mismatch surfaced during import: an existing row was kept (per the
/// plan-29 alignment 2.1 / 2.2 = B "skip + Warning" policy), but the imported
/// row carried a different display name and the user is informed.
/// </summary>
/// <param name="Kind">Which table the mismatch is on.</param>
/// <param name="Id">EVE-side stable id of the row (<c>pilot_id</c> or <c>station_id</c>).</param>
/// <param name="ExistingName">Name found in the destination DB (kept).</param>
/// <param name="ImportedName">Name carried by the import file (discarded).</param>
public sealed record NameMismatchWarning(
    NameMismatchKind Kind,
    long             Id,
    string           ExistingName,
    string           ImportedName);
