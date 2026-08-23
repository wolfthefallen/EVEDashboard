namespace EveDashboard.SessionIO.Json;

using System.Globalization;
using System.Text.Json;
using EveDashboard.SessionIO.Tables;
using Microsoft.Extensions.Logging;

/// <summary>
/// Writes one session's <see cref="SessionExportData"/> to a JSON file with a
/// <c>_manifest</c> block. Round-trippable via <see cref="JsonSessionImporter"/>.
/// </summary>
public sealed class JsonSessionExporter
{
    private readonly SessionTableReader  _reader;
    private readonly ILogger<JsonSessionExporter> _log;

    /// <summary>Construct the exporter.</summary>
    /// <param name="reader">Reader that materialises the per-table row payload.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    /// <remarks>
    /// The exporter does not read the DB <c>schema_version</c>. The manifest
    /// carries the decoupled <see cref="SessionExportFormat.CurrentVersion"/>
    /// (the export shape), so a DB-schema bump never affects export/import
    /// compatibility.
    /// </remarks>
    public JsonSessionExporter(
        SessionTableReader  reader,
        ILogger<JsonSessionExporter> log)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(log);
        _reader = reader;
        _log = log;
    }

    /// <summary>Export the session at <paramref name="sessionId"/> to <paramref name="filePath"/>.</summary>
    /// <param name="sessionId">Source session id to export.</param>
    /// <param name="filePath">Absolute path of the destination .json file. Overwritten if it exists.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown if no session exists with <paramref name="sessionId"/>.</exception>
    public async Task ExportAsync(string sessionId, string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(sessionId);

        var data = await _reader.ReadAsync(sessionId, ct).ConfigureAwait(false);
        if (data.Session.Count == 0)
        {
            throw new InvalidOperationException(
                $"Cannot export session {sessionId}: no row in 'session' table.");
        }

        var manifest = new SessionExportManifest(
            Format:        "EveDashboardSessionExport",
            SchemaVersion: SessionExportFormat.CurrentVersion,
            AppVersion:    JsonSerializerOptionsFactory.ResolveAppVersion(),
            ExportedAt:    DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            SessionId:     data.Session[0].SessionId,
            SessionName:   data.Session[0].Name);

        var envelope = new SessionExportEnvelope(
            Manifest:       manifest,
            Session:        data.Session,
            SessionTime:    data.SessionTimes,
            Pilot:          data.Pilots,
            SessionPilot:   data.SessionPilots,
            Log:            data.Logs,
            MarketStations: data.MarketStations,
            MarketData:     data.MarketData);

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, envelope, JsonSerializerOptionsFactory.Default, ct)
            .ConfigureAwait(false);

        _log.LogInformation(
            "Exported session #{SessionId} ('{Name}') to {Path}: {Logs} log + {MarketData} market_data rows.",
            sessionId, manifest.SessionName, filePath, data.Logs.Count, data.MarketData.Count);
    }
}
