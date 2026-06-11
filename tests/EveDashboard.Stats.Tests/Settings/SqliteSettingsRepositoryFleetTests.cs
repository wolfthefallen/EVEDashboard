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
}
