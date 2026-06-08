using System.Data.Common;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Storage;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Schema;
using EveDashboard.Stats.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Stats.Tests.Settings;

public class SqliteMarketStationCatalogTests
{
    private const string InMemory = "Data Source=:memory:";

    private const long JitaStationId = 60003760L;
    private const long Cj6MtStationId = 1049588174021L;
    private const string SeededSentinelKey = "non_sticky_station_defaults_seeded";

    private static SqliteDatabase OpenInMemory() =>
        new(InMemory, NullLogger<SqliteDatabase>.Instance);

    private static SqliteMarketStationCatalog NewCatalog(
        ISqliteDatabase db,
        ILogger<SqliteMarketStationCatalog>? log = null) =>
        new(db, log ?? NullLogger<SqliteMarketStationCatalog>.Instance);

    private static async Task<SqliteDatabase> NewSchemaAsync()
    {
        var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);
        return db;
    }

    private static MarketStation Sample(
        long stationId,
        string systemName = "Amarr",
        string stationName = "VIII - Emperor Family Academy",
        long? regionId = 10000043L,
        string? regionName = "Domain") =>
        new(stationId, systemName, stationName, regionId, regionName);

    // ---- Constructor / null-arg ---------------------------------------------------

    [Fact]
    public void Ctor_throws_on_null_database()
    {
        Action act = () => _ = new SqliteMarketStationCatalog(
            null!,
            NullLogger<SqliteMarketStationCatalog>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Ctor_throws_on_null_logger()
    {
        await using var db = OpenInMemory();
        Action act = () => _ = new SqliteMarketStationCatalog(db, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ---- GetAllAsync / GetAsync ---------------------------------------------------

    [Fact]
    public async Task GetAllAsync_on_empty_table_returns_empty_list()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);

        var rows = await catalog.GetAllAsync();

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_returns_rows_ordered_by_system_name_ascending()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);
        await catalog.AddAsync(Sample(2, systemName: "Zarzakh"));
        await catalog.AddAsync(Sample(1, systemName: "Amarr"));
        await catalog.AddAsync(Sample(3, systemName: "Jita"));

        var rows = await catalog.GetAllAsync();

        rows.Select(r => r.SystemName).Should().ContainInOrder("Amarr", "Jita", "Zarzakh");
    }

    [Fact]
    public async Task GetAsync_returns_null_when_station_id_not_present()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);

        var row = await catalog.GetAsync(42L);

        row.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_returns_row_when_station_id_present()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);
        var seed = Sample(42L);
        await catalog.AddAsync(seed);

        var row = await catalog.GetAsync(42L);

        row.Should().Be(seed);
    }

    // ---- AddAsync / UpdateAsync / RemoveAsync -------------------------------------

    [Fact]
    public async Task AddAsync_inserts_a_new_row_with_all_fields_round_tripping()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);
        var seed = new MarketStation(
            StationId: 100L,
            SystemName: "Hek",
            StationName: "VIII - Moon 12 - Boundless Creation Factory",
            RegionId: 10000042L,
            RegionName: "Metropolis");

        await catalog.AddAsync(seed);

        var row = await catalog.GetAsync(100L);
        row.Should().Be(seed);
    }

    [Fact]
    public async Task AddAsync_throws_DbException_on_duplicate_station_id()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);
        await catalog.AddAsync(Sample(7L));

        Func<Task> act = async () => await catalog.AddAsync(Sample(7L));

        await act.Should().ThrowAsync<DbException>();
    }

    [Fact]
    public async Task AddAsync_persists_null_region_id_and_region_name()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);
        var seed = new MarketStation(
            StationId: 9000L,
            SystemName: "WspaceSys",
            StationName: "Player Citadel",
            RegionId: null,
            RegionName: null);

        await catalog.AddAsync(seed);
        var row = await catalog.GetAsync(9000L);

        row.Should().Be(seed);
        row!.RegionId.Should().BeNull();
        row.RegionName.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_throws_on_null_station()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);

        Func<Task> act = async () => await catalog.AddAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateAsync_changes_existing_row_fields()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);
        await catalog.AddAsync(Sample(7L, systemName: "Old", stationName: "OldName"));

        var updated = new MarketStation(7L, "New", "NewName", 99L, "NewRegion");
        await catalog.UpdateAsync(updated);

        var row = await catalog.GetAsync(7L);
        row.Should().Be(updated);
    }

    [Fact]
    public async Task UpdateAsync_is_noop_when_station_id_absent()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);

        await catalog.UpdateAsync(Sample(404L));

        (await catalog.GetAsync(404L)).Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_throws_on_null_station()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);

        Func<Task> act = async () => await catalog.UpdateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RemoveAsync_deletes_existing_row()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);
        await catalog.AddAsync(Sample(7L));

        await catalog.RemoveAsync(7L);

        (await catalog.GetAsync(7L)).Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_is_noop_when_station_id_absent()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);

        Func<Task> act = async () => await catalog.RemoveAsync(404L);

        await act.Should().NotThrowAsync();
    }

    // ---- EnsureSeedDefaultsAsync -- the seeding-policy tests ----------------------

    [Fact]
    public async Task EnsureSeedDefaultsAsync_on_fresh_db_seeds_jita_and_cj6mt_and_writes_sentinel()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);

        await catalog.EnsureSeedDefaultsAsync();

        var jita = await catalog.GetAsync(JitaStationId);
        jita.Should().NotBeNull();
        jita!.SystemName.Should().Be("Jita");
        jita.RegionId.Should().Be(10000002L);

        var cj6 = await catalog.GetAsync(Cj6MtStationId);
        cj6.Should().NotBeNull();
        cj6!.SystemName.Should().Be("C-J6MT");
        cj6.RegionId.Should().Be(10000009L);

        var sentinel = await db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = $k",
            new Dictionary<string, object?> { ["$k"] = SeededSentinelKey });
        sentinel.Should().Be("1");
    }

    [Fact]
    public async Task EnsureSeedDefaultsAsync_re_seeds_jita_after_user_deletion()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);
        await catalog.EnsureSeedDefaultsAsync();
        await catalog.RemoveAsync(JitaStationId);
        (await catalog.GetAsync(JitaStationId)).Should().BeNull();

        await catalog.EnsureSeedDefaultsAsync();

        (await catalog.GetAsync(JitaStationId)).Should().NotBeNull();
    }

    [Fact]
    public async Task EnsureSeedDefaultsAsync_does_not_re_seed_cj6mt_after_user_deletion_when_sentinel_present()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);
        await catalog.EnsureSeedDefaultsAsync();
        await catalog.RemoveAsync(Cj6MtStationId);
        (await catalog.GetAsync(Cj6MtStationId)).Should().BeNull();

        await catalog.EnsureSeedDefaultsAsync();

        (await catalog.GetAsync(Cj6MtStationId)).Should().BeNull();
    }

    [Fact]
    public async Task EnsureSeedDefaultsAsync_is_idempotent_when_run_twice_back_to_back()
    {
        await using var db = await NewSchemaAsync();
        var catalog = NewCatalog(db);

        await catalog.EnsureSeedDefaultsAsync();
        await catalog.EnsureSeedDefaultsAsync();

        var rows = await catalog.GetAllAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.StationId).Should().BeEquivalentTo(new[]
        {
            JitaStationId,
            Cj6MtStationId,
        });
    }
}
