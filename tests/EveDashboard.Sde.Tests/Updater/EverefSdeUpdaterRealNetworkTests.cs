namespace EveDashboard.Sde.Tests.Updater;

using EveDashboard.Persistence.Storage;
using EveDashboard.Sde.Conversion;
using EveDashboard.Sde.Schema;
using EveDashboard.Sde.Storage;
using EveDashboard.Sde.Updater;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Real-network smoke test against <c>https://data.everef.net/...</c>. Default
/// state is <see cref="FactAttribute.Skip"/>'d; an operator un-skips, runs locally,
/// and re-skips before commit per the procedure documented in
/// <c>tests/manual/manual-tests.md</c> (Section 11 — "SDE updater against live everef.net").
/// </summary>
public sealed class EverefSdeUpdaterRealNetworkTests
{
    // If manually testing comment out the line with the skip and uncomment the fact one.
    // Switch back when done.
    [Fact(Skip = "Run manually per tests/manual/manual-tests.md Section 11")]
    //[Fact]
    public async Task CheckAndUpdateAsync_against_live_everef_succeeds()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(), "evemining-sde-realnet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "eve.sqlite");

        await using var db = new SqliteDatabase(
            $"Data Source={dbPath}",
            NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(db);

        var storage = new RealNetTestSdeStorage(dbPath);
        using var http = new HttpClient();
        var converter = new EverefSdeFormatConverter(
            NullLogger<EverefSdeFormatConverter>.Instance);
        var log = NullLogger<EverefSdeUpdater>.Instance;

        // Production constructor → 10,000 types threshold (real everef has ~26,500).
        var updater = new EverefSdeUpdater(http, storage, db, converter, log);

        var result = await updater.CheckAndUpdateAsync();

        // Either Updated (everef has newer data) or NoUpdateNeeded (cache hit).
        // Unreachable means the test failed.
        result.Should().Match<SdeUpdateResult>(
            r => r is SdeUpdateResult.Updated || r is SdeUpdateResult.NoUpdateNeeded);

        if (result is SdeUpdateResult.Updated upd)
        {
            upd.BuildTime.Year.Should().BeGreaterThanOrEqualTo(2020,
                "everef snapshots should have a sane build_time");

            var typeCount = await db.QueryScalarAsync<long>("SELECT COUNT(*) FROM types");
            typeCount.Should().BeGreaterThan(10_000);
        }

        // Cleanup — best-effort.
        try { Directory.Delete(tempDir, recursive: true); }
        catch (IOException) { /* leave for the operator if locked */ }
    }

    private sealed class RealNetTestSdeStorage : ISdeStorage
    {
        public RealNetTestSdeStorage(string path) { DatabasePath = path; }
        public string DatabasePath { get; }
        public Task<bool> EnsureDatabaseExistsAsync(CancellationToken ct = default) =>
            Task.FromResult(false);
    }
}
