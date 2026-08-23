using EveDashboard.Core.Sessions;
using EveDashboard.Core.Storage;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Fleets;
using EveDashboard.Stats.Schema;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Stats.Tests.Fleets;

public class SqliteFleetRepositoryTests
{
    private const string InMemory = "Data Source=:memory:";

    private static SqliteDatabase OpenInMemory() =>
        new(InMemory, NullLogger<SqliteDatabase>.Instance);

    private static SqliteFleetRepository NewRepo(
        ISqliteDatabase db,
        ILogger<SqliteFleetRepository>? log = null) =>
        new(db, log ?? NullLogger<SqliteFleetRepository>.Instance);

    private static async Task<SqliteDatabase> NewSchemaAsync()
    {
        var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);
        return db;
    }

    private static List<FleetMember> TwoMembers() => new()
    {
        new(2000000001, "Alice", PilotRole.Miner),
        new(2000000002, "Bob", PilotRole.Booster),
    };

    // ---- Round-trip ---------------------------------------------------------------

    [Fact]
    public async Task CreateFleetAsync_then_GetFleetsAsync_round_trips_fleet_and_members()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);
        var members = TwoMembers();

        var result = await repo.CreateFleetAsync("Alpha", members);

        result.Status.Should().Be(FleetCreateStatus.Created);
        result.FleetId.Should().NotBeNull();

        var fleets = await repo.GetFleetsAsync();

        fleets.Should().ContainSingle();
        var fleet = fleets[0];
        fleet.Name.Should().Be("Alpha");
        fleet.Members.Should().HaveCount(2);

        var alice = fleet.Members.Single(m => m.PilotId == 2000000001);
        alice.Name.Should().Be("Alice");
        alice.Role.Should().Be(PilotRole.Miner);

        var bob = fleet.Members.Single(m => m.PilotId == 2000000002);
        bob.Name.Should().Be("Bob");
        bob.Role.Should().Be(PilotRole.Booster);
    }

    // ---- Duplicate name -----------------------------------------------------------

    [Fact]
    public async Task CreateFleetAsync_rejects_duplicate_name()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var first = await repo.CreateFleetAsync(
            "Alpha",
            new List<FleetMember> { new(2000000001, "Alice", PilotRole.Miner) });
        first.Status.Should().Be(FleetCreateStatus.Created);

        var second = await repo.CreateFleetAsync(
            "Alpha",
            new List<FleetMember> { new(2000000099, "Carol", PilotRole.Cyno) });

        second.Status.Should().Be(FleetCreateStatus.DuplicateName);
        second.FleetId.Should().BeNull();

        var fleets = await repo.GetFleetsAsync();
        fleets.Should().ContainSingle();
    }

    // ---- Trim before save --------------------------------------

    [Fact]
    public async Task CreateFleetAsync_trims_name_before_save_so_padded_duplicate_is_rejected()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var first = await repo.CreateFleetAsync(
            "  Alpha  ",
            new List<FleetMember> { new(2000000001, "Alice", PilotRole.Miner) });
        first.Status.Should().Be(FleetCreateStatus.Created);

        var second = await repo.CreateFleetAsync(
            "Alpha",
            new List<FleetMember> { new(2000000002, "Bob", PilotRole.Booster) });

        second.Status.Should().Be(FleetCreateStatus.DuplicateName);
        second.FleetId.Should().BeNull();

        var fleets = await repo.GetFleetsAsync();
        fleets.Should().ContainSingle().Which.Name.Should().Be("Alpha");
    }

    // ---- Delete -------------------------------------------------------------------

    [Fact]
    public async Task DeleteFleetAsync_removes_fleet_and_its_members()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        var result = await repo.CreateFleetAsync("Alpha", TwoMembers());
        result.FleetId.Should().NotBeNull();

        await repo.DeleteFleetAsync(result.FleetId!);

        var fleets = await repo.GetFleetsAsync();
        fleets.Should().BeEmpty();

        var memberCount = await db.QueryAsync(
            "SELECT COUNT(*) FROM fleet_pilot", r => r.GetInt64(0), null, default);
        memberCount[0].Should().Be(0);
    }

    // ---- Ordering -----------------------------------------------------------------

    [Fact]
    public async Task GetFleetsAsync_returns_fleets_ordered_by_name()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.CreateFleetAsync(
            "Zulu", new List<FleetMember> { new(2000000001, "Alice", PilotRole.Miner) });
        await repo.CreateFleetAsync(
            "Alpha", new List<FleetMember> { new(2000000002, "Bob", PilotRole.Booster) });

        var fleets = await repo.GetFleetsAsync();

        fleets.Select(f => f.Name).Should().ContainInOrder("Alpha", "Zulu");
        fleets.Select(f => f.Name).Should().Equal("Alpha", "Zulu");
    }

    // ---- INSERT OR IGNORE pilot path ----------------------------------------------

    [Fact]
    public async Task CreateFleetAsync_inserts_new_pilot_into_pilot_table()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        await repo.CreateFleetAsync(
            "Alpha", new List<FleetMember> { new(2000000777, "Newbie", PilotRole.Miner) });

        var names = await db.QueryAsync(
            "SELECT name FROM pilot WHERE pilot_id = $id",
            r => r.GetString(0),
            new Dictionary<string, object?> { ["$id"] = 2000000777L },
            default);

        names.Should().ContainSingle().Which.Should().Be("Newbie");
    }

    // ---- Null-arg guards ----------------------------------------------------------

    [Fact]
    public async Task CreateFleetAsync_throws_on_null_name()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        Func<Task> act = async () => await repo.CreateFleetAsync(null!, TwoMembers());

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CreateFleetAsync_throws_on_null_members()
    {
        await using var db = await NewSchemaAsync();
        var repo = NewRepo(db);

        Func<Task> act = async () => await repo.CreateFleetAsync("Alpha", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
