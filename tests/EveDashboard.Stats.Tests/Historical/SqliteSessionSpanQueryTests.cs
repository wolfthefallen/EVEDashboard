using System.Globalization;
using EveDashboard.Core.Sessions;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Historical;
using EveDashboard.Stats.Schema;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Stats.Tests.Historical;

public class SqliteSessionSpanQueryTests
{
    private const string InMemory = "Data Source=:memory:";
    private static readonly DateTimeOffset T0 = new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);

    private static async Task<SqliteDatabase> NewSchemaAsync()
    {
        var db = new SqliteDatabase(InMemory, NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(db);
        return db;
    }

    private static string Iso(DateTimeOffset t) => t.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static async Task<string> SeedSessionAsync(
        SqliteDatabase db, string name, (int StartMin, int? StopMin, bool Pause)[] times, params (long Id, string Role)[] pilots)
    {
        var sid = SessionId.Compute(name, Iso(T0.AddMinutes(times[0].StartMin)));
        await db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($sid, $n, NULL)",
            new Dictionary<string, object?> { ["$sid"] = sid, ["$n"] = name });
        foreach (var (startMin, stopMin, pause) in times)
        {
            await db.ExecuteNonQueryAsync(
                "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) VALUES ($sid, $s, $e, $p)",
                new Dictionary<string, object?>
                {
                    ["$sid"] = sid,
                    ["$s"] = Iso(T0.AddMinutes(startMin)),
                    ["$e"] = stopMin is null ? null : Iso(T0.AddMinutes(stopMin.Value)),
                    ["$p"] = pause ? 1 : 0,
                });
        }
        foreach (var (id, role) in pilots)
        {
            await db.ExecuteNonQueryAsync(
                "INSERT OR IGNORE INTO pilot (pilot_id, name) VALUES ($pid, $n)",
                new Dictionary<string, object?> { ["$pid"] = id, ["$n"] = $"Pilot {id}" });
            await db.ExecuteNonQueryAsync(
                "INSERT INTO session_pilot (session_id, pilot_id, role) VALUES ($sid, $pid, $r)",
                new Dictionary<string, object?> { ["$sid"] = sid, ["$pid"] = id, ["$r"] = role });
        }
        return sid;
    }

    [Fact]
    public async Task GetOverlappingAsync_returns_the_full_span_including_pauses_with_pilots_and_roles()
    {
        await using var db = await NewSchemaAsync();
        var sid = await SeedSessionAsync(db, "Run",
            new[] { (0, (int?)10, false), (10, 25, true), (25, 40, false) },
            (1L, "Miner"), (2L, "Booster"));
        var query = new SqliteSessionSpanQuery(db, NullLogger<SqliteSessionSpanQuery>.Instance);

        var spans = await query.GetOverlappingAsync(T0.AddMinutes(15), T0.AddMinutes(16));

        var span = spans.Should().ContainSingle().Subject;
        span.SessionId.Should().Be(sid);
        span.Name.Should().Be("Run");
        span.Start.Should().Be(T0);
        span.Stop.Should().Be(T0.AddMinutes(40), "the span covers pause rows too");
        span.Pilots.Select(p => (p.PilotId, p.Name, p.Role)).Should().Equal((1L, "Pilot 1", PilotRole.Miner), (2L, "Pilot 2", PilotRole.Booster));
    }

    [Fact]
    public async Task GetOverlappingAsync_excludes_sessions_outside_the_window_and_keeps_edge_touches()
    {
        await using var db = await NewSchemaAsync();
        await SeedSessionAsync(db, "Before",     new[] { (-30, (int?)-10, false) }, (1L, "Miner"));
        await SeedSessionAsync(db, "TouchStart", new[] { (-10, (int?)0, false) },   (1L, "Miner"));
        await SeedSessionAsync(db, "Inside",     new[] { (10, (int?)20, false) },   (1L, "Miner"));
        await SeedSessionAsync(db, "TouchStop",  new[] { (60, (int?)70, false) },   (1L, "Miner"));
        await SeedSessionAsync(db, "After",      new[] { (61, (int?)90, false) },   (1L, "Miner"));
        var query = new SqliteSessionSpanQuery(db, NullLogger<SqliteSessionSpanQuery>.Instance);

        var spans = await query.GetOverlappingAsync(T0, T0.AddMinutes(60));

        spans.Select(s => s.Name).Should().Equal("TouchStart", "Inside", "TouchStop");
    }

    [Fact]
    public async Task GetOverlappingAsync_skips_archived_sessions()
    {
        await using var db = await NewSchemaAsync();
        var sid = await SeedSessionAsync(db, "Folded", new[] { (10, (int?)20, false) }, (1L, "Miner"));
        await db.ExecuteNonQueryAsync("UPDATE session SET archived = 1 WHERE session_id = $sid", new Dictionary<string, object?> { ["$sid"] = sid });
        var query = new SqliteSessionSpanQuery(db, NullLogger<SqliteSessionSpanQuery>.Instance);

        (await query.GetOverlappingAsync(T0, T0.AddMinutes(60))).Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverlappingAsync_extends_an_in_flight_session_to_asOf()
    {
        await using var db = await NewSchemaAsync();
        await SeedSessionAsync(db, "Live", new[] { (0, (int?)10, false), (10, null, false) }, (1L, "Miner"));
        var query = new SqliteSessionSpanQuery(db, NullLogger<SqliteSessionSpanQuery>.Instance);

        var spans = await query.GetOverlappingAsync(T0.AddMinutes(100), T0.AddMinutes(110), asOf: T0.AddMinutes(120));

        spans.Should().ContainSingle().Which.Stop.Should().Be(T0.AddMinutes(120));
    }

    [Fact]
    public async Task GetOverlappingAsync_ignores_a_session_with_no_session_time_rows()
    {
        await using var db = await NewSchemaAsync();
        await db.ExecuteNonQueryAsync("INSERT INTO session (session_id, name, notes) VALUES ('nospan', 'Empty', NULL)");
        var query = new SqliteSessionSpanQuery(db, NullLogger<SqliteSessionSpanQuery>.Instance);

        (await query.GetOverlappingAsync(T0.AddYears(-10), T0.AddYears(10))).Should().BeEmpty();
    }

    [Fact]
    public async Task GetSpansAsync_returns_only_the_requested_non_archived_sessions_in_start_order()
    {
        await using var db = await NewSchemaAsync();
        var late = await SeedSessionAsync(db, "Late", new[] { (40, (int?)50, false) }, (1L, "Miner"));
        var early = await SeedSessionAsync(db, "Early", new[] { (0, (int?)10, false) }, (2L, "Booster"));
        var other = await SeedSessionAsync(db, "Other", new[] { (20, (int?)30, false) }, (1L, "Miner"));
        var folded = await SeedSessionAsync(db, "Folded", new[] { (60, (int?)70, false) }, (1L, "Miner"));
        await db.ExecuteNonQueryAsync("UPDATE session SET archived = 1 WHERE session_id = $sid", new Dictionary<string, object?> { ["$sid"] = folded });
        var query = new SqliteSessionSpanQuery(db, NullLogger<SqliteSessionSpanQuery>.Instance);

        var spans = await query.GetSpansAsync(new[] { late, early, folded, "missing" });

        spans.Select(s => s.SessionId).Should().Equal(early, late);
        spans[0].Pilots.Should().ContainSingle().Which.Role.Should().Be(PilotRole.Booster);
        _ = other;
    }

    [Fact]
    public async Task GetSpansAsync_rejects_null()
    {
        await using var db = await NewSchemaAsync();
        var query = new SqliteSessionSpanQuery(db, NullLogger<SqliteSessionSpanQuery>.Instance);
        var act = async () => await query.GetSpansAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetAllAsync_returns_every_non_archived_span_with_pilots_and_roles()
    {
        await using var db = await NewSchemaAsync();
        var early = await SeedSessionAsync(db, "Early", new[] { (0, (int?)10, false) }, (1L, "Booster"));
        var late = await SeedSessionAsync(db, "Late", new[] { (100, (int?)120, false) }, (2L, "Miner"));
        var folded = await SeedSessionAsync(db, "Folded", new[] { (200, (int?)210, false) }, (1L, "Miner"));
        await db.ExecuteNonQueryAsync(
            "UPDATE session SET archived = 1 WHERE session_id = $sid",
            new Dictionary<string, object?> { ["$sid"] = folded });
        var query = new SqliteSessionSpanQuery(db, NullLogger<SqliteSessionSpanQuery>.Instance);

        var spans = await query.GetAllAsync();

        spans.Select(s => s.SessionId).Should().Equal(
            new[] { early, late }, "archived sessions stay invisible");
        spans[0].Start.Should().Be(T0);
        spans[0].Pilots.Should().ContainSingle().Which.Role.Should().Be(PilotRole.Booster);
        spans[1].Stop.Should().Be(T0.AddMinutes(120));
    }

    [Fact]
    public async Task GetAllAsync_extends_an_in_flight_session_to_the_asOf_instant()
    {
        await using var db = await NewSchemaAsync();
        await SeedSessionAsync(db, "Running", new[] { (0, (int?)10, false), (10, null, false) }, (1L, "Miner"));
        var query = new SqliteSessionSpanQuery(db, NullLogger<SqliteSessionSpanQuery>.Instance);

        var spans = await query.GetAllAsync(asOf: T0.AddMinutes(90));

        spans.Should().ContainSingle().Which.Stop.Should().Be(T0.AddMinutes(90));
    }

    [Fact]
    public async Task GetAllAsync_returns_nothing_for_an_empty_store()
    {
        await using var db = await NewSchemaAsync();
        var query = new SqliteSessionSpanQuery(db, NullLogger<SqliteSessionSpanQuery>.Instance);

        (await query.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public void Constructor_rejects_nulls()
    {
        using var db = new SqliteDatabase(InMemory, NullLogger<SqliteDatabase>.Instance);
        var a = () => new SqliteSessionSpanQuery(null!, NullLogger<SqliteSessionSpanQuery>.Instance);
        var b = () => new SqliteSessionSpanQuery(db, null!);
        a.Should().Throw<ArgumentNullException>();
        b.Should().Throw<ArgumentNullException>();
    }
}
