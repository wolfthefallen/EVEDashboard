namespace EveDashboard.App.Wpf.Tests.Reporting;

using EveDashboard.App.Wpf.Reporting;
using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Sessions.Historical;
using EveDashboard.Mining.Historical;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class DailyHistorySessionRunnerTests
{
    private const long PilotA = 1001L;
    private const long PilotB = 1002L;

    private static readonly DateTimeOffset RangeStart = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RangeStop = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int hour, int minute, int day) =>
        new(2026, 10, day, hour, minute, 0, TimeSpan.Zero);

    private static MiningEvent Tick(long pilot, DateTimeOffset at) =>
        new(MiningEventKind.Tick, at, pilot, "Veldspar", 1230L, 100);

    private sealed class Harness
    {
        public FakeReader Reader { get; } = new();
        public FakeSpanQuery Spans { get; } = new();
        public FakeBuilder Builder { get; } = new();
        public FakePricer Pricer { get; } = new();
        public DailyHistorySessionRunner Runner { get; }

        public Harness()
        {
            Reader.SetInventory((PilotA, "Alice"), (PilotB, "Bob"));
            Runner = new DailyHistorySessionRunner(
                Reader, Spans, Builder, Pricer, NullLogger<DailyHistorySessionRunner>.Instance);
        }

        public void Logs(params MiningEvent[] events) =>
            Reader.Result = new HistoricalReadResult(events, new[] { "one.txt" }, Array.Empty<long>());

        public Task<DailyHistoryRunResult> RunAsync(
            bool merge = false,
            string template = "{{$DATE}} history",
            IProgress<DailyHistoryProgress>? progress = null) =>
            Runner.RunAsync(
                new DailyHistoryRunRequest(RangeStart, RangeStop, template, "notes", 5, merge),
                progress);
    }

    [Fact]
    public async Task Builds_one_session_per_mining_day_in_order()
    {
        var h = new Harness();
        h.Logs(
            Tick(PilotA, At(14, 0, 11)),
            Tick(PilotA, At(14, 0, 12)),
            Tick(PilotA, At(14, 0, 13)));

        var result = await h.RunAsync();

        result.Built.Select(b => b.Day).Should().Equal(
            new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 13));
        result.Built.Select(b => b.Name).Should().Equal(
            new[] { "2026-10-11 history", "2026-10-12 history", "2026-10-13 history" },
            "each day's name is expanded from its own window");
        result.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task Reads_the_logs_once_for_the_whole_range()
    {
        var h = new Harness();
        h.Logs(Tick(PilotA, At(14, 0, 11)), Tick(PilotA, At(14, 0, 12)));

        await h.RunAsync();

        h.Reader.Calls.Should().Be(1, "three days must not mean three passes over the gamelogs");
        h.Reader.LastPilotIds.Should().BeEquivalentTo(new[] { PilotA, PilotB });
        h.Reader.LastStart.Should().Be(RangeStart);
        h.Reader.LastStop.Should().Be(RangeStop);
    }

    [Fact]
    public async Task Reads_the_saved_sessions_once_before_writing_anything()
    {
        var h = new Harness();
        h.Logs(Tick(PilotA, At(14, 0, 11)), Tick(PilotA, At(14, 0, 12)));

        await h.RunAsync();

        h.Spans.GetAllCalls.Should().Be(
            1, "querying between days would let the batch see its own new sessions");
        h.Spans.OverlapCalls.Should().Be(0, "the per-day windows are filtered in memory");
    }

    [Fact]
    public async Task Passes_each_days_roster_window_and_notes_to_the_builder()
    {
        var h = new Harness();
        h.Logs(Tick(PilotA, At(14, 0, 11)), Tick(PilotB, At(14, 1, 11)));

        await h.RunAsync();

        var request = h.Builder.Requests.Should().ContainSingle().Subject;
        request.Roster.Select(m => m.PilotId).Should().Equal(PilotA, PilotB);
        request.Roster.Select(m => m.Name).Should().Equal(new[] { "Alice", "Bob" }, "names come from the inventory");
        request.WindowStart.Should().Be(At(14, 0, 11));
        request.WindowStop.Should().Be(At(14, 6, 11));
        request.Notes.Should().Be("notes");
        request.IdleMinutes.Should().Be(5);
    }

    [Fact]
    public async Task Prices_every_built_session_in_a_single_batch()
    {
        var h = new Harness();
        h.Logs(Tick(PilotA, At(14, 0, 11)), Tick(PilotA, At(14, 0, 12)));

        var result = await h.RunAsync();

        var call = h.Pricer.BatchCalls.Should().ContainSingle(
            "the market is asked once for the whole batch").Subject;
        call.Select(t => t.SessionId).Should().Equal(new[] { "new-id", "new-id-2" });
        result.Pricing.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_failing_day_is_reported_and_the_rest_still_build()
    {
        var h = new Harness();
        h.Logs(
            Tick(PilotA, At(14, 0, 11)),
            Tick(PilotA, At(14, 0, 12)),
            Tick(PilotA, At(14, 0, 13)));
        h.Builder.ThrowFor = req => req.WindowStart.Day == 12
            ? new InvalidOperationException("A session named 'x' starting at y already exists; pick a different name.")
            : null;

        var result = await h.RunAsync();

        result.Built.Select(b => b.Day).Should().Equal(new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 13));
        var failure = result.Failures.Should().ContainSingle().Subject;
        failure.Day.Should().Be(new DateOnly(2026, 10, 12));
        failure.Message.Should().Contain("already exists");
        result.Summary.Should().Contain("1 day(s) failed");
    }

    [Fact]
    public async Task Skipped_days_are_carried_through_to_the_result()
    {
        var h = new Harness();
        h.Logs(Tick(PilotA, At(14, 0, 11)));
        h.Spans.Spans.Add(new SessionSpan(
            "s1", "Saved", At(13, 0, 11), At(16, 0, 11),
            new[] { new SessionSpanPilot(PilotA, "Alice", PilotRole.Miner) }));

        var result = await h.RunAsync(merge: false);

        result.Built.Should().BeEmpty();
        result.Skipped.Should().ContainSingle().Which.Reason.Should().Contain("already belongs to");
        result.Summary.Should().Contain("1 day(s) skipped");
        h.Pricer.BatchCalls.Should().BeEmpty("nothing was built, so nothing needs pricing");
    }

    [Fact]
    public async Task Merging_folds_a_same_day_session_into_the_new_one()
    {
        var h = new Harness();
        h.Logs(Tick(PilotA, At(14, 0, 11)));
        h.Spans.Spans.Add(new SessionSpan(
            "s1", "Saved", At(10, 0, 11), At(10, 30, 11),
            new[] { new SessionSpanPilot(PilotA, "Alice", PilotRole.Miner) }));

        var result = await h.RunAsync(merge: true);

        result.Built.Should().ContainSingle();
        h.Builder.Requests.Should().ContainSingle()
            .Which.SessionIdsToMerge.Should().Equal(new[] { "s1" });
    }

    [Fact]
    public async Task Progress_is_reported_per_phase_and_per_day()
    {
        var h = new Harness();
        h.Logs(Tick(PilotA, At(14, 0, 11)), Tick(PilotA, At(14, 0, 12)));
        var progress = new RecordingProgress<DailyHistoryProgress>();

        await h.RunAsync(progress: progress);

        progress.Reports.Select(p => p.CurrentDay).Should().Equal(
            new[] { "Surveying the gamelogs", "Reading the gamelogs", "2026-10-11", "2026-10-12", "Pricing" },
            "the status line should walk the user through the phases and then the days");
        progress.Reports.Select(p => p.DaysDone).Should().Equal(new[] { 0, 0, 0, 1, 2 });
        progress.Reports.Skip(2).Should().OnlyContain(p => p.DaysTotal == 2);
    }

    [Fact]
    public async Task No_gamelogs_at_all_is_reported_without_touching_the_store()
    {
        var h = new Harness();
        h.Reader.SetInventory();

        var result = await h.RunAsync();

        result.Built.Should().BeEmpty();
        result.Summary.Should().Be("No gamelogs were found, so there is nothing to build.");
        h.Reader.Calls.Should().Be(0, "there is nothing to read");
        h.Spans.GetAllCalls.Should().Be(0);
    }

    [Fact]
    public async Task Logs_with_no_mining_build_nothing()
    {
        var h = new Harness();
        h.Logs();

        var result = await h.RunAsync();

        result.Built.Should().BeEmpty();
        result.Skipped.Should().BeEmpty();
        result.Summary.Should().Contain("No sessions were built.");
    }

    [Fact]
    public async Task The_summary_names_the_range_and_the_rows_written()
    {
        var h = new Harness();
        h.Logs(Tick(PilotA, At(14, 0, 11)), Tick(PilotA, At(14, 0, 12)));

        var result = await h.RunAsync();

        result.Summary.Should().StartWith("Built 2 session(s) for 2026-10-11 to 2026-10-12: 14 mining rows");
    }

    [Fact]
    public async Task Rejects_bad_requests()
    {
        var h = new Harness();

        var nullRequest = async () => await h.Runner.RunAsync(null!);
        await nullRequest.Should().ThrowAsync<ArgumentNullException>();

        var inverted = async () => await h.Runner.RunAsync(
            new DailyHistoryRunRequest(RangeStop, RangeStart, "{{$DATE}}", null, 5, false));
        await inverted.Should().ThrowAsync<ArgumentException>();

        var blankName = async () => await h.Runner.RunAsync(
            new DailyHistoryRunRequest(RangeStart, RangeStop, "  ", null, 5, false));
        await blankName.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Constructor_rejects_nulls()
    {
        var reader = new FakeReader();
        var spans = new FakeSpanQuery();
        var builder = new FakeBuilder();
        var pricer = new FakePricer();
        var log = NullLogger<DailyHistorySessionRunner>.Instance;

        var a = () => new DailyHistorySessionRunner(null!, spans, builder, pricer, log);
        var b = () => new DailyHistorySessionRunner(reader, null!, builder, pricer, log);
        var c = () => new DailyHistorySessionRunner(reader, spans, null!, pricer, log);
        var d = () => new DailyHistorySessionRunner(reader, spans, builder, null!, log);
        var e = () => new DailyHistorySessionRunner(reader, spans, builder, pricer, null!);

        a.Should().Throw<ArgumentNullException>();
        b.Should().Throw<ArgumentNullException>();
        c.Should().Throw<ArgumentNullException>();
        d.Should().Throw<ArgumentNullException>();
        e.Should().Throw<ArgumentNullException>();
    }
}
