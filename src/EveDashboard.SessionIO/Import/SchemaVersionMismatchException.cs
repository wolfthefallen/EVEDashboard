namespace EveDashboard.SessionIO.Import;

/// <summary>
/// Thrown when an import file's export-FORMAT version (its
/// <c>_manifest.schema_version</c> wire field) is newer than this app's
/// supported export format (<c>SessionExportFormat.CurrentVersion</c>). The
/// export-format version is decoupled from the DB <c>schema_version</c>, so an
/// additive schema migration never blocks import of an older backup — only a
/// genuinely newer export shape is refused.
/// </summary>
public sealed class SchemaVersionMismatchException : Exception
{
    /// <summary>Export-format version recorded in the import file's manifest.</summary>
    public int FileSchemaVersion { get; }

    /// <summary>The app's supported export-format version (<c>SessionExportFormat.CurrentVersion</c>).</summary>
    public int CurrentSchemaVersion { get; }

    /// <summary>Construct with the mismatched versions surfaced in the message.</summary>
    /// <param name="fileSchemaVersion">Export-format version recorded in the manifest.</param>
    /// <param name="currentSchemaVersion">The app's supported export-format version.</param>
    public SchemaVersionMismatchException(int fileSchemaVersion, int currentSchemaVersion)
        : base($"schema_version mismatch (file={fileSchemaVersion}, current={currentSchemaVersion})")
    {
        FileSchemaVersion    = fileSchemaVersion;
        CurrentSchemaVersion = currentSchemaVersion;
    }
}
