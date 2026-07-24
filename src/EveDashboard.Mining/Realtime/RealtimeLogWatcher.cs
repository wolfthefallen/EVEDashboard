namespace EveDashboard.Mining.Realtime;

using System.Text;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Realtime;
using EveDashboard.Mining.Gamelogs;
using EveDashboard.Mining.Parsing;
using Microsoft.Extensions.Logging;

/// <summary>
/// Watches the EVE Gamelogs directory for live mining-tick output, parses each
/// new line, and pushes the resulting <see cref="MiningEvent"/> records to an
/// <see cref="IMiningEventSink"/>. Plan 12's Logs-side deliverable.
/// </summary>
/// <remarks>
/// <para>
/// Uses one <see cref="FileSystemWatcher"/> over the gamelog directory; per-file
/// dispatch happens in the <c>Changed</c> / <c>Created</c> handlers based on
/// the filename. Only files for pilots passed at <see cref="StartAsync"/> are
/// followed (per decision 11.2 - unknown pilots are ignored).
/// </para>
/// <para>
/// Initial read position is end-of-file (decision 9.1) - pre-existing log
/// content is not replayed. The per-file read offset lives in memory only
/// (decision 9.2). On a new <c>Created</c> file for a known pilot (the user
/// logged that character back in mid-session), the watcher repoints the
/// pilot's tail to the new file at offset 0 (the new file's content all
/// post-dates <see cref="StartAsync"/>).
/// </para>
/// <para>
/// The pilot list passed at <see cref="StartAsync"/> is treated as immutable
/// for the lifetime of that start. To add a pilot mid-session (a
/// session-controls UI feature, v1-backlog), callers must
/// <see cref="StopAsync"/> and start again.
/// </para>
/// </remarks>
public sealed class RealtimeLogWatcher : IAsyncDisposable
{
    private readonly IGamelogLineParser _parser;
    private readonly IMiningEventSink _sink;
    private readonly ILogger<RealtimeLogWatcher> _log;

    private FileSystemWatcher? _fsw;
    private Dictionary<long, FileTailState>? _tails;
    private HashSet<long>? _activePilotIds;
    private string? _startedSessionId;
    private bool _disposed;

    /// <summary>Construct a watcher.</summary>
    /// <param name="parser">Parser used to convert each complete log line to a typed <see cref="MiningEvent"/>.</param>
    /// <param name="sink">Where parsed events go. The writer (plan 12) implements this.</param>
    /// <param name="log">Logger for FSW errors and IO failures.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public RealtimeLogWatcher(IGamelogLineParser parser, IMiningEventSink sink, ILogger<RealtimeLogWatcher> log)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(log);
        _parser = parser;
        _sink = sink;
        _log = log;
    }

    /// <summary>
    /// Begin watching. Per pilot in <paramref name="activePilots"/>, opens a
    /// tail at end-of-file. Subsequent appends are parsed and pushed to the
    /// sink. New gamelog files (the same pilot logging back into EVE) are
    /// auto-attached at offset 0; new files for pilots NOT in the list are
    /// ignored.
    /// </summary>
    /// <param name="sessionId">Session id (passed for diagnostics; the watcher itself does not tag events with it - the sink does).</param>
    /// <param name="activePilots">Pilots to watch. Each <see cref="Character"/>'s <see cref="Character.LogFilePath"/> must point to an existing file.</param>
    /// <param name="ct">Cancellation token; honoured during initial offset capture and the (synchronous) FSW startup.</param>
    /// <exception cref="InvalidOperationException">Thrown if the watcher is already started.</exception>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="activePilots"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if pilots span more than one directory or the list is empty.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the watcher has been disposed.</exception>
    public Task StartAsync(string sessionId, IReadOnlyList<Character> activePilots, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(activePilots);
        ThrowIfDisposed();
        if (_startedSessionId is not null)
        {
            throw new InvalidOperationException("RealtimeLogWatcher is already started; call StopAsync before starting again.");
        }
        if (activePilots.Count == 0)
        {
            throw new ArgumentException("activePilots must contain at least one pilot.", nameof(activePilots));
        }

        // All pilots' latest-log paths must share a parent directory (in real
        // EVE they always do; verify defensively).
        var firstDir = Path.GetDirectoryName(activePilots[0].LogFilePath)
            ?? throw new ArgumentException("Pilot LatestLogPath has no directory component.", nameof(activePilots));
        foreach (var p in activePilots)
        {
            var d = Path.GetDirectoryName(p.LogFilePath);
            if (!string.Equals(d, firstDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "All pilots must share one gamelog directory (got both '" + firstDir + "' and '" + d + "').",
                    nameof(activePilots));
            }
        }

        var tails = new Dictionary<long, FileTailState>(activePilots.Count);
        var ids = new HashSet<long>(activePilots.Count);
        foreach (var p in activePilots)
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(p.LogFilePath);
            tails[p.CharacterId] = new FileTailState
            {
                Path = p.LogFilePath,
                Offset = info.Exists ? info.Length : 0,
            };
            ids.Add(p.CharacterId);
        }

        _tails = tails;
        _activePilotIds = ids;
        _startedSessionId = sessionId;

        _fsw = new FileSystemWatcher(firstDir, "*.txt")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            IncludeSubdirectories = false,
        };
        _fsw.Changed += OnFileChanged;
        _fsw.Created += OnFileCreated;
        _fsw.Error += OnFsError;
        _fsw.EnableRaisingEvents = true;

        _log.LogDebug("RealtimeLogWatcher started: session={SessionId}, dir='{Dir}', pilots={PilotCount}",
            sessionId, firstDir, activePilots.Count);
        return Task.CompletedTask;
    }

    /// <summary>Stop the watcher. Idempotent.</summary>
    /// <param name="ct">Cancellation token (currently unused; method completes synchronously).</param>
    public Task StopAsync(CancellationToken ct = default)
    {
        if (_startedSessionId is null) return Task.CompletedTask;

        if (_fsw is not null)
        {
            _fsw.EnableRaisingEvents = false;
            _fsw.Changed -= OnFileChanged;
            _fsw.Created -= OnFileCreated;
            _fsw.Error -= OnFsError;
            _fsw.Dispose();
            _fsw = null;
        }

        _tails = null;
        _activePilotIds = null;
        _startedSessionId = null;
        _log.LogDebug("RealtimeLogWatcher stopped");
        return Task.CompletedTask;
    }

    /// <summary>Dispose - calls <see cref="StopAsync"/> if started.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RealtimeLogWatcher));
    }

    private void OnFsError(object sender, ErrorEventArgs e)
    {
        _log.LogError(e.GetException(), "FileSystemWatcher reported an error; attempting to re-arm.");
        try
        {
            if (_fsw is not null)
            {
                _fsw.EnableRaisingEvents = false;
                _fsw.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to re-arm FileSystemWatcher after error; the watcher may have stopped reporting events.");
        }
    }

    /// <summary>Resolve the character id for a filename, if it is a pilot we are watching.</summary>
    private long? ResolveTrackedPilotId(string filename)
    {
        var match = GamelogDiscoveryService.FilenameRegex.Match(filename);
        if (!match.Success) return null;
        if (!long.TryParse(match.Groups[3].Value, out var charId)) return null;
        if (_activePilotIds is null || !_activePilotIds.Contains(charId)) return null;
        return charId;
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        var charId = ResolveTrackedPilotId(e.Name ?? string.Empty);
        if (charId is null) return; // unknown pilot or non-gamelog filename - decision 11.2 = A.

        var tails = _tails;
        if (tails is null) return;

        // Pilot logged back into EVE; repoint their tail to the new file at
        // offset 0 (the new file is fresh, all content post-dates StartAsync).
        // Decision 11.1 = A.
        tails[charId.Value] = new FileTailState
        {
            Path = e.FullPath,
            Offset = 0,
        };
        _log.LogDebug("RealtimeLogWatcher attached new gamelog for pilot {CharId}: '{Path}'", charId, e.FullPath);

        // Kick a read in case bytes already arrived between Created and our
        // handler; OnFileChanged will be a no-op if the file is empty.
        _ = Task.Run(() => TailFileAsync(charId.Value));
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var charId = ResolveTrackedPilotId(e.Name ?? string.Empty);
        if (charId is null) return;

        var tails = _tails;
        if (tails is null) return;
        if (!tails.ContainsKey(charId.Value)) return; // only tail pilots we are tracking

        // TailFileAsync re-fetches the current FileTailState for this pilot (including
        // its path + offset) under the per-file gate, so a Changed event that raced a
        // Created rotation reads whichever file we are now tracking — no stale path here.
        _ = Task.Run(() => TailFileAsync(charId.Value));
    }

    /// <summary>
    /// Read newly-appended bytes for the pilot's tracked file, parse each
    /// complete line, and push <see cref="MiningEvent"/>s to the sink. Per-file
    /// gate ensures concurrent <c>Changed</c> dispatches against the same
    /// file serialise.
    /// </summary>
    private async Task TailFileAsync(long charId)
    {
        var tails = _tails;
        if (tails is null) return;
        if (!tails.TryGetValue(charId, out var state)) return;

        await state.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await ReadAndDispatchAsync(charId, state).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _log.LogWarning(ex, "RealtimeLogWatcher IO error reading '{Path}' for pilot {CharId}; offset preserved at {Offset}.", state.Path, charId, state.Offset);
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.LogWarning(ex, "RealtimeLogWatcher permission denied on '{Path}' for pilot {CharId}.", state.Path, charId);
        }
        catch (Exception ex)
        {
            // Belt-and-suspenders: TailFileAsync is launched fire-and-forget
            // (Task.Run), so an unexpected exception (parser, UTF-8 decode, stream
            // math) would fault an unobserved task and silently stop tailing this
            // pilot. Surface it and preserve the offset so the next Changed/Created
            // event retries this file.
            _log.LogWarning(ex, "RealtimeLogWatcher unexpected error reading '{Path}' for pilot {CharId}; offset preserved at {Offset}, will retry on next event.", state.Path, charId, state.Offset);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private async Task ReadAndDispatchAsync(long charId, FileTailState state)
    {
        await using var stream = new FileStream(
            state.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        var fileLength = stream.Length;
        if (fileLength <= state.Offset) return; // no new bytes (Created could fire before any write).

        stream.Seek(state.Offset, SeekOrigin.Begin);
        var newByteCount = (int)Math.Min(fileLength - state.Offset, int.MaxValue);
        var buffer = new byte[newByteCount];
        var read = 0;
        while (read < newByteCount)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, newByteCount - read)).ConfigureAwait(false);
            if (n == 0) break;
            read += n;
        }

        // Combine any pending partial-line bytes from the previous read with
        // the newly-read bytes, then split on \n. Lines are decoded UTF-8;
        // \r before \n (CRLF) is stripped by Trim.
        var combined = state.Pending.Length == 0
            ? new ArraySegment<byte>(buffer, 0, read)
            : Concat(state.Pending, buffer, read);

        var (lines, leftover) = SplitOnNewline(combined);
        state.Pending = leftover;
        state.Offset += read;

        foreach (var lineBytes in lines)
        {
            var line = Encoding.UTF8.GetString(lineBytes.Array!, lineBytes.Offset, lineBytes.Count).TrimEnd('\r');
            if (line.Length == 0) continue;
            var ev = _parser.Parse(line, charId);
            if (ev is null) continue;

            try
            {
                await _sink.WriteAsync(ev, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "RealtimeLogWatcher sink threw on Write; continuing.");
            }
        }
    }

    /// <summary>Concatenate the pending partial-line buffer with the newly-read bytes.</summary>
    private static ArraySegment<byte> Concat(byte[] pending, byte[] buffer, int bufferLen)
    {
        var combined = new byte[pending.Length + bufferLen];
        Buffer.BlockCopy(pending, 0, combined, 0, pending.Length);
        Buffer.BlockCopy(buffer, 0, combined, pending.Length, bufferLen);
        return new ArraySegment<byte>(combined, 0, combined.Length);
    }

    /// <summary>
    /// Split a byte buffer on '\n'. Returns the complete-line segments and
    /// the trailing partial-line remainder (which will be the new
    /// <see cref="FileTailState.Pending"/>).
    /// </summary>
    private static (List<ArraySegment<byte>> Lines, byte[] Leftover) SplitOnNewline(ArraySegment<byte> input)
    {
        var lines = new List<ArraySegment<byte>>();
        var data = input.Array!;
        var start = input.Offset;
        var end = input.Offset + input.Count;
        var i = start;
        while (i < end)
        {
            if (data[i] == (byte)'\n')
            {
                lines.Add(new ArraySegment<byte>(data, start, i - start));
                start = i + 1;
            }
            i++;
        }

        if (start < end)
        {
            var leftover = new byte[end - start];
            Buffer.BlockCopy(data, start, leftover, 0, leftover.Length);
            return (lines, leftover);
        }
        return (lines, Array.Empty<byte>());
    }

    /// <summary>Per-pilot tail state.</summary>
    private sealed class FileTailState
    {
        public string Path { get; set; } = "";

        /// <summary>Bytes already read from <see cref="Path"/>.</summary>
        public long Offset { get; set; }

        /// <summary>Trailing bytes read but not yet terminated by '\n'.</summary>
        public byte[] Pending { get; set; } = Array.Empty<byte>();

        /// <summary>Serialises concurrent <c>Changed</c> dispatches against the same file.</summary>
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }
}
