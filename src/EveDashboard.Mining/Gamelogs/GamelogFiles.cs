using System.Text;
using System.Text.RegularExpressions;

namespace EveDashboard.Mining.Gamelogs;

/// <summary>
/// Shared per-file helpers for EVE gamelogs: reading the character name out of a
/// log's header, and the file's last-write time.
/// </summary>
/// <remarks>
/// These live apart from <see cref="GamelogDiscoveryService"/> because two
/// callers need them over different sets of files. Discovery reads only the
/// top-level folder, deliberately, so the live tailing service never follows an
/// archived log; the historical inventory reads every folder including
/// <c>old\</c>. Only the file-level mechanics are shared - which files to look
/// at stays each caller's decision.
/// </remarks>
internal static class GamelogFiles
{
    private static readonly Regex ListenerRegex = new(
        @"^\s*Listener:\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>How many lines from the top of a log we scan looking for the Listener line.</summary>
    /// <remarks>The Listener line is at line 3 in real EVE logs; 15 is defensive headroom.</remarks>
    internal const int ListenerSearchLineLimit = 15;

    /// <summary>
    /// Read the EVE character name from a gamelog's <c>Listener:</c> header line.
    /// </summary>
    /// <param name="filepath">Path to the gamelog file.</param>
    /// <param name="ct">Cancellation token, honored between lines.</param>
    /// <returns>The trimmed name, or <c>null</c> when no Listener line appears within the first <see cref="ListenerSearchLineLimit"/> lines (a file still being written, truncated by a crash, or corrupt).</returns>
    /// <exception cref="IOException">Thrown if the file cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown if the file cannot be opened.</exception>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="ct"/> is cancelled.</exception>
    internal static async Task<string?> TryReadListenerAsync(string filepath, CancellationToken ct)
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

    /// <summary>
    /// Last-write time of a gamelog in UTC, or a UTC-kinded <see cref="DateTime.MinValue"/>
    /// when the file is gone.
    /// </summary>
    /// <param name="path">Path to the gamelog file.</param>
    /// <returns>The file's UTC last-write time; <see cref="DateTime.MinValue"/> sorts a vanished file last.</returns>
    /// <remarks>
    /// The fallback is explicitly UTC-kinded so callers can lift it into a
    /// <see cref="DateTimeOffset"/>: the default <see cref="DateTime.MinValue"/>
    /// is <see cref="DateTimeKind.Unspecified"/> and would be interpreted as local
    /// time, which underflows for any negative UTC offset.
    /// </remarks>
    internal static DateTime LastWriteUtcOrMin(string path) =>
        File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
}
