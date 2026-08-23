namespace EveDashboard.Sde.Tests.Updater;

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using EveDashboard.Core.Storage;
using EveDashboard.Persistence.Storage;
using EveDashboard.Sde.Conversion;
using EveDashboard.Sde.Schema;
using EveDashboard.Sde.Storage;
using EveDashboard.Sde.Updater;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Unit tests for <see cref="EverefSdeUpdater"/>. Every test here uses a
/// <see cref="FakeHttpMessageHandler"/>-backed <see cref="HttpClient"/> — no
/// network call is ever made. The committed <c>sde-fixtures.tar.xz</c>
/// payload (8 published types) is fed back via the fake handler to exercise
/// the full decompress + import path. Also covered: the validation threshold
/// (an import below it rolls back), subscriber isolation on the Updated
/// event, and the temp-folder sweep on both success and failure.
/// </summary>
public sealed class EverefSdeUpdaterTests
{
    private const string EverefUrl =
        "https://data.everef.net/reference-data/reference-data-latest.tar.xz";

    // Round-trippable Last-Modified value used by the cache-tag tests.
    private const string KnownLastMod = "Wed, 30 Apr 2025 12:12:56 GMT";
    private const string KnownEtag = "\"abc123\"";

    private static DateTimeOffset KnownLastModParsed => DateTimeOffset.ParseExact(
        KnownLastMod, "R", CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor null-arg checks (test 1 in plan, expanded to one Fact per arg)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_throws_on_null_http()
    {
        var act = () => new EverefSdeUpdater(
            null!, Stub.Storage(), Stub.Db(), Stub.Converter(), Stub.Log());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_throws_on_null_storage()
    {
        var act = () => new EverefSdeUpdater(
            Stub.Http(), null!, Stub.Db(), Stub.Converter(), Stub.Log());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_throws_on_null_db()
    {
        var act = () => new EverefSdeUpdater(
            Stub.Http(), Stub.Storage(), null!, Stub.Converter(), Stub.Log());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_throws_on_null_converter()
    {
        var act = () => new EverefSdeUpdater(
            Stub.Http(), Stub.Storage(), Stub.Db(), null!, Stub.Log());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_throws_on_null_log()
    {
        var act = () => new EverefSdeUpdater(
            Stub.Http(), Stub.Storage(), Stub.Db(), Stub.Converter(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Conditional-handshake paths
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HEAD_tags_match_local_yields_NoUpdateNeeded()
    {
        await using var scope = await TestScope.CreateAsync();
        await SeedMetaCacheTagsAsync(scope.Db, KnownLastMod, KnownEtag);

        scope.Handler.Responder = _ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };
            resp.Content.Headers.LastModified = KnownLastModParsed;
            resp.Headers.ETag = new EntityTagHeaderValue(KnownEtag);
            return resp;
        };

        var result = await scope.Updater.CheckAndUpdateAsync();

        result.Should().BeOfType<SdeUpdateResult.NoUpdateNeeded>();
        scope.Handler.Requests.Should().HaveCount(1, "HEAD-tags-match must short-circuit before GET");
        scope.Handler.Requests[0].Method.Should().Be(HttpMethod.Head);
    }

    [Fact]
    public async Task HEAD_tags_differ_then_GET_304_yields_NoUpdateNeeded()
    {
        await using var scope = await TestScope.CreateAsync();
        await SeedMetaCacheTagsAsync(scope.Db, KnownLastMod, KnownEtag);

        scope.Handler.Responder = req =>
        {
            if (req.Method == HttpMethod.Head)
            {
                var head = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>()),
                };
                // Differ in BOTH tags so the local-compare proceeds to GET.
                head.Content.Headers.LastModified = DateTimeOffset.UtcNow;
                head.Headers.ETag = new EntityTagHeaderValue("\"different\"");
                return head;
            }
            // Server short-circuits the GET with 304.
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        };

        var result = await scope.Updater.CheckAndUpdateAsync();

        result.Should().BeOfType<SdeUpdateResult.NoUpdateNeeded>();
        scope.Handler.Requests.Select(r => r.Method)
            .Should().Equal(HttpMethod.Head, HttpMethod.Get);
    }

    [Fact]
    public async Task HEAD_throws_HttpRequestException_yields_Unreachable()
    {
        await using var scope = await TestScope.CreateAsync();

        scope.Handler.Responder = _ => throw new HttpRequestException("DNS failure");

        var result = await scope.Updater.CheckAndUpdateAsync();

        result.Should().BeOfType<SdeUpdateResult.Unreachable>()
            .Which.Reason.Should().Contain("HEAD failed").And.Contain("DNS failure");
        scope.Handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task HTTP_5xx_on_GET_yields_Unreachable()
    {
        await using var scope = await TestScope.CreateAsync();

        scope.Handler.Responder = req =>
        {
            if (req.Method == HttpMethod.Head)
            {
                var head = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>()),
                };
                head.Content.Headers.LastModified = DateTimeOffset.UtcNow;
                head.Headers.ETag = new EntityTagHeaderValue("\"x\"");
                return head;
            }
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                ReasonPhrase = "Service Unavailable",
            };
        };

        var result = await scope.Updater.CheckAndUpdateAsync();

        result.Should().BeOfType<SdeUpdateResult.Unreachable>()
            .Which.Reason.Should().Contain("503");
    }

    [Fact]
    public async Task Conditional_GET_request_headers_sent_when_meta_has_values()
    {
        await using var scope = await TestScope.CreateAsync();
        await SeedMetaCacheTagsAsync(scope.Db, KnownLastMod, KnownEtag);

        HttpRequestMessage? capturedGet = null;
        scope.Handler.Responder = req =>
        {
            if (req.Method == HttpMethod.Head)
            {
                // Differ from stored — force progression to GET.
                var head = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>()),
                };
                head.Content.Headers.LastModified = DateTimeOffset.UtcNow;
                head.Headers.ETag = new EntityTagHeaderValue("\"different\"");
                return head;
            }
            capturedGet = req;
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        };

        _ = await scope.Updater.CheckAndUpdateAsync();

        capturedGet.Should().NotBeNull();
        capturedGet!.Headers.IfModifiedSince.Should().Be(KnownLastModParsed);
        capturedGet.Headers.IfNoneMatch.Should().ContainSingle()
            .Which.Tag.Should().Be(KnownEtag);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Successful update + validation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Successful_update_imports_payload_raises_event_writes_cache_tags()
    {
        await using var scope = await TestScope.CreateAsync(minTypeRowsForCommit: 1);

        var fixtureBytes = SdeFixtureLoader.LoadTarXzBytes();
        scope.Handler.Responder = req =>
            req.Method == HttpMethod.Head
                ? HeadOk(KnownLastMod, KnownEtag)
                : GetOk(KnownLastMod, KnownEtag, fixtureBytes);

        var raised = new List<SdeUpdatedEventArgs>();
        scope.Updater.Updated += (_, args) => raised.Add(args);

        var result = await scope.Updater.CheckAndUpdateAsync();

        result.Should().BeOfType<SdeUpdateResult.Updated>()
            .Which.BuildTime.Should().Be(
                DateTimeOffset.Parse("2026-04-30T12:00:00Z", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));

        raised.Should().HaveCount(1);
        raised[0].BuildTime.Should().Be(((SdeUpdateResult.Updated)result).BuildTime);

        var (storedLm, storedEtag) = await ReadCacheTagsAsync(scope.Db);
        storedLm.Should().Be(KnownLastMod);
        storedEtag.Should().Be(KnownEtag);

        var typeCount = await scope.Db.QueryScalarAsync<long>("SELECT COUNT(*) FROM types");
        typeCount.Should().Be(8, "fixture has 8 published types");
    }

    [Fact]
    public async Task Validation_below_threshold_rolls_back_and_returns_Unreachable()
    {
        // Threshold 100 against the 8-row fixture forces validation failure.
        await using var scope = await TestScope.CreateAsync(minTypeRowsForCommit: 100);

        var fixtureBytes = SdeFixtureLoader.LoadTarXzBytes();
        scope.Handler.Responder = req =>
            req.Method == HttpMethod.Head
                ? HeadOk(KnownLastMod, KnownEtag)
                : GetOk(KnownLastMod, KnownEtag, fixtureBytes);

        var raised = new List<SdeUpdatedEventArgs>();
        scope.Updater.Updated += (_, args) => raised.Add(args);

        var result = await scope.Updater.CheckAndUpdateAsync();

        result.Should().BeOfType<SdeUpdateResult.Unreachable>()
            .Which.Reason.Should().StartWith("Import failed");

        raised.Should().BeEmpty("Updated event must NOT fire when validation rolls back");

        var typeCount = await scope.Db.QueryScalarAsync<long>("SELECT COUNT(*) FROM types");
        typeCount.Should().Be(0, "rollback must preserve pre-update state");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Subscriber isolation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Updated_event_subscriber_throws_does_not_break_result_or_cleanup()
    {
        await using var scope = await TestScope.CreateAsync(minTypeRowsForCommit: 1);

        var fixtureBytes = SdeFixtureLoader.LoadTarXzBytes();
        scope.Handler.Responder = req =>
            req.Method == HttpMethod.Head
                ? HeadOk(KnownLastMod, KnownEtag)
                : GetOk(KnownLastMod, KnownEtag, fixtureBytes);

        var firstRan = false;
        var secondRan = false;
        scope.Updater.Updated += (_, _) =>
        {
            firstRan = true;
            throw new InvalidOperationException("first subscriber bug");
        };
        scope.Updater.Updated += (_, _) => { secondRan = true; };

        var result = await scope.Updater.CheckAndUpdateAsync();

        result.Should().BeOfType<SdeUpdateResult.Updated>();
        firstRan.Should().BeTrue();
        secondRan.Should().BeTrue("second subscriber must run despite first one throwing");

        scope.Log.Entries.Should().Contain(e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            e.Exception is InvalidOperationException &&
            e.Exception.Message.Contains("first subscriber bug", StringComparison.Ordinal));

        // Temp folder cleaned up on success path too.
        var tmpRoot = Path.Combine(Path.GetDirectoryName(scope.Storage.DatabasePath)!, "tmp");
        if (Directory.Exists(tmpRoot))
            Directory.GetDirectories(tmpRoot).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Temp folder cleanup + stale-orphan sweep
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Temp_folder_cleaned_up_after_success_and_pre_existing_orphans_swept()
    {
        await using var scope = await TestScope.CreateAsync(minTypeRowsForCommit: 1);
        var sdeDir = Path.GetDirectoryName(scope.Storage.DatabasePath)!;

        // Pre-seed two orphan guid folders simulating prior crashes.
        var orphanA = Path.Combine(sdeDir, "tmp", Guid.NewGuid().ToString("N"));
        var orphanB = Path.Combine(sdeDir, "tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(orphanA);
        Directory.CreateDirectory(orphanB);
        await File.WriteAllTextAsync(Path.Combine(orphanA, "leftover.txt"), "stale");

        var fixtureBytes = SdeFixtureLoader.LoadTarXzBytes();
        scope.Handler.Responder = req =>
            req.Method == HttpMethod.Head
                ? HeadOk(KnownLastMod, KnownEtag)
                : GetOk(KnownLastMod, KnownEtag, fixtureBytes);

        var result = await scope.Updater.CheckAndUpdateAsync();

        result.Should().BeOfType<SdeUpdateResult.Updated>();

        Directory.GetDirectories(Path.Combine(sdeDir, "tmp")).Should().BeEmpty(
            "sweep must clear pre-existing orphans AND the current run's folder");
    }

    [Fact]
    public async Task Temp_folder_cleaned_up_after_GET_failure()
    {
        await using var scope = await TestScope.CreateAsync();
        var sdeDir = Path.GetDirectoryName(scope.Storage.DatabasePath)!;

        scope.Handler.Responder = req =>
        {
            if (req.Method == HttpMethod.Head) return HeadOk(KnownLastMod, "\"different-from-stored\"");
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        };

        var result = await scope.Updater.CheckAndUpdateAsync();

        result.Should().BeOfType<SdeUpdateResult.Unreachable>();

        // The tmp/ root may not exist (we never created a temp dir on the 503 path);
        // if it does, no orphan guid subfolders should remain.
        var tmpRoot = Path.Combine(sdeDir, "tmp");
        if (Directory.Exists(tmpRoot))
            Directory.GetDirectories(tmpRoot).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static HttpResponseMessage HeadOk(string lastModified, string etag)
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Array.Empty<byte>()),
        };
        resp.Content.Headers.LastModified = DateTimeOffset.ParseExact(
            lastModified, "R", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        resp.Headers.ETag = new EntityTagHeaderValue(etag);
        return resp;
    }

    private static HttpResponseMessage GetOk(string lastModified, string etag, byte[] body)
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body),
        };
        resp.Content.Headers.LastModified = DateTimeOffset.ParseExact(
            lastModified, "R", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        resp.Headers.ETag = new EntityTagHeaderValue(etag);
        return resp;
    }

    private static async Task SeedMetaCacheTagsAsync(SqliteDatabase db, string lastModified, string etag)
    {
        await db.ExecuteNonQueryAsync(
            """
            INSERT INTO meta (id, build_time, last_modified, etag)
            VALUES (1, '2025-01-01T00:00:00Z', $lm, $et)
            ON CONFLICT(id) DO UPDATE SET
                last_modified = excluded.last_modified,
                etag          = excluded.etag;
            """,
            new Dictionary<string, object?>
            {
                ["$lm"] = lastModified,
                ["$et"] = etag,
            });
    }

    private static async Task<(string? LastMod, string? Etag)> ReadCacheTagsAsync(SqliteDatabase db)
    {
        var rows = await db.QueryAsync(
            "SELECT last_modified, etag FROM meta WHERE id = 1",
            r => (
                LastMod: r.IsDBNull(0) ? null : r.GetString(0),
                Etag:    r.IsDBNull(1) ? null : r.GetString(1)));
        return rows.Count == 0 ? (null, null) : rows[0];
    }

    /// <summary>
    /// Per-test scaffolding: temp directory under <c>%TEMP%</c>, real
    /// file-backed <see cref="SqliteDatabase"/> with schema initialised, fake
    /// <see cref="HttpClient"/>, real <see cref="EverefSdeFormatConverter"/>,
    /// and an <see cref="EverefSdeUpdater"/> built via the test-seam ctor with
    /// configurable <c>minTypeRowsForCommit</c>.
    /// </summary>
    private sealed class TestScope : IAsyncDisposable
    {
        public string TempDir { get; }
        public SqliteDatabase Db { get; }
        public TestSdeStorage Storage { get; }
        public FakeHttpMessageHandler Handler { get; }
        public HttpClient Http { get; }
        public EverefSdeFormatConverter Converter { get; }
        public CapturingLogger<EverefSdeUpdater> Log { get; }
        public EverefSdeUpdater Updater { get; }

        private TestScope(
            string tempDir,
            SqliteDatabase db,
            TestSdeStorage storage,
            FakeHttpMessageHandler handler,
            HttpClient http,
            EverefSdeFormatConverter converter,
            CapturingLogger<EverefSdeUpdater> log,
            EverefSdeUpdater updater)
        {
            TempDir = tempDir;
            Db = db;
            Storage = storage;
            Handler = handler;
            Http = http;
            Converter = converter;
            Log = log;
            Updater = updater;
        }

        public static async Task<TestScope> CreateAsync(long minTypeRowsForCommit = 1)
        {
            var tempDir = Path.Combine(
                Path.GetTempPath(), "evemining-sde-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var dbPath = Path.Combine(tempDir, "eve.sqlite");

            var db = new SqliteDatabase(
                $"Data Source={dbPath}",
                NullLogger<SqliteDatabase>.Instance);
            await SchemaInitializer.EnsureSchemaAsync(db);

            var storage = new TestSdeStorage(dbPath);
            var handler = new FakeHttpMessageHandler();
            var http = new HttpClient(handler);
            var converter = new EverefSdeFormatConverter(
                NullLogger<EverefSdeFormatConverter>.Instance);
            var log = new CapturingLogger<EverefSdeUpdater>();
            var updater = new EverefSdeUpdater(
                http, storage, db, converter, log, minTypeRowsForCommit);

            return new TestScope(tempDir, db, storage, handler, http, converter, log, updater);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            Http.Dispose();
            try { Directory.Delete(TempDir, recursive: true); }
            catch (IOException) { /* best-effort */ }
            catch (UnauthorizedAccessException) { /* best-effort */ }
        }
    }

    private sealed class TestSdeStorage : ISdeStorage
    {
        public TestSdeStorage(string path) { DatabasePath = path; }
        public string DatabasePath { get; }
        public Task<bool> EnsureDatabaseExistsAsync(CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    /// <summary>
    /// Minimal stub factory for null-arg constructor checks. Each method
    /// returns a fresh instance so any one of them can be replaced with null
    /// in a single test row.
    /// </summary>
    private static class Stub
    {
        public static HttpClient Http() => new();
        public static ISdeStorage Storage() => new TestSdeStorage("X:/nope/eve.sqlite");
        public static ISqliteDatabase Db() =>
            new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        public static ISdeFormatConverter Converter() =>
            new EverefSdeFormatConverter(NullLogger<EverefSdeFormatConverter>.Instance);
        public static Microsoft.Extensions.Logging.ILogger<EverefSdeUpdater> Log() =>
            NullLogger<EverefSdeUpdater>.Instance;
    }
}
