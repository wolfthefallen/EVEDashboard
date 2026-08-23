namespace EveDashboard.SessionIO.Json;

/// <summary>
/// Version of the portable session-export JSON shape.
/// This is the export <b>format</b> version, deliberately decoupled from the
/// SQLite <c>schema_version</c>: a DB-schema migration that does not change the
/// export shape must NOT bump this, so a user can always re-import an older
/// backup after upgrading.
/// </summary>
/// <remarks>
/// Historically the export manifest carried the source DB's <c>schema_version</c>
/// in a wire field also named <c>schema_version</c>; both were <c>1</c>, so every
/// existing export file is already compatible. The wire field name stays
/// <c>schema_version</c> for back-compat, but its meaning is now this constant.
/// Bump <see cref="CurrentVersion"/> ONLY when the on-disk export JSON structure
/// itself changes, and add back-compat reading for older values in the same
/// change. Import rejects a file whose version is NEWER than
/// <see cref="CurrentVersion"/>; an equal-or-older file is accepted.
/// </remarks>
public static class SessionExportFormat
{
    /// <summary>The current export-format version (unchanged since the format was introduced).</summary>
    public const int CurrentVersion = 1;
}
