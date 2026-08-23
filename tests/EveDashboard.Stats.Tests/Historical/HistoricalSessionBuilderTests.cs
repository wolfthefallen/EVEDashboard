using System.Globalization;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Sessions.Historical;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Historical;
using EveDashboard.Stats.Schema;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Stats.Tests.Historical;

public class HistoricalSessionBuilderTests
{
    private const string InMemory = "Data Source=:memory:";
    private const long Veldspar = 1230L;
    private static readonly DateTimeOffset T0 = new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset M(int minutes) => T0.AddMinutes(minutes);
    private static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static readonly IReadOnlyList<FleetMember> Roster = new[]
    {
        new FleetMember(1, "Asgar", PilotRole.Miner),
        new FleetMember(2, "Bjorn", PilotRole.Booster),
    };

    private static MiningEvent Tick(long pilot, int minute, long? typeId = Veldspar, long amount = 100) =>
        new(MiningEventKind.Tick, M(minute), pilot, "Veldspar", typeId, amount);

    private static MiningEvent Residue(long pilot, int minute) =>
        new(MiningEventKind.Residue, M(minute), pilot, "", null, 7);

    private static async Task<SqliteDatabase> NewSchemaAsync()
    {
        var db = new SqliteDatabase(InMemory, NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(db);
        return db;
    }

    private static HistoricalSessionBuilder NewBuilder(SqliteDatabase db) =>
        new(db, new StubSde(), NullLogger<HistoricalSessionBuilder>.Instance);

    private static HistoricalSessionRequest Request(
        IReadOnlyList<MiningEvent> events,
        IReadOnlyList<string>? merge = null,
        IReadOnlyList<PilotExclusion>? exclusions = null,
        int startMin = 0, int stopMin = 60, string name = "Rebuilt", IReadOnlyList<FleetMember>? roster = null) =>
        new(name, "notes", M(startMin), M(stopMin), roster ?? Roster, events,
            merge ?? Array.Empty<string>(), exclusions ?? Array.Empty<PilotExclusion>(), IdleMinutes: 5);

    /// <summary>Seed a finished saved session the way a live run leaves it.</summary>
    private static async Task<string> SeedSavedSessionAsync(
        SqliteDatabase db, string name, int startMin, int stopMin, (int Start, int Stop, bool Pause)[] times,
        (long Pilot, string Role)[] pilots, (long Pilot, int Minute)[] ticks)
    {
        var sid = SessionId.Compute(name, Iso(M(startMin)));
        await db.ExecuteNonQueryAsync("INSERT INTO session (session_id, name, notes) VALUES ($sid, $n, NULL)",
            new Dictionary<string, object?> { ["$sid"] = sid, ["$n"] = name });
        foreach (var (s, e, p) in times)
        {
            await db.ExecuteNonQueryAsync(
                "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) VALUES ($sid, $s, $e, $p)",
                new Dictionary<string, object?> { ["$sid"] = sid, ["$s"] = Iso(M(s)), ["$e"] = Iso(M(e)), ["$p"] = p ? 1 : 0 });
        }
        foreach (var (pilot, role) in pilots)
        {
            await db.ExecuteNonQueryAsync("INSERT OR IGNORE INTO pilot (pilot_id, name) VALUES ($pid, $n)",
                new Dictionary<string, object?> { ["$pid"] = pilot, ["$n"] = $"Pilot {pilot}" });
            await db.ExecuteNonQueryAsync("INSERT INTO session_pilot (session_id, pilot_id, role) VALUES ($sid, $pid, $r)",
                new Dictionary<string, object?> { ["$sid"] = sid, ["$pid"] = pilot, ["$r"] = role });
        }
        foreach (var (pilot, minute) in ticks)
        {
            await db.ExecuteNonQueryAsync(
                "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3, role) VALUES ($sid, $pid, $ts, 'Tick', 1230, 50, 5.0, 'Miner')",
                new Dictionary<string, object?> { ["$sid"] = sid, ["$pid"] = pilot, ["$ts"] = Iso(M(minute)) });
        }
        _ = stopMin;
        return sid;
    }

    private static async Task<List<(long Pilot, string Ts, string Kind, long? Type, long? Units, double? Vol, string Role)>> LogRowsAsync(SqliteDatabase db, string sid) =>
        (await db.QueryAsync(
            "SELECT pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3, role FROM log WHERE session_id = $sid ORDER BY eve_timestamp, log_id",
            static r => (r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? (long?)null : r.GetInt64(3), r.IsDBNull(4) ? (long?)null : r.GetInt64(4), r.IsDBNull(5) ? (double?)null : r.GetDouble(5), r.GetString(6)),
            new Dictionary<string, object?> { ["$sid"] = sid })).ToList();

    private static async Task<List<(int Start, int Stop, bool Pause)>> TimelineAsync(SqliteDatabase db, string sid) =>
        (await db.QueryAsync(
            "SELECT start_time, stop_time, is_pause FROM session_time WHERE session_id = $sid ORDER BY start_time",
            r => ((int)(DateTimeOffset.Parse(r.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal) - T0).TotalMinutes,
                  (int)(DateTimeOffset.Parse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal) - T0).TotalMinutes,
                  r.GetInt64(2) != 0),
            new Dictionary<string, object?> { ["$sid"] = sid })).ToList();

    [Fact]
    public async Task BuildAsync_from_parsed_events_writes_session_pilots_log_rows_and_timeline()
    {
        await using var db = await NewSchemaAsync();
        var events = new[] { Tick(1, 10), Tick(2, 12, amount: 30), Tick(1, 14) };

        var result = await NewBuilder(db).BuildAsync(Request(events));

        result.SessionId.Should().Be(SessionId.Compute("Rebuilt", Iso(M(0))));
        result.ParsedLogRows.Should().Be(3);
        result.CopiedLogRows.Should().Be(0);
        result.ArchivedSessionIds.Should().BeEmpty();
        (result.EffectiveStart, result.EffectiveStop).Should().Be((M(0), M(60)));

        var session = await db.QueryAsync("SELECT name, notes, archived FROM session WHERE session_id = $sid",
            static r => (r.GetString(0), r.GetString(1), r.GetInt64(2)), new Dictionary<string, object?> { ["$sid"] = result.SessionId });
        session.Should().ContainSingle().Which.Should().Be(("Rebuilt", "notes", 0L));

        var pilots = await db.QueryAsync("SELECT sp.pilot_id, p.name, sp.role FROM session_pilot sp JOIN pilot p ON p.pilot_id = sp.pilot_id WHERE sp.session_id = $sid ORDER BY sp.pilot_id",
            static r => (r.GetInt64(0), r.GetString(1), r.GetString(2)), new Dictionary<string, object?> { ["$sid"] = result.SessionId });
        pilots.Should().Equal((1L, "Asgar", "Miner"), (2L, "Bjorn", "Booster"));

        var rows = await LogRowsAsync(db, result.SessionId);
        rows.Should().HaveCount(3);
        rows[0].Should().Be((1L, Iso(M(10)), "Tick", Veldspar, 100L, 10.0, "Miner"));
        rows[1].Should().Be((2L, Iso(M(12)), "Tick", Veldspar, 30L, 3.0, "Booster"));

        (await TimelineAsync(db, result.SessionId)).Should().Equal((0, 10, true), (10, 19, false), (19, 60, true));
        result.ActiveIntervals.Should().Be(1);
        result.PauseIntervals.Should().Be(2);
    }

    [Fact]
    public async Task BuildAsync_drops_events_inside_an_exclusion_but_keeps_the_rest()
    {
        await using var db = await NewSchemaAsync();
        var events = new[] { Tick(1, 10), Tick(1, 20), Tick(2, 20) };
        var exclusions = new[] { new PilotExclusion(1, M(15), M(25), "declined") };

        var result = await NewBuilder(db).BuildAsync(Request(events, exclusions: exclusions));

        result.ParsedLogRows.Should().Be(2);
        result.DroppedByOwnership.Should().Be(1);
        (await LogRowsAsync(db, result.SessionId)).Select(r => (r.Pilot, r.Ts)).Should().Equal((1L, Iso(M(10))), (2L, Iso(M(20))));
    }

    [Fact]
    public async Task BuildAsync_drops_unknown_ore_ticks_and_off_roster_pilots_but_keeps_residue()
    {
        await using var db = await NewSchemaAsync();
        var events = new[] { Tick(1, 10, typeId: null), Residue(1, 11), Tick(99, 12) };

        var result = await NewBuilder(db).BuildAsync(Request(events));

        result.DroppedUnknownType.Should().Be(1);
        result.DroppedNotInRoster.Should().Be(1);
        result.ParsedLogRows.Should().Be(1);
        var row = (await LogRowsAsync(db, result.SessionId)).Should().ContainSingle().Subject;
        row.Kind.Should().Be("Residue");
        row.Type.Should().BeNull();
        row.Vol.Should().BeNull();
        row.Units.Should().Be(7);
        // A residue line is not a qualifying event, so the timeline stays paused.
        (await TimelineAsync(db, result.SessionId)).Should().Equal((0, 60, true));
    }

    [Fact]
    public async Task BuildAsync_merges_a_source_copying_its_rows_archiving_it_and_not_reparsing_its_span()
    {
        await using var db = await NewSchemaAsync();
        // Source ran 20..40 with a pause 30..35; pilot 1 only; two ticks at 22 and 37.
        var source = await SeedSavedSessionAsync(db, "Earlier", 20, 40,
            new[] { (20, 30, false), (30, 35, true), (35, 40, false) },
            new[] { (1L, "Miner") },
            new[] { (1L, 22), (1L, 37) });
        // Parsed events: pilot 1 at 25 (inside the source span -> owned, dropped),
        // pilot 2 at 25 (not in the source -> kept), pilot 1 at 50 (gap -> kept).
        var events = new[] { Tick(1, 25), Tick(2, 25), Tick(1, 50) };

        var result = await NewBuilder(db).BuildAsync(Request(events, merge: new[] { source }));

        result.CopiedLogRows.Should().Be(2);
        result.ParsedLogRows.Should().Be(2);
        result.DroppedByOwnership.Should().Be(1);
        result.ArchivedSessionIds.Should().Equal(source);

        var rows = await LogRowsAsync(db, result.SessionId);
        rows.Select(r => (r.Pilot, r.Ts)).Should().Equal((1L, Iso(M(22))), (2L, Iso(M(25))), (1L, Iso(M(37))), (1L, Iso(M(50))));
        rows.Where(r => r.Ts == Iso(M(22))).Should().OnlyContain(r => r.Vol == 5.0 && r.Role == "Miner", "copied rows are verbatim");

        var archived = await db.QueryAsync("SELECT archived, merged_into FROM session WHERE session_id = $sid",
            static r => (r.GetInt64(0), r.IsDBNull(1) ? null : r.GetString(1)), new Dictionary<string, object?> { ["$sid"] = source });
        archived.Should().ContainSingle().Which.Should().Be((1L, result.SessionId));
        // The source's own rows still exist untouched (archived, not deleted).
        (await LogRowsAsync(db, source)).Should().HaveCount(2);

        // Timeline: source rows govern 20..40; pilot 2's tick at 25 lies inside the
        // source span so it does not alter it; the gap 40..60 gets the idle rule.
        (await TimelineAsync(db, result.SessionId)).Should().Equal(
            (0, 20, true), (20, 30, false), (30, 35, true), (35, 40, false), (40, 50, true), (50, 55, false), (55, 60, true));
    }

    [Fact]
    public async Task BuildAsync_widens_the_window_to_cover_a_merged_source_and_derives_the_id_from_the_widened_start()
    {
        await using var db = await NewSchemaAsync();
        var source = await SeedSavedSessionAsync(db, "Earlier", -30, -10,
            new[] { (-30, -10, false) }, new[] { (1L, "Miner") }, new[] { (1L, -20) });

        var result = await NewBuilder(db).BuildAsync(Request(Array.Empty<MiningEvent>(), merge: new[] { source }));

        result.EffectiveStart.Should().Be(M(-30));
        result.EffectiveStop.Should().Be(M(60));
        result.SessionId.Should().Be(SessionId.Compute("Rebuilt", Iso(M(-30))));
        (await TimelineAsync(db, result.SessionId)).Should().Equal((-30, -10, false), (-10, 60, true));
    }

    [Fact]
    public async Task BuildAsync_throws_and_writes_nothing_when_the_id_already_exists()
    {
        await using var db = await NewSchemaAsync();
        await NewBuilder(db).BuildAsync(Request(new[] { Tick(1, 10) }));

        var act = async () => await NewBuilder(db).BuildAsync(Request(new[] { Tick(1, 11) }));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already exists*");
        (await db.QueryScalarAsync<long>("SELECT COUNT(*) FROM session")).Should().Be(1);
        (await db.QueryScalarAsync<long>("SELECT COUNT(*) FROM log")).Should().Be(1, "the failed build was rolled back");
    }

    [Fact]
    public async Task BuildAsync_refuses_a_missing_archived_or_running_source()
    {
        await using var db = await NewSchemaAsync();
        var archived = await SeedSavedSessionAsync(db, "Gone", 0, 10, new[] { (0, 10, false) }, new[] { (1L, "Miner") }, Array.Empty<(long, int)>());
        await db.ExecuteNonQueryAsync("UPDATE session SET archived = 1 WHERE session_id = $sid", new Dictionary<string, object?> { ["$sid"] = archived });
        var running = SessionId.Compute("Live", Iso(M(0)));
        await db.ExecuteNonQueryAsync("INSERT INTO session (session_id, name, notes) VALUES ($sid, 'Live', NULL)", new Dictionary<string, object?> { ["$sid"] = running });
        await db.ExecuteNonQueryAsync("INSERT INTO session_time (session_id, start_time, stop_time, is_pause) VALUES ($sid, $s, NULL, 0)",
            new Dictionary<string, object?> { ["$sid"] = running, ["$s"] = Iso(M(0)) });
        var builder = NewBuilder(db);

        var missing = async () => await builder.BuildAsync(Request(Array.Empty<MiningEvent>(), merge: new[] { "nope" }));
        var folded = async () => await builder.BuildAsync(Request(Array.Empty<MiningEvent>(), merge: new[] { archived }));
        var live = async () => await builder.BuildAsync(Request(Array.Empty<MiningEvent>(), merge: new[] { running }));

        await missing.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not exist*");
        await folded.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already archived*");
        await live.Should().ThrowAsync<InvalidOperationException>().WithMessage("*still running*");
        (await db.QueryScalarAsync<long>("SELECT COUNT(*) FROM session")).Should().Be(2, "nothing new was written");
    }

    [Fact]
    public async Task BuildAsync_validates_the_request()
    {
        await using var db = await NewSchemaAsync();
        var builder = NewBuilder(db);
        var none = Array.Empty<MiningEvent>();

        var blank = async () => await builder.BuildAsync(Request(none, name: " "));
        var inverted = async () => await builder.BuildAsync(Request(none, startMin: 10, stopMin: 0));
        var noRoster = async () => await builder.BuildAsync(Request(none, roster: Array.Empty<FleetMember>()));
        var dupMerge = async () => await builder.BuildAsync(Request(none, merge: new[] { "a", "a" }));
        var nullReq = async () => await builder.BuildAsync(null!);

        await blank.Should().ThrowAsync<ArgumentException>();
        await inverted.Should().ThrowAsync<ArgumentException>();
        await noRoster.Should().ThrowAsync<ArgumentException>();
        await dupMerge.Should().ThrowAsync<ArgumentException>();
        await nullReq.Should().ThrowAsync<ArgumentNullException>();
    }

    private sealed class StubSde : ISdeLookup
    {
        public long? TryGetTypeId(string productName) => Veldspar;
        public MiningProduct? TryGetProduct(long typeId) => typeId == Veldspar ? new MiningProduct(Veldspar, "Veldspar", 0.1, 100) : null;
        public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId) => Array.Empty<TypeMaterial>();
        public IReadOnlyList<long> GetRefinedMaterialTypeIds() => Array.Empty<long>();
        public string? TryGetRefinedMaterialName(long typeId) => null;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
