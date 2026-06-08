using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;
using EveDashboard.Persistence.Storage;
using EveDashboard.Sde.Lookup;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EveDashboard.Sde.Tests.Lookup;

public class SqliteSdeLookupTests
{
    private const string Schema = """
        CREATE TABLE types (
            type_id      INTEGER PRIMARY KEY,
            group_id     INTEGER NOT NULL,
            name         TEXT    NOT NULL,
            volume       REAL    NOT NULL,
            portion_size INTEGER NOT NULL,
            is_ore       INTEGER NOT NULL,
            icon_id      INTEGER
        );
        CREATE INDEX idx_types_name     ON types(name);
        CREATE INDEX idx_types_group_id ON types(group_id);
        CREATE INDEX idx_types_is_ore   ON types(is_ore);

        CREATE TABLE type_materials (
            type_id          INTEGER NOT NULL,
            material_type_id INTEGER NOT NULL,
            quantity         INTEGER NOT NULL,
            PRIMARY KEY (type_id, material_type_id)
        );
        CREATE INDEX idx_type_materials_type_id ON type_materials(type_id);
        """;

    // Hand-built seed: one of every shape the lookup must distinguish.
    //   1230  Veldspar       (asteroid ore;  is_ore=1, group 462)
    //  45492  Bitumens       (moon ore;      is_ore=1, group 1884)
    //  16262  Glacial Mass   (ice;           is_ore=1, group 465)
    //  30375  Fullerite-C28  (gas;           is_ore=0, group 711)
    //     34  Tritanium      (refined out;   is_ore=0, group 18)  -- MUST NOT be cached
    private const string SeedRows = """
        INSERT INTO types (type_id, group_id, name, volume, portion_size, is_ore) VALUES
            (1230,    462, 'Veldspar',      0.1,    100, 1),
            (45492,  1884, 'Bitumens',      1.2,    100, 1),
            (16262,   465, 'Glacial Mass',  1000.0, 100, 1),
            (30375,   711, 'Fullerite-C28', 1.0,      1, 0),
            (34,       18, 'Tritanium',     0.01,   100, 0),
            (16637,    -1, 'Tungsten',      0.01,     1, 0);

        -- Plan 18: type_materials seed.
        --   Veldspar  -> 415 Tritanium per portion
        --   Bitumens  -> 100 Tungsten + 50 Tritanium per portion
        --   Glacial Mass refines (intentionally absent — exercises empty-list path for an ore)
        --   Fullerite-C28 has no row (gas)
        INSERT INTO type_materials (type_id, material_type_id, quantity) VALUES
            (1230,  34,    415),
            (45492, 16637, 100),
            (45492, 34,    50);
        """;

    private static async Task<SqliteDatabase> SeededDbAsync()
    {
        var db = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await db.ExecuteScriptAsync(Schema);
        await db.ExecuteScriptAsync(SeedRows);
        return db;
    }

    [Fact]
    public async Task LoadAsync_loads_ore_and_gas_only_not_refined_outputs()
    {
        await using var db = await SeededDbAsync();
        using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);

        await lookup.LoadAsync();

        lookup.TryGetProduct(1230)!.Name.Should().Be("Veldspar");
        lookup.TryGetProduct(45492)!.Name.Should().Be("Bitumens");
        lookup.TryGetProduct(16262)!.Name.Should().Be("Glacial Mass");
        lookup.TryGetProduct(30375)!.Name.Should().Be("Fullerite-C28");

        lookup.TryGetProduct(34).Should().BeNull(
            "Tritanium is a mineral output (is_ore=0, group not 711) and must not be cached");
    }

    [Fact]
    public async Task TryGetProduct_returns_full_value_object()
    {
        await using var db = await SeededDbAsync();
        using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);
        await lookup.LoadAsync();

        var veldspar = lookup.TryGetProduct(1230);
        veldspar.Should().NotBeNull();
        veldspar!.Should().Be(new MiningProduct(1230, "Veldspar", 0.1, 100));
    }

    [Fact]
    public async Task TryGetTypeId_is_case_insensitive()
    {
        await using var db = await SeededDbAsync();
        using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);
        await lookup.LoadAsync();

        lookup.TryGetTypeId("Veldspar").Should().Be(1230L);
        lookup.TryGetTypeId("VELDSPAR").Should().Be(1230L);
        lookup.TryGetTypeId("veldspar").Should().Be(1230L);
        lookup.TryGetTypeId("VeLdSpAr").Should().Be(1230L);
    }

    [Fact]
    public async Task TryGetTypeId_unknown_returns_null()
    {
        await using var db = await SeededDbAsync();
        using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);
        await lookup.LoadAsync();

        lookup.TryGetTypeId("NotARealOre").Should().BeNull();
        lookup.TryGetTypeId("").Should().BeNull();
    }

    [Fact]
    public async Task TryGetProduct_unknown_typeid_returns_null()
    {
        await using var db = await SeededDbAsync();
        using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);
        await lookup.LoadAsync();

        lookup.TryGetProduct(999_999L).Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_rebuilds_from_changing_db()
    {
        await using var db = await SeededDbAsync();
        using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);

        await lookup.LoadAsync();
        lookup.TryGetTypeId("NewOre").Should().BeNull("not yet inserted");

        await db.ExecuteScriptAsync(
            "INSERT INTO types (type_id, group_id, name, volume, portion_size, is_ore) " +
            "VALUES (9999, 462, 'NewOre', 0.5, 100, 1);");

        await lookup.LoadAsync();
        lookup.TryGetTypeId("NewOre").Should().Be(9999L);
        lookup.TryGetProduct(9999)!.Name.Should().Be("NewOre");
    }

    [Fact]
    public async Task TryGet_called_before_LoadAsync_blocks_until_loaded()
    {
        await using var db = await SeededDbAsync();
        using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Kick off a lookup on a worker BEFORE LoadAsync — must block on _ready.Wait().
        var lookupTask = Task.Run(() => lookup.TryGetTypeId("Veldspar"), cts.Token);

        await Task.Delay(100, cts.Token);
        lookupTask.IsCompleted.Should().BeFalse(
            "the lookup must block until LoadAsync sets the ready gate");

        // Load — the worker unblocks and resolves.
        await lookup.LoadAsync(cts.Token);
        var result = await lookupTask;
        result.Should().Be(1230L);
    }

    [Fact]
    public async Task LoadAsync_serializes_concurrent_calls_cleanly()
    {
        await using var db = await SeededDbAsync();
        using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);

        var t1 = Task.Run(() => lookup.LoadAsync());
        var t2 = Task.Run(() => lookup.LoadAsync());
        await Task.WhenAll(t1, t2);

        lookup.TryGetTypeId("Veldspar").Should().Be(1230L);
        lookup.TryGetTypeId("Fullerite-C28").Should().Be(30375L);
    }

    [Fact]
    public async Task LoadAsync_honors_cancellation()
    {
        await using var db = await SeededDbAsync();
        using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => lookup.LoadAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Constructor_throws_on_null_db()
    {
        Action act = () => _ = new SqliteSdeLookup(null!, NullLogger<SqliteSdeLookup>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Constructor_throws_on_null_logger()
    {
        await using var db = await SeededDbAsync();
        Action act = () => _ = new SqliteSdeLookup(db, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task TryGetTypeId_throws_on_null_argument()
    {
        await using var db = await SeededDbAsync();
        using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);
        await lookup.LoadAsync();

        Action act = () => lookup.TryGetTypeId(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ---- Plan 18: type_materials surface --------------------------------------------

    [Fact]
    public async Task GetTypeMaterials_returns_expected_materials_for_an_ore_type()
    {
        await using var db = await SeededDbAsync();
        using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);
        await lookup.LoadAsync();

        var materials = lookup.GetTypeMaterials(1230L);
        materials.Should().BeEquivalentTo(new[]
        {
            new TypeMaterial(34L, 415L),
        });
    }

    [Fact]
    public async Task GetTypeMaterials_returns_multiple_materials_for_a_moon_ore()
    {
        await using var db = await SeededDbAsync();
        using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);
        await lookup.LoadAsync();

        var materials = lookup.GetTypeMaterials(45492L);
        materials.Should().BeEquivalentTo(new[]
        {
            new TypeMaterial(34L, 50L),
            new TypeMaterial(16637L, 100L),
        });
    }

    [Fact]
    public async Task GetTypeMaterials_returns_empty_for_a_gas_type()
    {
        await using var db = await SeededDbAsync();
        using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);
        await lookup.LoadAsync();

        lookup.GetTypeMaterials(30375L).Should().BeEmpty();
    }

    [Fact]
    public async Task GetTypeMaterials_returns_empty_for_an_unknown_type_id()
    {
        await using var db = await SeededDbAsync();
        using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);
        await lookup.LoadAsync();

        lookup.GetTypeMaterials(999_999L).Should().BeEmpty();
    }

    [Fact]
    public async Task GetTypeMaterials_blocks_on_pre_load_gate()
    {
        await using var db = await SeededDbAsync();
        using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var lookupTask = Task.Run(() => lookup.GetTypeMaterials(1230L), cts.Token);
        await Task.Delay(100, cts.Token);
        lookupTask.IsCompleted.Should().BeFalse(
            "the lookup must block until LoadAsync sets the ready gate");

        await lookup.LoadAsync(cts.Token);
        var result = await lookupTask;
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetRefinedMaterialTypeIds_returns_distinct_set()
    {
        await using var db = await SeededDbAsync();
        using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);
        await lookup.LoadAsync();

        // Veldspar -> 34, Bitumens -> 34 + 16637. Distinct = {34, 16637}.
        var refined = lookup.GetRefinedMaterialTypeIds();
        refined.Should().BeEquivalentTo(new long[] { 34L, 16637L });
    }
}
