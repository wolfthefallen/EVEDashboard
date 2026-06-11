using EveDashboard.Core.Sessions;
using EveDashboard.Core.Storage;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Schema;
using EveDashboard.Stats.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Stats.Tests.Settings;

public class SqliteSettingsRepositoryFleetTests
{
    private const string InMemory = "Data Source=:memory:";

    private static SqliteDatabase OpenInMemory() =>
        new(InMemory, NullLogger<SqliteDatabase>.Instance);

    private static SqliteSettingsRepository NewRepo(
        ISqliteDatabase db,
        ILogger<SqliteSettingsRepository>? log = null) =>
        new(db, log ?? NullLogger<SqliteSettingsRepository>.Instance);

    private static async Task<SqliteDatabase> NewSchemaAsync()
    {
        var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);
        return db;
    }

    // ---- DefaultFleetId -----------------------------------------------------------

    [Fact]
    public async Task GetDefaultFleetIdAsync_returns_null_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var id = await repo.GetDefaultFleetIdAsync();

        id.Should().BeNull();
    }

    [Fact]
    public async Task SetDefaultFleetIdAsync_then_GetDefaultFleetIdAsync_round_trips_id()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetDefaultFleetIdAsync("fleet-abc123");
        var id = await repo.GetDefaultFleetIdAsync();

        id.Should().Be("fleet-abc123");
    }

    [Fact]
    public async Task SetDefaultFleetIdAsync_null_clears_the_stored_id()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetDefaultFleetIdAsync("fleet-abc123");
        await repo.SetDefaultFleetIdAsync(null);
        var id = await repo.GetDefaultFleetIdAsync();

        id.Should().BeNull();
    }

    [Fact]
    public async Task SetDefaultFleetIdAsync_raises_SettingChanged_with_key_default_fleet_id()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetDefaultFleetIdAsync("fleet-abc123");

        keys.Should().ContainSingle().Which.Should().Be("default_fleet_id");
    }

    // ---- PilotSortOrder -----------------------------------------------------------

    [Fact]
    public async Task GetPilotSortOrderAsync_returns_last_active_newest_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var order = await repo.GetPilotSortOrderAsync();

        order.Should().Be(PilotSortOrder.LastActiveNewest);
    }

    [Theory]
    [InlineData(PilotSortOrder.NameAsc)]
    [InlineData(PilotSortOrder.LastActiveOldest)]
    [InlineData(PilotSortOrder.NameDesc)]
    public async Task SetPilotSortOrderAsync_then_GetPilotSortOrderAsync_round_trips(PilotSortOrder order)
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetPilotSortOrderAsync(order);
        var roundTrip = await repo.GetPilotSortOrderAsync();

        roundTrip.Should().Be(order);
    }

    [Fact]
    public async Task SetPilotSortOrderAsync_raises_SettingChanged_with_key_pilot_sort_order()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetPilotSortOrderAsync(PilotSortOrder.NameAsc);

        keys.Should().ContainSingle().Which.Should().Be("pilot_sort_order");
    }

    // ---- Plan 43: AutoShowRefinedMaterials ----------------------------------------

    [Fact]
    public async Task GetAutoShowRefinedMaterialsAsync_returns_true_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var enabled = await repo.GetAutoShowRefinedMaterialsAsync();

        enabled.Should().BeTrue("auto-show defaults to true when never persisted");
    }

    [Fact]
    public async Task SetAutoShowRefinedMaterialsAsync_then_GetAutoShowRefinedMaterialsAsync_round_trips_false()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.SetAutoShowRefinedMaterialsAsync(false);
        var enabled = await repo.GetAutoShowRefinedMaterialsAsync();

        enabled.Should().BeFalse();
    }

    [Fact]
    public async Task SetAutoShowRefinedMaterialsAsync_raises_SettingChanged_with_key_auto_show_refined_materials()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetAutoShowRefinedMaterialsAsync(false);

        keys.Should().ContainSingle().Which.Should().Be("auto_show_refined_materials");
    }

    // ---- Plan 43: RefinedMaterialsVisibility (session scope, scopeKey "") ----------

    [Fact]
    public async Task GetRefinedMaterialsVisibilityAsync_returns_empty_when_key_missing()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var map = await repo.GetRefinedMaterialsVisibilityAsync("");

        map.Should().BeEmpty();
    }

    [Fact]
    public async Task SetRefinedMaterialsVisibilityAsync_then_GetRefinedMaterialsVisibilityAsync_round_trips_map()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var visibility = new Dictionary<long, bool> { [34] = false, [35] = true };

        await repo.SetRefinedMaterialsVisibilityAsync("", visibility);
        var map = await repo.GetRefinedMaterialsVisibilityAsync("");

        map.Should().BeEquivalentTo(visibility);
    }

    [Fact]
    public async Task SetRefinedMaterialsVisibilityAsync_session_scope_raises_SettingChanged_with_unscoped_key()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetRefinedMaterialsVisibilityAsync("", new Dictionary<long, bool> { [34] = false });

        keys.Should().ContainSingle().Which.Should().Be("refined_materials_visibility");
    }

    // ---- Plan 43: RefinedMaterialsVisibility (per-pilot scope) ---------------------

    [Fact]
    public async Task SetRefinedMaterialsVisibilityAsync_per_pilot_scope_raises_SettingChanged_with_composed_key()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var keys = new List<string>();
        repo.SettingChanged += (_, e) => keys.Add(e.Key);

        await repo.SetRefinedMaterialsVisibilityAsync("12345", new Dictionary<long, bool> { [34] = false });

        keys.Should().ContainSingle().Which.Should().Be("refined_materials_visibility.12345");
    }

    [Fact]
    public async Task RefinedMaterialsVisibility_per_pilot_scope_round_trips_independently_of_session_scope()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var sessionScope = new Dictionary<long, bool> { [34] = false };
        var pilotScope   = new Dictionary<long, bool> { [35] = false, [36] = true };

        await repo.SetRefinedMaterialsVisibilityAsync("", sessionScope);
        await repo.SetRefinedMaterialsVisibilityAsync("12345", pilotScope);

        (await repo.GetRefinedMaterialsVisibilityAsync("")).Should().BeEquivalentTo(sessionScope,
            "the session scope is untouched by the per-pilot write");
        (await repo.GetRefinedMaterialsVisibilityAsync("12345")).Should().BeEquivalentTo(pilotScope);
    }
}
