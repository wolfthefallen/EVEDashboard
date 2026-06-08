namespace EveDashboard.SessionIO.Json;

using System.Text.Json;
using EveDashboard.SessionIO.Import;
using EveDashboard.SessionIO.Tables;
using Microsoft.Extensions.Logging;

/// <summary>
/// Reads a single-session export JSON file and inserts its contents as a new
/// session in the destination DB via <see cref="SessionTableWriter"/>.
/// </summary>
/// <remarks>
/// Validates the envelope's <c>_manifest.format</c> before running any SQL.
/// The schema-version compatibility check is delegated to the writer (single
/// source of truth) so JSON-mediated and direct-data imports refuse mismatched
/// versions identically.
/// </remarks>
public sealed class JsonSessionImporter
{
    private readonly SessionTableWriter           _writer;
    private readonly ILogger<JsonSessionImporter> _log;

    /// <summary>Construct the importer.</summary>
    /// <param name="writer">Writer that performs the atomic SQL insert.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if either argument is null.</exception>
    public JsonSessionImporter(
        SessionTableWriter           writer,
        ILogger<JsonSessionImporter> log)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(log);
        _writer = writer;
        _log = log;
    }

    /// <summary>Import a single session from <paramref name="filePath"/>.</summary>
    /// <param name="filePath">Absolute path of the source .json file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="ImportResult"/> from the underlying writer.</returns>
    /// <exception cref="PortableFileFormatException">Thrown if the file is not a recognisable session export envelope.</exception>
    /// <exception cref="SchemaVersionMismatchException">Thrown if the file's schema_version does not match the destination DB.</exception>
    public async Task<ImportResult> ImportAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        SessionExportEnvelope? envelope;
        await using (var stream = File.OpenRead(filePath))
        {
            try
            {
                envelope = await JsonSerializer
                    .DeserializeAsync<SessionExportEnvelope>(stream, JsonSerializerOptionsFactory.Default, ct)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new PortableFileFormatException(
                    $"File is not valid JSON or does not match the EveDashboardSessionExport shape: {ex.Message}");
            }
        }

        if (envelope is null)
        {
            throw new PortableFileFormatException("File deserialised to null — not a recognisable export.");
        }
        if (envelope.Manifest is null ||
            !string.Equals(envelope.Manifest.Format, "EveDashboardSessionExport", StringComparison.Ordinal))
        {
            var found = envelope.Manifest?.Format ?? "<missing>";
            throw new PortableFileFormatException(
                $"File _manifest.format is '{found}'; expected 'EveDashboardSessionExport'.");
        }

        var data = new SessionExportData(
            Session:        envelope.Session,
            SessionTimes:   envelope.SessionTime,
            Pilots:         envelope.Pilot,
            SessionPilots:  envelope.SessionPilot,
            Logs:           envelope.Log,
            MarketStations: envelope.MarketStations,
            MarketData:     envelope.MarketData);

        _log.LogInformation(
            "Importing session '{Name}' from {Path} (file schema={SchemaVersion}, file app={AppVersion}).",
            envelope.Manifest.SessionName, filePath, envelope.Manifest.SchemaVersion, envelope.Manifest.AppVersion);

        return await _writer.WriteAsync(data, envelope.Manifest.SchemaVersion, ct).ConfigureAwait(false);
    }
}
