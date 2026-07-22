using System.Text;
using System.Text.RegularExpressions;
using EveDashboard.Core.Domain;
using Microsoft.Extensions.Logging;

namespace EveDashboard.Mining.Gamelogs;

/// <summary>
/// Scans an EVE Gamelogs directory and returns one <see cref="Character"/> per
/// EVE character whose log files appear there, with each character's most-recent
/// log file path attached.
/// </summary>
/// <remarks>
/// One-shot discovery: each call enumerates the directory afresh. There is no
/// caching and no file watching — the live tailing service in a later plan
/// consumes the results of this service to know which files to follow.
/// "Most recent" is determined by the <c>YYYYMMDDHHMMSS</c> portion of the
/// filename, not by file modification time, because the filename timestamp
/// equals the EVE-session start time and never changes after creation.
/// </remarks>
public sealed class GamelogDiscoveryService
{
    /// <summary>
    /// Gamelog filename pattern: <c>YYYYMMDD_HHMMSS_&lt;characterID&gt;.txt</c>.
    /// Promoted to <c>internal</c> in plan 12 so <c>RealtimeLogWatcher</c> can
    /// reuse the same shape rather than duplicate the regex.
    /// </summary>
    internal static readonly Regex FilenameRegex = new(
        @"^(\d{8})_(\d{6})_(\d+)\.txt$",
        RegexOptions.Compiled);

    private static readonly Regex ListenerRegex = new(
        @"^\s*Listener:\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>How many lines from the top of a log we scan looking for the Listener line.</summary>
    /// <remarks>The Listener line is at line 3 in real EVE logs; 15 is defensive headroom.</remarks>
    private const int ListenerSearchLineLimit = 15;

    private readonly string _directory;
    private readonly ILogger<GamelogDiscoveryService> _logger;

    /// <summary>
    /// Construct a discovery service bound to a specific Gamelogs directory.
    /// </summary>
    /// <param name="gamelogDirectory">Absolute path to the EVE Gamelogs directory (e.g. <c>D:\user_files\Documents\EVE\logs\Gamelogs\</c>).</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <exception cref="ArgumentNullException">Thrown if either argument is null.</exception>
    public GamelogDiscoveryService(string gamelogDirectory, ILogger<GamelogDiscoveryService> logger)
    {
        _directory = gamelogDirectory ?? throw new ArgumentNullException(nameof(gamelogDirectory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Enumerate the Gamelogs directory and return one <see cref="Character"/>
    /// per character ID present, with the most-recent log file path attached.
    /// </summary>
    /// <param name="ct">Cancellation token. Honored at directory enumeration, per-file boundaries, and during header reads.</param>
    /// <returns>
    /// Zero or more characters. Empty list if the directory does not exist
    /// (logged at Warning), if no files match the gamelog filename pattern
    /// (silent), or if every matched file fails to yield a Listener line
    /// (each failure logged at Warning).
    /// <para>
    /// Plan 23: result is ordered by gamelog file <c>LastWriteTimeUtc</c>
    /// descending (most recently active character first); ties broken by
    /// <c>Name</c> ascending case-insensitive; characters whose log file no
    /// longer exists sort to the end via <see cref="DateTime.MinValue"/>.
    /// This is distinct from the existing per-character "latest log file pick"
    /// (which uses the filename <c>YYYYMMDDHHMMSS</c> prefix to choose WHICH
    /// file represents each character — see remarks).
    /// </para>
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="ct"/> is cancelled.</exception>
    public async Task<IReadOnlyList<Character>> DiscoverCharactersAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_directory))
        {
            _logger.LogWarning("Gamelog directory does not exist: {Directory}", _directory);
            return Array.Empty<Character>();
        }

        // Step 1: enumerate top-level *.txt files. SearchOption.TopDirectoryOnly
        // intentionally skips the `old/` subdirectory and any other nested folders.
        var files = Directory.EnumerateFiles(_directory, "*.txt", SearchOption.TopDirectoryOnly);

        // Step 2: parse filenames; for each character ID keep the running max
        // by lexicographic compare on the 14-digit YYYYMMDDHHMMSS prefix.
        var byCharId = new Dictionary<long, (string Filename, string FilePath, string Timestamp14)>();
        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();

            var filename = Path.GetFileName(path);
            var match = FilenameRegex.Match(filename);
            if (!match.Success)
            {
                _logger.LogDebug("Skipping file that does not match the gamelog filename pattern: {Filename}", filename);
                continue;
            }

            // TryParse (not Parse): the filename regex captures the char-id as
            // unbounded \d+, so a stray file with an over-long id would otherwise
            // throw OverflowException out of the whole discovery pass and drop
            // EVERY pilot. Log-and-skip the bad file instead (mirrors the sibling
            // path RealtimeLogWatcher.ResolveTrackedPilotId).
            if (!long.TryParse(match.Groups[3].Value, out var charId))
            {
                _logger.LogWarning(
                    "Skipping gamelog file with an unparseable character id in its name: {Filename}", filename);
                continue;
            }
            var timestamp14 = match.Groups[1].Value + match.Groups[2].Value;

            if (!byCharId.TryGetValue(charId, out var existing) ||
                StringComparer.Ordinal.Compare(timestamp14, existing.Timestamp14) > 0)
            {
                byCharId[charId] = (filename, path, timestamp14);
            }
        }

        // Step 3: for each (charId, latest-file), read the Listener line and emit a Character.
        var result = new List<Character>(byCharId.Count);
        foreach (var (charId, entry) in byCharId)
        {
            ct.ThrowIfCancellationRequested();

            string? listenerName;
            try
            {
                listenerName = await ReadListenerNameAsync(entry.FilePath, ct);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to read gamelog header for {Filename} (character {CharacterId}); skipping.", entry.Filename, charId);
                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Permission denied reading gamelog header for {Filename} (character {CharacterId}); skipping.", entry.Filename, charId);
                continue;
            }

            if (listenerName is null)
            {
                _logger.LogWarning(
                    "Gamelog {Filename} for character {CharacterId} has no Listener: line in the first {LineLimit} lines. " +
                    "The file may still be opening, may have been truncated by an EVE crash, or may be corrupt; " +
                    "skipping. The next discovery run may pick it up if the situation was transient.",
                    entry.Filename, charId, ListenerSearchLineLimit);
                continue;
            }

            result.Add(new Character(charId, listenerName, entry.FilePath));
        }

        // Plan 23: sort by gamelog file mtime descending, tie-break Name asc.
        // Drives the Config-mode pilot list "newest first" ordering.
        result.Sort((a, b) =>
        {
            var aTime = SafeMtime(a.LogFilePath);
            var bTime = SafeMtime(b.LogFilePath);
            var cmp = bTime.CompareTo(aTime);
            return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        });

        if (result.Count >= 2)
        {
            _logger.LogDebug(
                "Pilots ordered by last-active mtime: top={TopName}@{TopMtime:O}, bottom={BottomName}@{BottomMtime:O}, count={Count}",
                result[0].Name, SafeMtime(result[0].LogFilePath),
                result[^1].Name, SafeMtime(result[^1].LogFilePath),
                result.Count);
        }

        return result;
    }

    private static DateTime SafeMtime(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

    private async Task<string?> ReadListenerNameAsync(string filepath, CancellationToken ct)
    {
        await using var stream = new FileStream(
            filepath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        for (int i = 0; i < ListenerSearchLineLimit; i++)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            var match = ListenerRegex.Match(line);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return null;
    }
}
