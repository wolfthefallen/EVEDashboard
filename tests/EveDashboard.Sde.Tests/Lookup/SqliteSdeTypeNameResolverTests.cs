namespace EveDashboard.Sde.Tests.Lookup;

using System.Threading.Tasks;
using EveDashboard.Persistence.Storage;
using EveDashboard.Sde.Lookup;
using EveDashboard.Sde.Schema;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class SqliteSdeTypeNameResolverTests
{
    private static async Task<SqliteDatabase> NewSdeAsync()
    {
        var db = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(db);
        await db.ExecuteScriptAsync(
            """
            INSERT INTO categories (category_id, name) VALUES (4, 'Material');
            INSERT INTO groups (group_id, category_id, name) VALUES (18, 4, 'Mineral');
            INSERT INTO types (type_id, group_id, name, volume, portion_size, is_ore)
            VALUES (34, 18, 'Tritanium', 0.01, 1, 0),
                   (35, 18, 'Pyerite',   0.01, 1, 0);
            """);
        return db;
    }

    private static SqliteSdeTypeNameResolver Resolver(SqliteDatabase db) =>
        new(db, NullLogger<SqliteSdeTypeNameResolver>.Instance);

    [Fact]
    public async Task Resolves_exact_name()
    {
        await using var db = await NewSdeAsync();
        (await Resolver(db).ResolveTypeIdAsync("Tritanium")).Should().Be(34L);
        (await Resolver(db).ResolveTypeIdAsync("Pyerite")).Should().Be(35L);
    }

    [Fact]
    public async Task Resolves_case_insensitively()
    {
        await using var db = await NewSdeAsync();
        (await Resolver(db).ResolveTypeIdAsync("tritanium")).Should().Be(34L);
        (await Resolver(db).ResolveTypeIdAsync("TRITANIUM")).Should().Be(34L);
    }

    [Fact]
    public async Task Trims_surrounding_whitespace()
    {
        await using var db = await NewSdeAsync();
        (await Resolver(db).ResolveTypeIdAsync("  Tritanium  ")).Should().Be(34L);
    }

    [Fact]
    public async Task Returns_null_for_unknown_or_blank_name()
    {
        await using var db = await NewSdeAsync();
        (await Resolver(db).ResolveTypeIdAsync("Nonexistent Widget")).Should().BeNull();
        (await Resolver(db).ResolveTypeIdAsync("   ")).Should().BeNull();
    }

    [Fact]
    public async Task Throws_on_null_name()
    {
        await using var db = await NewSdeAsync();
        var act = async () => await Resolver(db).ResolveTypeIdAsync(null!);
        await act.Should().ThrowAsync<System.ArgumentNullException>();
    }

    [Fact]
    public void Ctor_throws_on_null_args()
    {
        ((Action)(() => _ = new SqliteSdeTypeNameResolver(null!, NullLogger<SqliteSdeTypeNameResolver>.Instance)))
            .Should().Throw<ArgumentNullException>();
    }
}
