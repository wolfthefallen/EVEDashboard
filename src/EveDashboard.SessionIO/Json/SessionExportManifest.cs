namespace EveDashboard.SessionIO.Json;

using System.Text.Json.Serialization;

/// <summary>
/// The <c>_manifest</c> block at the top of every session export file.
/// Identifies the file as one of ours and carries enough metadata for the
/// importer to refuse a file from a different schema or app version.
/// </summary>
/// <param name="Format">Literal string <c>"EveDashboardSessionExport"</c>. Importer rejects anything else.</param>
/// <param name="SchemaVersion">Value of the <c>schema_version</c> setting in the source DB at export time. Must match the destination DB on import.</param>
/// <param name="AppVersion">Source-app build version (e.g. <c>"0.29.0"</c>). Diagnostic only — mismatch does NOT fail the import.</param>
/// <param name="ExportedAt">ISO-8601 UTC instant the export was written.</param>
/// <param name="SessionId">Source-DB session id (plan-38 sha256 hex, or a legacy integer) of the exported session. Informational — hoisted out of the row data for quick file inspection; the importer recomputes from the session row.</param>
/// <param name="SessionName">Display name of the exported session, hoisted for the same reason.</param>
public sealed record SessionExportManifest(
    [property: JsonPropertyName("format")]          string Format,
    [property: JsonPropertyName("schema_version")]  int    SchemaVersion,
    [property: JsonPropertyName("app_version")]     string AppVersion,
    [property: JsonPropertyName("exported_at")]     string ExportedAt,
    [property: JsonPropertyName("session_id"), JsonConverter(typeof(FlexibleSessionIdJsonConverter))] string SessionId,
    [property: JsonPropertyName("session_name")]    string SessionName);
