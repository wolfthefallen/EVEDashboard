namespace EveDashboard.SessionIO.Import;

/// <summary>
/// Thrown when an import file's <c>_manifest.schema_version</c> does not match
/// the destination database's <c>schema_version</c> setting. Per the
/// pre-release rule (no migrations until v1 ships) the importer hard-fails
/// rather than attempting to transform data across schema versions.
/// </summary>
public sealed class SchemaVersionMismatchException : Exception
{
    /// <summary>Schema version recorded in the import file's manifest.</summary>
    public int FileSchemaVersion { get; }

    /// <summary>Schema version present in the destination database's <c>setting</c> table.</summary>
    public int CurrentSchemaVersion { get; }

    /// <summary>Construct with the mismatched versions surfaced in the message.</summary>
    /// <param name="fileSchemaVersion">Schema version recorded in the manifest.</param>
    /// <param name="currentSchemaVersion">Schema version of the destination database.</param>
    public SchemaVersionMismatchException(int fileSchemaVersion, int currentSchemaVersion)
        : base($"schema_version mismatch (file={fileSchemaVersion}, current={currentSchemaVersion})")
    {
        FileSchemaVersion    = fileSchemaVersion;
        CurrentSchemaVersion = currentSchemaVersion;
    }
}
