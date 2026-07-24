namespace EveDashboard.Sde.Updater;

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using EveDashboard.Core.Storage;
using EveDashboard.Sde.Conversion;
using EveDashboard.Sde.Storage;
using Microsoft.Extensions.Logging;
using SharpCompress.Common;
using SharpCompress.Readers;

/// <summary>
/// Default <see cref="ISdeUpdater"/> implementation. Probes
/// <see href="https://data.everef.net/reference-data/reference-data-latest.tar.xz"/>
/// for newer reference-data snapshots and applies them in-place to the live
/// SDE database.
/// </summary>
/// <remarks>
/// See <see cref="ISdeUpdater"/> for the contract. Single-attempt protocol
/// (plan 08 decision 8.6); subscriber failures isolated per-handler
/// (decision 8.20); orphan temp folders swept at entry (decision 8.23);
/// validation threshold injected via test-seam constructor (decision 8.16).
/// </remarks>
public sealed class EverefSdeUpdater : ISdeUpdater
{
    private const string EverefUrl =
        "https://data.everef.net/reference-data/reference-data-latest.tar.xz";

    /// <summary>
    /// Default validation threshold. After import, <c>SELECT COUNT(*) FROM types</c>
    /// must be at least this value or the transaction rolls back. Real everef
    /// snapshots have ~26,500 published types — 10,000 is well above any
    /// plausible "garbage payload" outcome and well below the legitimate count.
    /// </summary>
    private const long DefaultMinTypeRowsForCommit = 10_000;

    /// <summary>
    /// Maximum time the archive download may go WITHOUT receiving any new bytes
    /// before it is abandoned. The GET uses <see cref="HttpCompletionOption.ResponseHeadersRead"/>,
    /// which removes <see cref="HttpClient.Timeout"/> from the body transfer, and the
    /// production call passes <see cref="CancellationToken.None"/> — so without this an
    /// upstream stall after headers would hang the whole bootstrap chain (and thus app
    /// launch) indefinitely. This is an INACTIVITY budget, not a total-download cap, so a
    /// slow-but-progressing download is never falsely aborted; 60s is well clear of any
    /// legitimate read gap on a ~14 MB tarball.
    /// </summary>
    private static readonly TimeSpan DownloadInactivityTimeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly ISdeStorage _storage;
    private readonly ISqliteDatabase _db;
    private readonly ISdeFormatConverter _converter;
    private readonly ILogger<EverefSdeUpdater> _log;
    private readonly long _minTypeRowsForCommit;

    /// <summary>Production constructor — uses the default safety threshold (10,000 types).</summary>
    /// <param name="http">HTTP client used for everef.net probes and downloads.</param>
    /// <param name="storage">SDE storage manager — its <see cref="ISdeStorage.DatabasePath"/> defines the parent directory for the temp scratch folder.</param>
    /// <param name="db">Live SDE database the updater writes through.</param>
    /// <param name="converter">Converter used to import the decompressed JSON tree.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public EverefSdeUpdater(
        HttpClient http,
        ISdeStorage storage,
        ISqliteDatabase db,
        ISdeFormatConverter converter,
        ILogger<EverefSdeUpdater> log)
        : this(http, storage, db, converter, log, DefaultMinTypeRowsForCommit) { }

    /// <summary>
    /// Test-seam constructor — pass an explicit validation threshold. The
    /// production constructor above is what call sites should use; this
    /// overload exists so unit-test fixtures (which produce far fewer than
    /// 10,000 types) can run the happy-path tests by passing a threshold the
    /// fixture cleanly exceeds. Per plan 08 decision 8.16.
    /// </summary>
    internal EverefSdeUpdater(
        HttpClient http,
        ISdeStorage storage,
        ISqliteDatabase db,
        ISdeFormatConverter converter,
        ILogger<EverefSdeUpdater> log,
        long minTypeRowsForCommit)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(log);
        _http = http;
        _storage = storage;
        _db = db;
        _converter = converter;
        _log = log;
        _minTypeRowsForCommit = minTypeRowsForCommit;
    }

    /// <inheritdoc />
    public event EventHandler<SdeUpdatedEventArgs>? Updated;

    /// <inheritdoc />
    public async Task<SdeUpdateResult> CheckAndUpdateAsync(CancellationToken ct = default)
    {
        // 0. Sweep orphan temp folders left behind by prior crashes (decision 8.23).
        SweepStaleTempDirs();

        // 1. Read currently-stored cache tags from the live SDE.
        var (storedLastMod, storedEtag) = await ReadStoredCacheTagsAsync(ct).ConfigureAwait(false);

        // 2. Plain HEAD probe — read response headers, compare locally (decision 8.15).
        HttpResponseMessage headResp;
        try
        {
            using var headReq = new HttpRequestMessage(HttpMethod.Head, EverefUrl);
            headResp = await _http.SendAsync(headReq, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "HEAD {Url} failed; reporting Unreachable", EverefUrl);
            return new SdeUpdateResult.Unreachable($"HEAD failed: {ex.Message}");
        }

        using (headResp)
        {
            if (!headResp.IsSuccessStatusCode)
                return new SdeUpdateResult.Unreachable(
                    $"HEAD returned HTTP {(int)headResp.StatusCode} {headResp.ReasonPhrase}");

            string? upstreamLastMod = FormatRfc7231(headResp.Content.Headers.LastModified);
            string? upstreamEtag = headResp.Headers.ETag?.Tag;

            if (TagsMatch(storedLastMod, upstreamLastMod) && TagsMatch(storedEtag, upstreamEtag))
            {
                _log.LogInformation("SDE up-to-date (HEAD tags match local meta)");
                return new SdeUpdateResult.NoUpdateNeeded();
            }
        }

        // 3. Conditional GET. Server can still 304 if HEAD-vs-GET race left HEAD
        //    claiming different but GET sees current.
        HttpResponseMessage getResp;
        try
        {
            using var getReq = new HttpRequestMessage(HttpMethod.Get, EverefUrl);
            if (!string.IsNullOrEmpty(storedLastMod) && TryParseRfc7231(storedLastMod, out var ifMod))
                getReq.Headers.IfModifiedSince = ifMod;
            if (!string.IsNullOrEmpty(storedEtag) && EntityTagHeaderValue.TryParse(storedEtag, out var etag))
                getReq.Headers.IfNoneMatch.Add(etag);

            // ResponseHeadersRead — body read is governed by ct only, not
            // HttpClient.Timeout (decision 8.17). Slow downloads (~100 KB/s on a
            // 14 MB tarball) can exceed the default 100s connection-and-body timeout;
            // ResponseHeadersRead caps the timeout at "until headers arrive." The
            // body transfer is instead bounded by DownloadInactivityTimeout below,
            // so a stall after headers cannot hang startup forever.
            getResp = await _http.SendAsync(
                getReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "GET {Url} failed; reporting Unreachable", EverefUrl);
            return new SdeUpdateResult.Unreachable($"GET failed: {ex.Message}");
        }

        // 4. Process the GET response inside a try/finally that owns the temp
        //    folder lifecycle and the response disposal.
        var sdeDir = Path.GetDirectoryName(_storage.DatabasePath)!;
        var tempDir = Path.Combine(sdeDir, "tmp", Guid.NewGuid().ToString("N"));

        try
        {
            if (getResp.StatusCode == HttpStatusCode.NotModified)
            {
                _log.LogInformation("SDE up-to-date (GET 304)");
                return new SdeUpdateResult.NoUpdateNeeded();
            }

            if (!getResp.IsSuccessStatusCode)
                return new SdeUpdateResult.Unreachable(
                    $"GET returned HTTP {(int)getResp.StatusCode} {getResp.ReasonPhrase}");

            // Cache tags read from the GET response — these are what get stored on commit.
            string? newLastMod = FormatRfc7231(getResp.Content.Headers.LastModified);
            string? newEtag = getResp.Headers.ETag?.Tag;

            // Stream the archive to disk (decision 8.11 — keep memory low).
            Directory.CreateDirectory(tempDir);
            var archivePath = Path.Combine(tempDir, "everef.tar.xz");
            await using (var net = await getResp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var file = File.Create(archivePath))
            {
                await CopyWithInactivityTimeoutAsync(
                    net, file, DownloadInactivityTimeout, ct).ConfigureAwait(false);
            }

            var jsonRoot = Path.Combine(tempDir, "extracted");
            Directory.CreateDirectory(jsonRoot);

            // Decompress xz + extract tar in one pass via SharpCompress (decision 8.7).
            // Tarball internal layout has JSON files directly at the root — no
            // wrapper folder (decision 8.19).
            ExtractTarXz(archivePath, jsonRoot);

            var newBuildTime = await ImportInTransactionAsync(
                jsonRoot, newLastMod, newEtag, ct).ConfigureAwait(false);

            // Subscriber failures isolated per-handler (decision 8.20). Multicast
            // delegates short-circuit on the first throw; iterate the invocation
            // list so every subscriber observes the event and every failure is
            // logged at Warning.
            if (Updated is { } handlers)
            {
                foreach (EventHandler<SdeUpdatedEventArgs> handler
                         in handlers.GetInvocationList())
                {
                    try
                    {
                        handler(this, new SdeUpdatedEventArgs(newBuildTime));
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex,
                            "Subscriber to ISdeUpdater.Updated threw; commit succeeded, other subscribers continue");
                    }
                }
            }

            return new SdeUpdateResult.Updated(newBuildTime);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "SDE update failed during import/commit; existing data preserved");
            return new SdeUpdateResult.Unreachable($"Import failed: {ex.Message}");
        }
        finally
        {
            getResp.Dispose();

            // Put toys away — best-effort. A leftover temp folder is annoying but
            // not data-corrupting; the next-run sweep (decision 8.23) catches any
            // leak from here.
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException ex)
            {
                _log.LogWarning(ex, "Could not clean up temp folder {Path}; will sweep next run", tempDir);
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.LogWarning(ex, "Permission denied cleaning up temp folder {Path}; will sweep next run", tempDir);
            }
        }
    }

    /// <summary>
    /// Copy <paramref name="source"/> to <paramref name="destination"/>, aborting
    /// with a <see cref="TimeoutException"/> if no new bytes arrive for
    /// <paramref name="inactivityTimeout"/>. This is an INACTIVITY budget (reset on
    /// every successful read), not a total-download cap, so a slow-but-progressing
    /// transfer is never falsely aborted. A genuine cancellation of
    /// <paramref name="ct"/> propagates as <see cref="OperationCanceledException"/>;
    /// only a stall with the caller's token still live is translated to a timeout.
    /// </summary>
    /// <param name="source">Stream to read from (the HTTP response body).</param>
    /// <param name="destination">Stream to write to (the on-disk archive).</param>
    /// <param name="inactivityTimeout">Maximum gap between received bytes.</param>
    /// <param name="ct">Caller cancellation token.</param>
    /// <exception cref="TimeoutException">Thrown when no data is received for the budget.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled.</exception>
    internal static async Task CopyWithInactivityTimeoutAsync(
        Stream source, Stream destination, TimeSpan inactivityTimeout, CancellationToken ct)
    {
        var buffer = new byte[81920];
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        while (true)
        {
            timeoutCts.CancelAfter(inactivityTimeout); // (re)arm the inactivity timer for this read
            int read;
            try
            {
                read = await source.ReadAsync(buffer.AsMemory(), timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The inactivity timer fired, not the caller — surface as a timeout so
                // CheckAndUpdateAsync's catch reports Unreachable (non-fatal) rather
                // than treating it as a genuine cancellation.
                throw new TimeoutException(
                    $"SDE download stalled: no data received for {inactivityTimeout.TotalSeconds:F0}s.");
            }

            timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan); // disarm so a slow disk write can't trip the timer
            if (read == 0) break; // end of stream
            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Best-effort cleanup of orphan <c>tmp/&lt;guid&gt;/</c> folders left behind
    /// by previous crashes. Iterates folders under <c>&lt;sde-dir&gt;/tmp/</c>,
    /// recursively deletes each. Failures log at Warning and continue — sweep
    /// is housekeeping, not a hard precondition. Per plan 08 decision 8.23.
    /// </summary>
    private void SweepStaleTempDirs()
    {
        var sdeDir = Path.GetDirectoryName(_storage.DatabasePath)!;
        var tmpRoot = Path.Combine(sdeDir, "tmp");
        if (!Directory.Exists(tmpRoot)) return;

        foreach (var dir in Directory.GetDirectories(tmpRoot))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException ex)
            {
                _log.LogWarning(ex,
                    "Could not sweep stale temp folder {Path}; will retry next run", dir);
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.LogWarning(ex,
                    "Permission denied sweeping {Path}; will retry next run", dir);
            }
        }
    }

    /// <summary>
    /// Read <c>last_modified</c> and <c>etag</c> from the live SDE's <c>meta</c>
    /// table. Both columns are nullable — first-launch fallback extract leaves
    /// them <c>NULL</c> until the first successful update. Returns <c>(null, null)</c>
    /// when the row is missing or the values are <c>NULL</c>.
    /// </summary>
    private async Task<(string? LastMod, string? Etag)> ReadStoredCacheTagsAsync(CancellationToken ct)
    {
        var rows = await _db.QueryAsync(
            "SELECT last_modified, etag FROM meta WHERE id = 1",
            r => (
                LastMod: r.IsDBNull(0) ? null : r.GetString(0),
                Etag:    r.IsDBNull(1) ? null : r.GetString(1)
            ),
            null, ct).ConfigureAwait(false);

        return rows.Count == 0 ? (null, null) : rows[0];
    }

    /// <summary>
    /// Open a transaction on the live SDE, call the converter's caller-owns-tx
    /// method, validate post-import (<c>COUNT(*) FROM types &gt;= _minTypeRowsForCommit</c>),
    /// update <c>meta.last_modified</c> / <c>meta.etag</c>, read the new
    /// <c>meta.build_time</c>, and commit. On any throw, ROLLBACK and re-throw.
    /// Per plan 08 decision 8.4 / 8.10 / 8.16 / 8.21.
    /// </summary>
    /// <returns>The new <c>meta.build_time</c> as a <see cref="DateTimeOffset"/>.</returns>
    private async Task<DateTimeOffset> ImportInTransactionAsync(
        string jsonRoot, string? newLastMod, string? newEtag, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync("BEGIN TRANSACTION", null, ct).ConfigureAwait(false);
        try
        {
            await _converter.ImportToOpenTransactionAsync(jsonRoot, _db, null, ct).ConfigureAwait(false);

            // Validation (decisions 8.4 / 8.16).
            var typeCount = await _db.QueryScalarAsync<long>(
                "SELECT COUNT(*) FROM types", null, ct).ConfigureAwait(false);
            if (typeCount < _minTypeRowsForCommit)
            {
                throw new InvalidDataException(
                    $"Post-import validation failed: types row count {typeCount} below threshold {_minTypeRowsForCommit}");
            }

            // Update conditional-GET cache tags from the GET response.
            await _db.ExecuteNonQueryAsync(
                "UPDATE meta SET last_modified = $lm, etag = $et WHERE id = 1",
                new Dictionary<string, object?>
                {
                    ["$lm"] = (object?)newLastMod ?? DBNull.Value,
                    ["$et"] = (object?)newEtag    ?? DBNull.Value,
                }, ct).ConfigureAwait(false);

            // Read + validate + parse the new build_time BEFORE the COMMIT so that
            // any failure here still has an active transaction to ROLLBACK. If this
            // ran after COMMIT, the catch's ROLLBACK would fire with no active
            // transaction (itself throwing) and a genuinely-committed update would be
            // mis-reported as failed, so the Updated event would never fire.
            var buildTimeStr = await _db.QueryScalarAsync<string>(
                "SELECT build_time FROM meta WHERE id = 1", null, ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(buildTimeStr))
                throw new InvalidDataException("meta.build_time is empty after import");

            var newBuildTime = DateTimeOffset.Parse(
                buildTimeStr,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            await _db.ExecuteNonQueryAsync("COMMIT", null, ct).ConfigureAwait(false);

            return newBuildTime;
        }
        catch
        {
            try { await _db.ExecuteNonQueryAsync("ROLLBACK", null, ct).ConfigureAwait(false); }
            catch (Exception rbEx) { _log.LogError(rbEx, "Rollback also failed during SDE update"); }
            throw;
        }
    }

    /// <summary>
    /// Extract a tar.xz archive at <paramref name="archivePath"/> into
    /// <paramref name="destDir"/> with no wrapper folder. Streams from disk
    /// — peak memory stays bounded regardless of the ~250 MB decompressed
    /// payload size. Uses <see cref="ReaderFactory.OpenReader(Stream, ReaderOptions)"/>
    /// (auto-detects the xz+tar pairing) rather than wrapping <c>XZStream</c>
    /// in <c>TarReader</c> manually — the latter trips a Rewind call that
    /// SharpCompress 0.47 only supports on a recording-enabled stream.
    /// </summary>
    /// <remarks>
    /// Defense-in-depth against GHSA-6c8g-7p36-r338 (CWE-22 path traversal in
    /// SharpCompress &lt;= 0.47.4 <c>WriteToDirectoryInternal</c>): we never
    /// call <c>IArchive.WriteToDirectory</c> (the umbrella API with the unsafe
    /// directory-entry fast-path), we skip directory entries entirely, we
    /// skip symlink entries (closing the TAR symlink-chain escalation
    /// regardless of <c>SymbolicLinkHandler</c>), and we validate every file
    /// entry's resolved target path lies inside <paramref name="destDir"/>
    /// before invoking the per-entry <c>WriteEntryToDirectory</c>. The guard
    /// at our call site keeps the safety property explicit and resilient to
    /// future upstream regressions or contributor-side refactors.
    /// </remarks>
    /// <param name="archivePath">Path on disk to the source tar / tar.xz archive.</param>
    /// <param name="destDir">Extraction root. Must already exist; the canonical form is computed once and reused as the bound for the per-entry traversal check.</param>
    /// <exception cref="ExtractionException">Thrown when an entry's resolved target path lies outside <paramref name="destDir"/> (GHSA-6c8g-7p36-r338 defense-in-depth guard).</exception>
    internal void ExtractTarXz(string archivePath, string destDir)
    {
        using var fs = File.OpenRead(archivePath);
        using var reader = ReaderFactory.OpenReader(fs);
        var options = new ExtractionOptions
        {
            ExtractFullPath = true,
            Overwrite = true,
        };

        // Canonicalise the extraction root once for the per-entry traversal
        // check below. Trailing separator is mandatory so that a sibling-prefix
        // attack like destDir = "/tmp/extract" with entry key "../extract-evil/x"
        // (which resolves to "/tmp/extract-evil/x") can't pass a naive
        // StartsWith("/tmp/extract") check.
        var rootCanonical = Path.GetFullPath(destDir);
        if (!rootCanonical.EndsWith(Path.DirectorySeparatorChar))
        {
            rootCanonical += Path.DirectorySeparatorChar;
        }

        while (reader.MoveToNextEntry())
        {
            var entry = reader.Entry;
            if (entry.IsDirectory) continue;

            // Symlink entries are anomalous in the everef SDE archive (plain
            // JSON only). Skip them and surface at Warning. This is the
            // chokepoint that closes the TAR symlink-chain escalation in
            // GHSA-6c8g-7p36-r338 independent of any SymbolicLinkHandler.
            if (!string.IsNullOrEmpty(entry.LinkTarget))
            {
                _log.LogWarning(
                    "Skipping symlink entry '{Key}' -> '{LinkTarget}' in SDE archive '{ArchivePath}' (everef SDE is expected to contain plain files only).",
                    entry.Key, entry.LinkTarget, archivePath);
                continue;
            }

            var key = entry.Key;
            if (string.IsNullOrEmpty(key))
            {
                _log.LogWarning(
                    "Skipping SDE archive entry with null/empty key in '{ArchivePath}'.", archivePath);
                continue;
            }

            // Traversal check: resolve the entry's target path the same way
            // the OS would (via Path.GetFullPath, which normalises '..' and
            // absorbs absolute Keys that would otherwise override the base —
            // Path.Combine("/a/b", "/c") returns "/c") and confirm it lies
            // inside the canonical root.
            var targetCanonical = Path.GetFullPath(Path.Combine(rootCanonical, key));
            if (!targetCanonical.StartsWith(rootCanonical, StringComparison.Ordinal))
            {
                throw new ExtractionException(
                    $"Refusing to extract SDE archive entry '{key}': resolved target '{targetCanonical}' is outside the extraction root '{rootCanonical}'. This may indicate a malicious archive (CWE-22 / GHSA-6c8g-7p36-r338).");
            }

            reader.WriteEntryToDirectory(destDir, options);
        }
    }

    /// <summary>
    /// Format a <see cref="DateTimeOffset"/> as RFC 7231 IMF-fixdate
    /// (e.g. <c>Wed, 30 Apr 2026 12:12:56 GMT</c>) — the form HTTP servers use
    /// in <c>Last-Modified</c> headers. Returns <c>null</c> if input is null.
    /// </summary>
    private static string? FormatRfc7231(DateTimeOffset? dt) =>
        dt?.UtcDateTime.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// Try to parse an RFC 7231 IMF-fixdate string into a
    /// <see cref="DateTimeOffset"/>. Used to round-trip stored cache tags
    /// back into <c>HttpRequestMessage.Headers.IfModifiedSince</c>.
    /// </summary>
    private static bool TryParseRfc7231(string s, out DateTimeOffset value)
    {
        return DateTimeOffset.TryParseExact(
            s, "R", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
    }

    /// <summary>
    /// Compare a stored cache tag to an upstream-reported value.
    /// Both null counts as a match (no cache state to compare).
    /// Otherwise: ordinal string equality; one-null-one-not is a mismatch.
    /// </summary>
    private static bool TagsMatch(string? a, string? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return string.Equals(a, b, StringComparison.Ordinal);
    }
}
