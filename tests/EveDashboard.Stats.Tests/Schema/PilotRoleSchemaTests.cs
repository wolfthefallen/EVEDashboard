using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Schema;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Stats.Tests.Schema;

/// <summary>
/// Verify the <c>role</c> columns on <c>session_pilot</c> and
/// <c>log</c>, plus the <c>idx_session_pilot_pilot</c> index that supports
/// the default-to-last-role lookup.
/// </summary>
public class PilotRoleSchemaTests
{
    private static SqliteDatabase OpenInMemory() =>
        new("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);

    [Fact]
    public async Task session_pilot_has_role_column_with_default_Miner()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        var cols = await db.QueryAsync(
            "PRAGMA table_info(session_pilot)",
            r => new
            {
                Name      = r.GetString(1),
                Type      = r.GetString(2),
                NotNull   = r.GetInt32(3),
                DfltValue = r.IsDBNull(4) ? null : r.GetString(4),
            });

        var role = cols.FirstOrDefault(c => c.Name == "role");
        role.Should().NotBeNull("session_pilot.role must exist");
        role!.Type.Should().Be("TEXT");
        role.NotNull.Should().Be(1);
        role.DfltValue.Should().Be("'Miner'");
    }

    [Fact]
    public async Task log_has_role_column_with_default_Miner()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        var cols = await db.QueryAsync(
            "PRAGMA table_info(log)",
            r => new
            {
                Name      = r.GetString(1),
                Type      = r.GetString(2),
                NotNull   = r.GetInt32(3),
                DfltValue = r.IsDBNull(4) ? null : r.GetString(4),
            });

        var role = cols.FirstOrDefault(c => c.Name == "role");
        role.Should().NotBeNull("log.role must exist");
        role!.Type.Should().Be("TEXT");
        role.NotNull.Should().Be(1);
        role.DfltValue.Should().Be("'Miner'");
    }

    [Fact]
    public async Task idx_session_pilot_pilot_index_exists()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        var indexes = await db.QueryAsync(
            "PRAGMA index_list(session_pilot)",
            r => r.GetString(1));

        indexes.Should().Contain("idx_session_pilot_pilot");
    }

    [Fact]
    public async Task session_pilot_role_CHECK_rejects_unknown_role()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        // Seed parents so FKs pass; the only thing we want to fail is the role CHECK.
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name) VALUES ('1', 'test')");
        await db.ExecuteNonQueryAsync(
            "INSERT INTO pilot (pilot_id, name) VALUES (1, 'TestPilot')");

        Func<Task> act = async () => await db.ExecuteNonQueryAsync(
            "INSERT INTO session_pilot (session_id, pilot_id, role) VALUES (1, 1, 'Hauler')");

        await act.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task log_role_CHECK_rejects_unknown_role()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        await db.ExecuteNonQueryAsync("INSERT INTO session (session_id, name) VALUES ('1', 'test')");
        await db.ExecuteNonQueryAsync("INSERT INTO pilot (pilot_id, name) VALUES (1, 'TestPilot')");

        Func<Task> act = async () => await db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, role) " +
            "VALUES (1, 1, '2026-05-01T00:00:00.000Z', 'Tick', 'Hauler')");

        await act.Should().ThrowAsync<SqliteException>();
    }

    [Theory]
    [InlineData("Miner")]
    [InlineData("Booster")]
    [InlineData("Cyno")]
    [InlineData("Combat")]
    public async Task session_pilot_accepts_all_four_canonical_roles(string role)
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        await db.ExecuteNonQueryAsync("INSERT INTO session (session_id, name) VALUES ('1', 'test')");
        await db.ExecuteNonQueryAsync("INSERT INTO pilot (pilot_id, name) VALUES (1, 'TestPilot')");

        Func<Task> act = async () => await db.ExecuteNonQueryAsync(
            "INSERT INTO session_pilot (session_id, pilot_id, role) VALUES (1, 1, $r)",
            new Dictionary<string, object?> { ["$r"] = role });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task session_pilot_INSERT_without_role_defaults_to_Miner()
    {
        await using var db = OpenInMemory();
        await SchemaInitializer.EnsureSchemaAsync(db);

        await db.ExecuteNonQueryAsync("INSERT INTO session (session_id, name) VALUES ('1', 'test')");
        await db.ExecuteNonQueryAsync("INSERT INTO pilot (pilot_id, name) VALUES (1, 'TestPilot')");
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session_pilot (session_id, pilot_id) VALUES (1, 1)");

        var role = await db.QueryScalarAsync<string>(
            "SELECT role FROM session_pilot WHERE session_id = 1 AND pilot_id = 1");
        role.Should().Be("Miner");
    }
}
