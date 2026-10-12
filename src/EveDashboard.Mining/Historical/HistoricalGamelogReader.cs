namespace EveDashboard.Mining.Historical;

using System.Globalization;
using System.Text;
using EveDashboard.Core.Domain;
using EveDashboard.Mining.Gamelogs;
using EveDashboard.Mining.Parsing;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="IHistoricalGamelogReader"/>: enumerates the gamelog folder
/// (top level plus every subfolder, so the user's <c>old\</c> archive counts),
/// picks the files that can overlap the window, and streams their lines through
/// the shared <see cref="IGamelogLineParser"/>. The same enumeration backs
/// <see cref="InventoryAsync"/>, which surveys the folder without parsing.
/// </summary>
/// <remarks>
/// <para>
/// File selection reuses <see cref="GamelogDiscoveryService.FilenameRegex"/>
/// (<c>YYYYMMDD_HHMMSS_&lt;characterID&gt;.txt</c>). The 14-digit filename
/// timestamp is EVE (UTC) time - it matches the in-file <c>Session Started:</c>
/// header - so it is safe to compare against the UTC window. Two pruning rules
/// keep the scan bounded: a file that STARTED after the window stop cannot
/// contain window events, and a file is skipped when a newer file for the same
/// pilot started before the window start (EVE opens a fresh gamelog per client
/// session, so the older file stopped receiving lines before the newer began).
/// Everything else is read in full; the per-line timestamp decides inclusion.
/// </para>
/// <para>
/// A filename present both at top level and under a subfolder is read once, the
/// top-level copy winning. Per-file <see cref="IOException"/> /
/// <see cref="UnauthorizedAccessException"/> are logged at Warning and the file is
/// skipped, mirroring discovery; the caller still gets every other file's events.
/// </para>
/// </remarks>
public sealed class HistoricalGamelogReader : IHistoricalGamelogReader
{
    private const string FilenameTimestampFormat = "yyyyMMddHHmmss";

    private readonly string _directory;
    private readonly IGamelogLineParser _parser;
    private readonly ILogger<HistoricalGamelogReader> _log;

    /// <summary>
    /// Construct a reader bound to a specific Gamelogs directory.
    /// </summary>
    /// <param name="gamelogDirectory">Absolute path to the EVE Gamelogs directory.</param>
    /// <param name="parser">The shared gamelog line parser (same instance the live watcher uses).</param>
    /// <param name="log">Logger for diagnostic output.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public HistoricalGamelogReader(string gamelogDirectory, IGamelogLineParser parser, ILogger<HistoricalGamelogReader> log)
    {
        _directory = gamelogDirectory ?? throw new ArgumentNullException(nameof(gamelogDirectory));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public async Task<HistoricalReadResult> ReadAsync(
        IReadOnlyCollection<long> pilotIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowStop,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pilotIds);
        if (windowStop < windowStart)
        {
            throw new ArgumentException("windowStop must not precede windowStart.", nameof(windowStop));
        }

        var wanted = new HashSet<long>(pilotIds);
        var candidates = SelectCandidateFiles(wanted, windowStart, windowStop);

        var events = new List<MiningEvent>();
        var filesRead = new List<string>();
        var pilotsWithEvents = new HashSet<long>();
        foreach (var file in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var before = events.Count;
                await ReadFileAsync(file.Path, file.CharacterId, windowStart, windowStop, events, ct).ConfigureAwait(false);
                filesRead.Add(file.Path);
                if (events.Count > before) pilotsWithEvents.Add(file.CharacterId);
            }
            catch (IOException ex)
            {
                _log.LogWarning(ex, "Failed to read gamelog {Path} for pilot {PilotId}; skipping it.", file.Path, file.CharacterId);
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.LogWarning(ex, "Access denied reading gamelog {Path} for pilot {PilotId}; skipping it.", file.Path, file.CharacterId);
            }
        }

        // Stable sort: a file's lines are already chronological, so ties keep
        // their on-disk order.
        var ordered = events
            .Select((ev, index) => (ev, index))
            .OrderBy(t => t.ev.EveTimestamp)
            .ThenBy(t => t.index)
            .Select(t => t.ev)
            .ToList();

        var filesByPilot = candidates.Select(c => c.CharacterId).ToHashSet();
        var pilotsWithoutLogs = wanted.Where(p => !filesByPilot.Contains(p)).OrderBy(p => p).ToList();

        _log.LogInformation(
            "Historical gamelog read: {Events} events from {Files} file(s) for {Pilots} pilot(s) in {Start:O}..{Stop:O}; {Missing} pilot(s) had no log.",
            ordered.Count, filesRead.Count, wanted.Count, windowStart, windowStop, pilotsWithoutLogs.Count);

        return new HistoricalReadResult(ordered, filesRead, pilotsWithoutLogs);
    }

    /// <inheritdoc />
    public async Task<GamelogInventory> InventoryAsync(CancellationToken ct = default)
    {
        var candidates = EnumerateCandidates(wanted: null);

        var pilots = new List<GamelogInventoryEntry>();
        foreach (var group in candidates.GroupBy(c => c.CharacterId).OrderBy(g => g.Key))
        {
            ct.ThrowIfCancellationRequested();

            var files = group.OrderBy(c => c.FileStart).ToList();
            var newest = files[^1];

            // Only the newest log's header is read: it carries the name the
            // character answers to now, and one header read per pilot keeps this
            // cheap enough for a dialog's Loaded handler.
            string? name = null;
            try
            {
                name = await GamelogFiles.TryReadListenerAsync(newest.Path, ct).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                _log.LogWarning(ex, "Failed to read the header of gamelog {Path}; listing pilot {PilotId} by id.", newest.Path, group.Key);
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.LogWarning(ex, "Access denied reading the header of gamelog {Path}; listing pilot {PilotId} by id.", newest.Path, group.Key);
            }

            if (name is null)
            {
                _log.LogWarning(
                    "Gamelog {Path} has no readable Listener: line; listing pilot {PilotId} by id so their logs stay selectable.",
                    newest.Path, group.Key);
            }

            var lastWrite = files.Max(f => GamelogFiles.LastWriteUtcOrMin(f.Path));
            pilots.Add(new GamelogInventoryEntry(
                group.Key,
                name ?? group.Key.ToString(CultureInfo.InvariantCulture),
                files[0].FileStart,
                new DateTimeOffset(lastWrite, TimeSpan.Zero),
                files.Count));
        }

        var inventory = new GamelogInventory(
            pilots,
            pilots.Count == 0 ? null : pilots.Min(p => p.FirstLogStart),
            pilots.Count == 0 ? null : pilots.Max(p => p.LastActivity));

        _log.LogInformation(
            "Gamelog inventory: {Pilots} pilot(s) across {Files} file(s), logs from {Earliest:O} to {Latest:O}.",
            inventory.Pilots.Count, candidates.Count, inventory.EarliestLogStart, inventory.LatestActivity);

        return inventory;
    }

    /// <summary>
    /// Keep the files for the wanted pilots that can overlap the window, ordered
    /// by pilot then start time.
    /// </summary>
    private List<CandidateFile> SelectCandidateFiles(HashSet<long> wanted, DateTimeOffset windowStart, DateTimeOffset windowStop)
    {
        var selected = new List<CandidateFile>();
        foreach (var group in EnumerateCandidates(wanted).GroupBy(c => c.CharacterId))
        {
            var files = group.OrderBy(c => c.FileStart).ToList();
            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                if (file.FileStart > windowStop) continue; // started after the window ended
                var next = i + 1 < files.Count ? files[i + 1] : null;
                if (next is not null && next.FileStart <= windowStart) continue; // superseded before the window began
                selected.Add(file);
            }
        }

        return selected.OrderBy(c => c.CharacterId).ThenBy(c => c.FileStart).ToList();
    }

    /// <summary>
    /// Enumerate every gamelog under the folder (all depths) and de-duplicate by
    /// filename, the top-level copy winning over any subfolder copy.
    /// </summary>
    /// <param name="wanted">Pilot ids to keep, or <c>null</c> to keep every pilot found.</param>
    /// <returns>One candidate per distinct filename, in no particular order.</returns>
    private List<CandidateFile> EnumerateCandidates(HashSet<long>? wanted)
    {
        if (!Directory.Exists(_directory))
        {
            _log.LogWarning("Gamelog directory does not exist: {Directory}", _directory);
            return new List<CandidateFile>();
        }

        // filename -> best path (top-level beats any subfolder copy).
        var byName = new Dictionary<string, CandidateFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(_directory, "*.txt", SearchOption.AllDirectories))
        {
            var filename = Path.GetFileName(path);
            var match = GamelogDiscoveryService.FilenameRegex.Match(filename);
            if (!match.Success) continue;
            if (!long.TryParse(match.Groups[3].Value, out var charId))
            {
                _log.LogWarning("Skipping gamelog file with an unparseable character id in its name: {Filename}", filename);
                continue;
            }
            if (wanted is not null && !wanted.Contains(charId)) continue;
            if (!DateTimeOffset.TryParseExact(
                    match.Groups[1].Value + match.Groups[2].Value,
                    FilenameTimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var fileStart))
            {
                _log.LogWarning("Skipping gamelog file with an unparseable timestamp in its name: {Filename}", filename);
                continue;
            }

            var isTopLevel = string.Equals(
                Path.GetDirectoryName(path)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                _directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
            var candidate = new CandidateFile(path, charId, fileStart, isTopLevel);
            if (!byName.TryGetValue(filename, out var existing) || (candidate.IsTopLevel && !existing.IsTopLevel))
            {
                byName[filename] = candidate;
            }
        }

        return byName.Values.ToList();
    }

    /// <summary>
    /// Stream one gamelog through the parser, appending the events inside the window.
    /// Same line treatment as the live watcher: UTF-8 (BOM auto-detected), <c>\r</c>
    /// trimmed, empty lines skipped; the parser decides what is a mining line.
    /// </summary>
    private async Task ReadFileAsync(
        string path, long charId, DateTimeOffset windowStart, DateTimeOffset windowStop, List<MiningEvent> sink, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } rawLine)
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0) continue;
            var ev = _parser.Parse(line, charId);
            if (ev is null) continue;
            if (ev.EveTimestamp < windowStart || ev.EveTimestamp > windowStop) continue;
            sink.Add(ev);
        }
    }

    private sealed record CandidateFile(string Path, long CharacterId, DateTimeOffset FileStart, bool IsTopLevel);
}
