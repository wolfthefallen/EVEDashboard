using EveDashboard.Persistence.Storage;
using EveDashboard.Sde.Conversion;
using EveDashboard.Sde.Schema;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EveDashboard.Sde.Tests.Conversion;

public class EverefSdeFormatConverterTests
{
    private static string FixtureRoot =>
        Path.Combine(AppContext.BaseDirectory, "testdata", "sde-fixtures");

    private static SqliteDatabase OpenInMemory() =>
        new("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);

    private static EverefSdeFormatConverter NewConverter() =>
        new(NullLogger<EverefSdeFormatConverter>.Instance);

    [Fact]
    public async Task ImportAsync_creates_schema_if_missing()
    {
        await using var db = OpenInMemory();
        await NewConverter().ImportAsync(FixtureRoot, db);

        var tables = await db.QueryAsync(
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name",
            r => r.GetString(0));

        tables.Should().BeEquivalentTo(new[]
        {
            "categories", "groups", "meta", "type_materials", "types",
        });
    }

    [Fact]
    public async Task ImportAsync_imports_meta_row()
    {
        await using var db = OpenInMemory();
        await NewConverter().ImportAsync(FixtureRoot, db);

        var rowCount = await db.QueryScalarAsync<long>("SELECT COUNT(*) FROM meta");
        rowCount.Should().Be(1);

        var buildTime = await db.QueryScalarAsync<string>("SELECT build_time FROM meta WHERE id = 1");
        buildTime.Should().Be("2026-04-30T12:00:00Z");

        var esi = await db.QueryScalarAsync<string>("SELECT esi_sha256 FROM meta WHERE id = 1");
        esi.Should().Be("test-esi-sha-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    }

    [Fact]
    public async Task ImportAsync_skips_published_false_categories()
    {
        await using var db = OpenInMemory();
        await NewConverter().ImportAsync(FixtureRoot, db);

        var ids = await db.QueryAsync(
            "SELECT category_id FROM categories ORDER BY category_id",
            r => r.GetInt64(0));

        ids.Should().BeEquivalentTo(new long[] { 2, 4, 25 });
        ids.Should().NotContain(0);
    }

    [Fact]
    public async Task ImportAsync_skips_published_false_groups()
    {
        await using var db = OpenInMemory();
        await NewConverter().ImportAsync(FixtureRoot, db);

        var ids = await db.QueryAsync(
            "SELECT group_id FROM groups ORDER BY group_id",
            r => r.GetInt64(0));

        ids.Should().BeEquivalentTo(new long[] { 18, 462, 711, 1884 });
        ids.Should().NotContain(0);
    }

    [Fact]
    public async Task ImportAsync_skips_published_false_types()
    {
        await using var db = OpenInMemory();
        await NewConverter().ImportAsync(FixtureRoot, db);

        var typeCount = await db.QueryScalarAsync<long>("SELECT COUNT(*) FROM types");
        typeCount.Should().Be(8);   // 9 fixture rows minus type_id=0 (published=false)

        var hasZero = await db.QueryScalarAsync<long>("SELECT COUNT(*) FROM types WHERE type_id = 0");
        hasZero.Should().Be(0);
    }

    [Fact]
    public async Task ImportAsync_imports_type_materials_for_ore_records()
    {
        await using var db = OpenInMemory();
        await NewConverter().ImportAsync(FixtureRoot, db);

        // Veldspar (1230) → Tritanium (34) × 415
        var veldspar = await db.QueryAsync(
            "SELECT material_type_id, quantity FROM type_materials WHERE type_id = 1230 ORDER BY material_type_id",
            r => new { Mat = r.GetInt64(0), Qty = r.GetInt64(1) });
        veldspar.Should().HaveCount(1);
        veldspar[0].Mat.Should().Be(34);
        veldspar[0].Qty.Should().Be(415);

        // Bitumens (45492) → Pyerite (35) ×6000, Mexallon (36) ×400, Hydrocarbons (16633) ×65
        var bitumens = await db.QueryAsync(
            "SELECT material_type_id, quantity FROM type_materials WHERE type_id = 45492 ORDER BY material_type_id",
            r => new { Mat = r.GetInt64(0), Qty = r.GetInt64(1) });
        bitumens.Should().HaveCount(3);
        bitumens.Should().BeEquivalentTo(new[]
        {
            new { Mat = 35L,    Qty = 6000L },
            new { Mat = 36L,    Qty = 400L },
            new { Mat = 16633L, Qty = 65L },
        });
    }

    [Fact]
    public async Task ImportAsync_handles_type_with_no_type_materials_field()
    {
        await using var db = OpenInMemory();
        await NewConverter().ImportAsync(FixtureRoot, db);

        // type 34 (Tritanium) has NO type_materials field at all in the fixture.
        // Type row should still be present; no materials referencing it.
        var row = await db.QueryScalarAsync<long>("SELECT type_id FROM types WHERE type_id = 34");
        row.Should().Be(34);

        var matRows = await db.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM type_materials WHERE type_id = 34");
        matRows.Should().Be(0);
    }

    [Fact]
    public async Task ImportAsync_handles_type_with_empty_type_materials_object()
    {
        await using var db = OpenInMemory();
        await NewConverter().ImportAsync(FixtureRoot, db);

        // type 35 (Pyerite) has explicit empty type_materials: {}
        var matRows = await db.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM type_materials WHERE type_id = 35");
        matRows.Should().Be(0);
    }

    [Fact]
    public async Task ImportAsync_stores_null_icon_id_for_type_without_field()
    {
        await using var db = OpenInMemory();
        await NewConverter().ImportAsync(FixtureRoot, db);

        // type 99999 has no icon_id field — should be NULL in DB.
        var iconNull = await db.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM types WHERE type_id = 99999 AND icon_id IS NULL");
        iconNull.Should().Be(1);
    }

    [Fact]
    public async Task ImportAsync_round_trips_non_ASCII_name()
    {
        await using var db = OpenInMemory();
        await NewConverter().ImportAsync(FixtureRoot, db);

        var name = await db.QueryScalarAsync<string>(
            "SELECT name FROM types WHERE type_id = 99999");
        name.Should().Be("Café Mocha (test)");
    }

    [Fact]
    public async Task ImportAsync_is_idempotent_on_re_import()
    {
        await using var db = OpenInMemory();
        var converter = NewConverter();
        await converter.ImportAsync(FixtureRoot, db);
        await converter.ImportAsync(FixtureRoot, db);

        var counts = await db.QueryAsync(
            """
            SELECT 'categories' AS t, COUNT(*) FROM categories UNION ALL
            SELECT 'groups',          COUNT(*) FROM groups UNION ALL
            SELECT 'types',           COUNT(*) FROM types UNION ALL
            SELECT 'type_materials',  COUNT(*) FROM type_materials UNION ALL
            SELECT 'meta',            COUNT(*) FROM meta
            """,
            r => new { Table = r.GetString(0), Count = r.GetInt64(1) });

        counts.Should().Contain(x => x.Table == "categories"     && x.Count == 3);
        counts.Should().Contain(x => x.Table == "groups"         && x.Count == 4);
        counts.Should().Contain(x => x.Table == "types"          && x.Count == 8);
        counts.Should().Contain(x => x.Table == "type_materials" && x.Count == 4);
        counts.Should().Contain(x => x.Table == "meta"           && x.Count == 1);
    }

    [Fact]
    public async Task ImportAsync_throws_DirectoryNotFoundException_on_missing_folder()
    {
        await using var db = OpenInMemory();
        Func<Task> act = () => NewConverter().ImportAsync("/no/such/folder/here", db);
        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task ImportAsync_throws_FileNotFoundException_on_missing_required_file()
    {
        // Build a folder that has only meta.json and is missing types.json.
        var partial = Path.Combine(Path.GetTempPath(), $"sde-partial-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(partial);
            File.Copy(Path.Combine(FixtureRoot, "meta.json"), Path.Combine(partial, "meta.json"));
            File.Copy(Path.Combine(FixtureRoot, "categories.json"), Path.Combine(partial, "categories.json"));
            File.Copy(Path.Combine(FixtureRoot, "groups.json"), Path.Combine(partial, "groups.json"));
            // types.json deliberately omitted.

            await using var db = OpenInMemory();
            Func<Task> act = () => NewConverter().ImportAsync(partial, db);
            await act.Should().ThrowAsync<FileNotFoundException>()
                .Where(ex => ex.Message.Contains("types.json"));
        }
        finally
        {
            if (Directory.Exists(partial)) Directory.Delete(partial, recursive: true);
        }
    }

    [Fact]
    public async Task ImportAsync_skips_orphan_rows_whose_parents_were_filtered()
    {
        // Build a synthetic fixture set where:
        //  - category 99 is published=false (filtered)
        //  - group 990 is published=true but references unpublished category 99 (orphan)
        //  - type 9990 is published=true but references unpublished group 990 (orphan)
        //  - group 991 / type 9991 are valid (controls)
        //  - type 9991 has a type_material referencing type 7777 which is missing/unpublished (orphan material)
        //  - type 9991 has a type_material referencing type 9991 itself (valid: self-ref to a published type)
        var dir = Path.Combine(Path.GetTempPath(), $"sde-orphan-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "meta.json"),
                """{"build_time":"2026-04-30T00:00:00Z","esi":{"sha256":"a"},"sde":{"sha256":"b"},"hoboleaks":{"sha256":"c"}}""");
            File.WriteAllText(Path.Combine(dir, "categories.json"),
                """
                {
                  "99": { "category_id": 99, "name": {"en":"Unpublished"}, "published": false },
                  "100": { "category_id": 100, "name": {"en":"Published"},  "published": true }
                }
                """);
            File.WriteAllText(Path.Combine(dir, "groups.json"),
                """
                {
                  "990": { "group_id": 990, "category_id": 99,  "name": {"en":"OrphanGroup"}, "published": true },
                  "991": { "group_id": 991, "category_id": 100, "name": {"en":"GoodGroup"},   "published": true }
                }
                """);
            File.WriteAllText(Path.Combine(dir, "types.json"),
                """
                {
                  "9990": { "type_id": 9990, "group_id": 990, "name": {"en":"OrphanType"}, "published": true,
                            "volume": 1, "portion_size": 1, "is_ore": false },
                  "9991": { "type_id": 9991, "group_id": 991, "name": {"en":"GoodType"}, "published": true,
                            "volume": 1, "portion_size": 1, "is_ore": true,
                            "type_materials": {
                              "7777": { "material_type_id": 7777, "quantity": 100 },
                              "9991": { "material_type_id": 9991, "quantity": 1 }
                            } }
                }
                """);

            await using var db = OpenInMemory();
            await NewConverter().ImportAsync(dir, db);

            // Orphan group skipped, valid group present.
            var groupIds = await db.QueryAsync(
                "SELECT group_id FROM groups ORDER BY group_id",
                r => r.GetInt64(0));
            groupIds.Should().BeEquivalentTo(new long[] { 991 });

            // Orphan type skipped, valid type present.
            var typeIds = await db.QueryAsync(
                "SELECT type_id FROM types ORDER BY type_id",
                r => r.GetInt64(0));
            typeIds.Should().BeEquivalentTo(new long[] { 9991 });

            // Material edge to missing type 7777 is dropped; self-ref to 9991 is kept.
            var matEdges = await db.QueryAsync(
                "SELECT type_id, material_type_id, quantity FROM type_materials ORDER BY material_type_id",
                r => new { TypeId = r.GetInt64(0), MatId = r.GetInt64(1), Qty = r.GetInt64(2) });
            matEdges.Should().HaveCount(1);
            matEdges[0].Should().BeEquivalentTo(new { TypeId = 9991L, MatId = 9991L, Qty = 1L });
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ImportAsync_throws_on_null_target()
    {
        Func<Task> act = () => NewConverter().ImportAsync(FixtureRoot, null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("target");
    }

    [Fact]
    public async Task ImportAsync_UPSERTs_changed_values_on_re_import()
    {
        await using var db = OpenInMemory();
        var converter = NewConverter();
        await converter.ImportAsync(FixtureRoot, db);

        // Manually corrupt a row to simulate stale data in the existing DB.
        await db.ExecuteNonQueryAsync(
            "UPDATE types SET name = 'CORRUPTED', volume = 999.9 WHERE type_id = 1230");
        var corruptedName = await db.QueryScalarAsync<string>(
            "SELECT name FROM types WHERE type_id = 1230");
        corruptedName.Should().Be("CORRUPTED");

        // Re-import — UPSERT should overwrite the corruption with the JSON value.
        await converter.ImportAsync(FixtureRoot, db);

        var name = await db.QueryScalarAsync<string>(
            "SELECT name FROM types WHERE type_id = 1230");
        name.Should().Be("Veldspar");

        var volume = await db.QueryScalarAsync<double>(
            "SELECT volume FROM types WHERE type_id = 1230");
        volume.Should().BeApproximately(0.1, 1e-6);
    }

    [Fact]
    public async Task ImportAsync_reports_progress_per_table()
    {
        await using var db = OpenInMemory();
        var captured = new List<ImportProgress>();
        var progress = new Progress<ImportProgress>(p => captured.Add(p));

        await NewConverter().ImportAsync(FixtureRoot, db, progress);

        // IProgress<T> dispatches via SynchronizationContext / threadpool — give it a tick to flush.
        await Task.Delay(50);

        captured.Should().NotBeEmpty();
        captured.Select(p => p.CurrentTable).Should().Contain(new[]
        {
            "meta", "categories", "groups", "types", "type_materials",
        });
        captured.Select(p => p.RowsTotal).Should().BeInAscendingOrder();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ImportToOpenTransactionAsync — caller-owns-tx variant
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportToOpenTransactionAsync_persists_when_caller_commits()
    {
        await using var db = OpenInMemory();
        // Caller's responsibility — schema before transaction.
        await SchemaInitializer.EnsureSchemaAsync(db);

        await db.ExecuteNonQueryAsync("BEGIN TRANSACTION");
        await NewConverter().ImportToOpenTransactionAsync(FixtureRoot, db);
        await db.ExecuteNonQueryAsync("COMMIT");

        var typeCount = await db.QueryScalarAsync<long>("SELECT COUNT(*) FROM types");
        typeCount.Should().Be(8, "fixture has 8 published types and the COMMIT should persist them");
    }

    [Fact]
    public async Task ImportToOpenTransactionAsync_rolls_back_when_caller_rolls_back()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        await db.ExecuteNonQueryAsync("BEGIN TRANSACTION");
        await NewConverter().ImportToOpenTransactionAsync(FixtureRoot, db);
        await db.ExecuteNonQueryAsync("ROLLBACK");

        var typeCount = await db.QueryScalarAsync<long>("SELECT COUNT(*) FROM types");
        typeCount.Should().Be(0, "ROLLBACK by the caller should discard the in-flight import");
    }
}
