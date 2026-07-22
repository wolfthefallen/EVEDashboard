using EveDashboard.Persistence.Storage;
using EveDashboard.Sde.Meta;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EveDashboard.Sde.Tests.Meta;

public class SdeMetaReaderTests
{
    // Minimal slice of the real SDE meta DDL (see SchemaInitializer.SchemaDdl):
    // build_time is TEXT NOT NULL, so a "missing" build time means no row, not a
    // NULL value.
    private const string MetaSchema = """
        CREATE TABLE meta (
            id               INTEGER PRIMARY KEY CHECK (id = 1),
            build_time       TEXT NOT NULL,
            esi_sha256       TEXT,
            sde_sha256       TEXT,
            hoboleaks_sha256 TEXT,
            last_modified    TEXT,
            etag             TEXT
        );
        """;

    private static async Task<SqliteDatabase> NewMetaDbAsync(string? buildTime)
    {
        var db = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await db.ExecuteScriptAsync(MetaSchema);
        if (buildTime is not null)
        {
            await db.ExecuteNonQueryAsync(
                "INSERT INTO meta (id, build_time) VALUES (1, $bt)",
                new Dictionary<string, object?> { ["$bt"] = buildTime });
        }
        return db;
    }

    [Fact]
    public async Task GetBuildTimeAsync_returns_the_parsed_instant_for_a_valid_build_time()
    {
        await using var db = await NewMetaDbAsync("2026-06-12T00:11:31Z");
        var reader = new SdeMetaReader(db, NullLogger<SdeMetaReader>.Instance);

        var result = await reader.GetBuildTimeAsync();

        result.Should().Be(new DateTimeOffset(2026, 6, 12, 0, 11, 31, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetBuildTimeAsync_returns_null_when_no_meta_row_exists()
    {
        await using var db = await NewMetaDbAsync(buildTime: null);
        var reader = new SdeMetaReader(db, NullLogger<SdeMetaReader>.Instance);

        var result = await reader.GetBuildTimeAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetBuildTimeAsync_returns_null_and_logs_warning_when_build_time_is_malformed()
    {
        await using var db = await NewMetaDbAsync("not-a-date");
        var log = new CapturingLogger<SdeMetaReader>();
        var reader = new SdeMetaReader(db, log);

        var result = await reader.GetBuildTimeAsync();

        result.Should().BeNull();
        log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Ctor_throws_on_null_database()
    {
        Action act = () => _ = new SdeMetaReader(null!, NullLogger<SdeMetaReader>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }
}
