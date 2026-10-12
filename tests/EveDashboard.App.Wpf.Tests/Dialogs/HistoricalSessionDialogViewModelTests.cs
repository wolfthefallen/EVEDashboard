namespace EveDashboard.App.Wpf.Tests.Dialogs;

using EveDashboard.App.Wpf.Dialogs;
using EveDashboard.App.Wpf.Reporting;
using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Sessions.Historical;
using EveDashboard.MarketData.Pricing;
using EveDashboard.Mining.Historical;
using EveDashboard.Stats.Historical;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class HistoricalSessionDialogViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset M(int minutes) => T0.AddMinutes(minutes);

    private static readonly Fleet FleetA = new("fleet-a", "Alpha", "2026-05-01T00:00:00Z",
        new[] { new FleetMember(1, "Asgar", PilotRole.Miner), new FleetMember(2, "Bjorn", PilotRole.Booster) });
    private static readonly Fleet FleetB = new("fleet-b", "Bravo", "2026-05-01T00:00:00Z",
        new[] { new FleetMember(3, "Cara", PilotRole.Miner) });

    private static SessionSpanPilot Pilot(long id, string name, PilotRole role = PilotRole.Miner) => new(id, name, role);

    private static MiningEvent Tick(long pilot, DateTimeOffset at) =>
        new(MiningEventKind.Tick, at, pilot, "Veldspar", 1230L, 100);

    private sealed class Harness
    {
        public FakeSpanQuery Spans { get; } = new();
        public FakeReader Reader { get; } = new();
        public FakeBuilder Builder { get; } = new();
        public FakePricer Pricer { get; } = new();
        public FakeFleetRepository Fleets { get; } = new();
        public FakeSettingsRepository Settings { get; } = new();
        public ActiveSessionContext Active { get; } = new();
        public FakeConfirmDialog Confirm { get; }
        public DailyHistorySessionRunner Runner { get; }
        public HistoricalSessionDialogViewModel Vm { get; }

        public Harness(bool confirmResult = true)
        {
            Confirm = new FakeConfirmDialog(confirmResult);
            Fleets.Fleets.AddRange(new[] { FleetB, FleetA });
            Settings.DefaultFleetId = FleetA.FleetId;
            Settings.AutoPauseIdleMinutes = 7;
            Runner = new DailyHistorySessionRunner(
                Reader, Spans, Builder, Pricer, NullLogger<DailyHistorySessionRunner>.Instance);
            Vm = new HistoricalSessionDialogViewModel(Spans, Reader, Builder, Pricer, Fleets, Settings, Active, Confirm,
                Runner, NullLogger<HistoricalSessionDialogViewModel>.Instance);
        }

        /// <summary>Open in date-range mode with a valid one-hour window on 2026-05-05 12:00..13:00 and fleet Alpha.</summary>
        public async Task OpenDateRangeAsync()
        {
            Vm.ResetForOpen(null);
            await Vm.InitializeAsync();
            // These tests cover the single-window flow; the per-day batch has its
            // own tests against the runner.
            Vm.SplitByDay = false;
            Vm.StartDate = T0.UtcDateTime.Date; Vm.StartTimeText = "12:00";
            Vm.StopDate = T0.UtcDateTime.Date;  Vm.StopTimeText = "13:00";
            Vm.SelectedFleet = FleetA;
        }
    }

    [Fact]
    public async Task InitializeAsync_blocks_the_dialog_while_a_session_is_running_or_paused()
    {
        var h = new Harness();
        h.Active.Set("live", SessionRunState.Paused);
        h.Vm.ResetForOpen(null);

        await h.Vm.InitializeAsync();

        h.Vm.BlockedMessage.Should().Contain("Stop it first");
        h.Vm.IsEnabled.Should().BeFalse();
        h.Vm.CheckOverlapsCommand.CanExecute(null).Should().BeFalse();
        h.Vm.BuildCommand.CanExecute(null).Should().BeFalse();
        h.Vm.Fleets.Should().BeEmpty("nothing is loaded while blocked");
    }

    [Fact]
    public async Task InitializeAsync_loads_fleets_sorted_by_name_and_preselects_the_default()
    {
        var h = new Harness();
        h.Vm.ResetForOpen(null);

        await h.Vm.InitializeAsync();

        h.Vm.Fleets.Select(f => f.Name).Should().Equal("Alpha", "Bravo");
        h.Vm.SelectedFleet.Should().Be(FleetA);
        h.Vm.IsCombineMode.Should().BeFalse();
        h.Vm.HeaderText.Should().Be("Build a session from history");
        h.Vm.SplitByDay.Should().BeTrue("a date-range open defaults to one session per mining day");
        h.Vm.SessionName.Should().Be("{{$DATE}} {{$DOW}} history{{$OVERNIGHT}}");
        h.Vm.IsFleetVisible.Should().BeFalse("per-day mode discovers its roster from the logs");
        h.Vm.HasChecked.Should().BeFalse("the action slot shows the check until a check has run");

        h.Vm.SplitByDay = false;

        h.Vm.SessionName.Should().Be("{{$STARTDATE}} - {{$ENDDATE}} {{$YYYY}} history",
            "the single-window mode names the whole window instead");
        h.Vm.IsFleetVisible.Should().BeTrue();
    }

    [Fact]
    public async Task A_name_the_user_edited_survives_switching_mode()
    {
        var h = new Harness();
        h.Vm.ResetForOpen(null);
        await h.Vm.InitializeAsync();

        h.Vm.SessionName = "Hek belt grind";
        h.Vm.SplitByDay = false;

        h.Vm.SessionName.Should().Be("Hek belt grind");
    }

    [Fact]
    public async Task Per_day_mode_needs_no_fleet()
    {
        var h = new Harness();
        h.Reader.SetInventory((1L, "Asgar"));
        h.Reader.Result = new HistoricalReadResult(
            new[] { Tick(1, M(0)), Tick(1, M(2)) }, new[] { "log.txt" }, Array.Empty<long>());
        h.Vm.ResetForOpen(null);
        await h.Vm.InitializeAsync();
        h.Vm.SelectedFleet = null;
        h.Vm.StartDate = T0.UtcDateTime.Date; h.Vm.StartTimeText = "00:00";
        h.Vm.StopDate = T0.UtcDateTime.Date; h.Vm.StopTimeText = "23:59";

        await h.Vm.CheckOverlapsCommand.ExecuteAsync(null);

        h.Vm.StatusMessage.Should().NotContain("Pick a fleet");
        h.Vm.DayRows.Should().ContainSingle().Which.WillBuild.Should().BeTrue();
        h.Vm.ActionButtonText.Should().Be("Build 1 session");
    }

    [Fact]
    public async Task Per_day_mode_previews_the_names_and_then_builds_exactly_those_days()
    {
        var h = new Harness();
        h.Reader.SetInventory((1L, "Asgar"));
        h.Reader.Result = new HistoricalReadResult(
            new[] { Tick(1, M(0)), Tick(1, M(1440)) }, new[] { "log.txt" }, Array.Empty<long>());
        h.Vm.ResetForOpen(null);
        await h.Vm.InitializeAsync();
        h.Vm.StartDate = T0.UtcDateTime.Date; h.Vm.StartTimeText = "00:00";
        h.Vm.StopDate = T0.AddDays(2).UtcDateTime.Date; h.Vm.StopTimeText = "23:59";

        await h.Vm.CheckOverlapsCommand.ExecuteAsync(null);

        h.Vm.DayRows.Select(r => r.Name).Should().Equal(
            new[] { "2026-05-05 Tue history", "2026-05-06 Wed history" },
            "the preview shows the exact names the build will use");
        h.Vm.ActionButtonText.Should().Be("Build 2 sessions");

        await h.Vm.BuildCommand.ExecuteAsync(null);

        h.Builder.Requests.Select(r => r.Name).Should().Equal(
            new[] { "2026-05-05 Tue history", "2026-05-06 Wed history" });
        h.Vm.Outcome!.Sessions.Should().HaveCount(2);
        h.Vm.ShouldClose.Should().BeTrue();
    }

    [Fact]
    public async Task The_progress_bar_is_cleared_once_a_batch_finishes()
    {
        var h = new Harness();
        h.Reader.SetInventory((1L, "Asgar"));
        h.Reader.Result = new HistoricalReadResult(
            new[] { Tick(1, M(0)), Tick(1, M(1440)) }, new[] { "log.txt" }, Array.Empty<long>());
        h.Vm.ResetForOpen(null);
        await h.Vm.InitializeAsync();
        h.Vm.StartDate = T0.UtcDateTime.Date; h.Vm.StartTimeText = "00:00";
        h.Vm.StopDate = T0.AddDays(2).UtcDateTime.Date; h.Vm.StopTimeText = "23:59";

        await h.Vm.CheckOverlapsCommand.ExecuteAsync(null);

        h.Vm.IsBusy.Should().BeFalse("the window must be usable again after the check");
        h.Vm.ProgressMaximum.Should().Be(0);
        h.Vm.IsProgressIndeterminate.Should().BeFalse();

        await h.Vm.BuildCommand.ExecuteAsync(null);

        h.Vm.IsBusy.Should().BeFalse("and after the build, so the dialog can close");
        h.Vm.ProgressValue.Should().Be(0);
        h.Vm.ProgressMaximum.Should().Be(0);
        h.Vm.IsProgressIndeterminate.Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_build_still_clears_the_busy_state()
    {
        var h = new Harness();
        h.Reader.SetInventory((1L, "Asgar"));
        h.Reader.Result = new HistoricalReadResult(
            new[] { Tick(1, M(0)) }, new[] { "log.txt" }, Array.Empty<long>());
        h.Vm.ResetForOpen(null);
        await h.Vm.InitializeAsync();
        h.Vm.StartDate = T0.UtcDateTime.Date; h.Vm.StopDate = T0.UtcDateTime.Date; h.Vm.StopTimeText = "23:59";
        await h.Vm.CheckOverlapsCommand.ExecuteAsync(null);
        h.Builder.Throw = new InvalidOperationException("boom");

        await h.Vm.BuildCommand.ExecuteAsync(null);

        h.Vm.IsBusy.Should().BeFalse("a stuck busy flag would lock the dialog shut");
        h.Vm.ShouldClose.Should().BeFalse();
    }

    [Fact]
    public async Task Toggling_the_merge_box_invalidates_the_checked_plan()
    {
        var h = new Harness();
        h.Reader.SetInventory((1L, "Asgar"));
        h.Reader.Result = new HistoricalReadResult(
            new[] { Tick(1, M(0)) }, new[] { "log.txt" }, Array.Empty<long>());
        h.Vm.ResetForOpen(null);
        await h.Vm.InitializeAsync();
        h.Vm.StartDate = T0.UtcDateTime.Date; h.Vm.StopDate = T0.UtcDateTime.Date; h.Vm.StopTimeText = "23:59";
        await h.Vm.CheckOverlapsCommand.ExecuteAsync(null);
        h.Vm.HasChecked.Should().BeTrue();

        h.Vm.MergeSameDaySessions = false;

        h.Vm.HasChecked.Should().BeFalse("the plan belonged to the old answer");
        h.Vm.DayRows.Should().BeEmpty();
        h.Vm.ActionButtonText.Should().Be("Check the days");
    }

    [Fact]
    public async Task Use_all_available_logs_fills_the_range_and_never_runs_past_now()
    {
        var h = new Harness();
        h.Reader.SetInventory((1L, "Asgar"));
        h.Vm.ResetForOpen(null);
        await h.Vm.InitializeAsync();

        h.Vm.UseAllAvailableLogsCommand.Execute(null);

        h.Vm.StartDate.Should().Be(FakeReader.InventoryAt.UtcDateTime.Date, "the range starts at the oldest log");
        h.Vm.TryParseWindow(out var start, out var stop).Should().BeNull("the prefilled range must be valid");
        stop.Should().BeOnOrBefore(DateTimeOffset.UtcNow, "a window ending in the future is refused");
        start.Should().BeBefore(stop);
    }

    [Fact]
    public void Combine_mode_never_builds_per_day()
    {
        var h = new Harness();

        h.Vm.ResetForOpen(new[] { "s1", "s2" });

        h.Vm.IsCombineMode.Should().BeTrue();
        h.Vm.SplitByDay.Should().BeFalse("combining is about named sessions, not days");
        h.Vm.IsDailyMode.Should().BeFalse();
        h.Vm.IsFleetVisible.Should().BeTrue();
        h.Vm.SessionName.Should().Be("{{$STARTDATE}} - {{$ENDDATE}} {{$YYYY}} history");
    }

    [Fact]
    public async Task CheckOverlaps_reports_validation_errors_without_querying()
    {
        var h = new Harness();
        h.Vm.ResetForOpen(null);
        await h.Vm.InitializeAsync();
        h.Vm.SplitByDay = false; // the fleet is only asked for in single-window mode

        await h.Vm.CheckOverlapsCommand.ExecuteAsync(null);
        h.Vm.StatusMessage.Should().Be("Pick both a start and a stop date.");

        h.Vm.StartDate = T0.UtcDateTime.Date; h.Vm.StopDate = T0.UtcDateTime.Date;
        h.Vm.StartTimeText = "13:00"; h.Vm.StopTimeText = "12:00";
        await h.Vm.CheckOverlapsCommand.ExecuteAsync(null);
        h.Vm.StatusMessage.Should().Be("The stop must be after the start.");

        h.Vm.StartTimeText = "nope";
        await h.Vm.CheckOverlapsCommand.ExecuteAsync(null);
        h.Vm.StatusMessage.Should().Contain("HH:mm");

        h.Vm.StartTimeText = "12:00"; h.Vm.StopTimeText = "13:00"; h.Vm.SelectedFleet = null;
        await h.Vm.CheckOverlapsCommand.ExecuteAsync(null);
        h.Vm.StatusMessage.Should().Be("Pick a fleet.");

        h.Spans.OverlapCalls.Should().Be(0);
        h.Vm.HasChecked.Should().BeFalse();
    }

    [Fact]
    public async Task CheckOverlaps_lists_relevant_sessions_with_their_kind_and_writes_the_exclusion_notice()
    {
        var h = new Harness();
        h.Spans.Spans.Add(new SessionSpan("full", "Belt run", M(10), M(20), new[] { Pilot(1, "Asgar") }));
        h.Spans.Spans.Add(new SessionSpan("partial", "Moon pop", M(30), M(40), new[] { Pilot(2, "Bjorn", PilotRole.Booster), Pilot(9, "Zed", PilotRole.Cyno) }));
        h.Spans.Spans.Add(new SessionSpan("unrelated", "Other crew", M(30), M(40), new[] { Pilot(7, "Nobody") }));
        await h.OpenDateRangeAsync();

        await h.Vm.CheckOverlapsCommand.ExecuteAsync(null);

        h.Vm.HasChecked.Should().BeTrue();
        h.Vm.Overlaps.Select(r => (r.SessionId, r.IsPartial, r.IsChecked)).Should().Equal(("full", false, false), ("partial", true, false));
        h.Vm.Overlaps[1].KindText.Should().Be("Adds Zed - combining it creates a union fleet");
        h.Vm.Overlaps[0].KindText.Should().Be("Same pilots as the fleet");
        h.Vm.ExclusionNotice.Should().Contain("'Belt run'").And.Contain("Asgar's time inside it will be excluded");
        h.Vm.ExclusionNotice.Should().Contain("'Moon pop'").And.Contain("Bjorn's time");
        h.Vm.StatusMessage.Should().Be("2 overlapping session(s) found - tick the ones to combine.");

        // Ticking a session drops it from the notice.
        h.Vm.Overlaps[0].IsChecked = true;
        h.Vm.ExclusionNotice.Should().NotContain("Belt run").And.Contain("Moon pop");

        // Editing an input invalidates the check.
        h.Vm.StopTimeText = "13:30";
        h.Vm.HasChecked.Should().BeFalse();
        h.Vm.Overlaps.Should().BeEmpty();
        h.Vm.ExclusionNotice.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckOverlaps_with_nothing_overlapping_sets_the_hint()
    {
        var h = new Harness();
        await h.OpenDateRangeAsync();

        await h.Vm.CheckOverlapsCommand.ExecuteAsync(null);

        h.Vm.HasNoOverlaps.Should().BeTrue();
        h.Vm.StatusMessage.Should().Be("No saved session overlaps this window for these pilots.");
        h.Vm.BuildCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Build_in_date_range_mode_reads_logs_builds_with_exclusions_prices_and_closes_with_a_summary()
    {
        var h = new Harness();
        h.Spans.Spans.Add(new SessionSpan("declined", "Belt run", M(10), M(20), new[] { Pilot(1, "Asgar") }));
        h.Spans.Spans.Add(new SessionSpan("merged", "Second run", M(30), M(40), new[] { Pilot(2, "Bjorn", PilotRole.Booster) }));
        h.Reader.Result = new HistoricalReadResult(
            new[] { new MiningEvent(MiningEventKind.Tick, M(5), 1, "Veldspar", 1230, 100) },
            new[] { "a.txt" },
            new[] { 2L });
        h.Pricer.Result = new HistoricalPricingResult("new-id", 60003760, HistoricalPriceBasis.FetchedToday, 0, 0, 5, 0);
        await h.OpenDateRangeAsync();
        h.Vm.SessionName = "{{$DATE}} rebuilt";
        h.Vm.Notes = "n";
        await h.Vm.CheckOverlapsCommand.ExecuteAsync(null);
        h.Vm.Overlaps.Single(r => r.SessionId == "merged").IsChecked = true;

        await h.Vm.BuildCommand.ExecuteAsync(null);

        h.Reader.LastPilotIds.Should().BeEquivalentTo(new long[] { 1, 2 });
        (h.Reader.LastStart, h.Reader.LastStop).Should().Be((M(0), M(60)));

        var req = h.Builder.LastRequest!;
        req.Name.Should().Be("2026-05-05 rebuilt");
        req.Notes.Should().Be("n");
        (req.WindowStart, req.WindowStop).Should().Be((M(0), M(60)));
        req.Roster.Should().Equal(FleetA.Members);
        req.ParsedEvents.Should().HaveCount(1);
        req.SessionIdsToMerge.Should().Equal("merged");
        req.Exclusions.Should().ContainSingle().Which.Should().Be(new PilotExclusion(1, M(10), M(20), "declined"));
        req.IdleMinutes.Should().Be(7);

        (h.Pricer.LastSessionId, h.Pricer.LastStart, h.Pricer.LastStop).Should().Be(("new-id", M(0), M(60)));
        h.Confirm.ShowCount.Should().Be(0, "no partial match was ticked");

        h.Vm.ShouldClose.Should().BeTrue();
        h.Vm.Outcome.Should().NotBeNull();
        var session = h.Vm.Outcome!.Sessions.Should().ContainSingle().Subject;
        session.SessionId.Should().Be("new-id");
        session.SessionName.Should().Be("2026-05-05 rebuilt");
        h.Vm.Outcome.Summary.Should().Contain("3 copied + 4 parsed mining rows, 1 session(s) archived")
            .And.Contain("valued with TODAY's prices")
            .And.Contain("No gamelog found for: Bjorn");
    }

    [Fact]
    public async Task Build_in_combine_mode_prefills_the_window_preticks_the_sessions_and_skips_the_log_read()
    {
        var h = new Harness();
        h.Spans.Spans.Add(new SessionSpan("s1", "One", M(-30), M(-10), new[] { Pilot(1, "Asgar") }));
        h.Spans.Spans.Add(new SessionSpan("s2", "Two", M(10), M(40), new[] { Pilot(2, "Bjorn", PilotRole.Booster) }));
        h.Pricer.Result = new HistoricalPricingResult("new-id", 60003760, HistoricalPriceBasis.AveragedFromWindow, 5, 40, 0, 0);
        h.Vm.ResetForOpen(new[] { "s1", "s2" });

        await h.Vm.InitializeAsync();

        h.Vm.IsCombineMode.Should().BeTrue();
        h.Vm.HeaderText.Should().Be("Combine saved sessions");
        h.Vm.SessionName.Should().Be(HistoricalSessionDialogViewModel.DefaultNameTemplate);
        (h.Vm.StartDate, h.Vm.StartTimeText).Should().Be((M(-30).UtcDateTime.Date, "11:30"));
        (h.Vm.StopDate, h.Vm.StopTimeText).Should().Be((M(40).UtcDateTime.Date, "12:40"));
        h.Vm.HasChecked.Should().BeTrue("combine mode runs the check on open");
        h.Vm.Overlaps.Select(r => (r.SessionId, r.IsChecked)).Should().Equal(("s1", true), ("s2", true));
        h.Vm.ExclusionNotice.Should().BeEmpty();

        await h.Vm.BuildCommand.ExecuteAsync(null);

        h.Reader.Calls.Should().Be(0, "a pure combine never parses gamelogs");
        h.Builder.LastRequest!.Name.Should().Be("5May - 5May 2026 history", "the default template renders the window's dates");
        h.Builder.LastRequest.ParsedEvents.Should().BeEmpty();
        h.Builder.LastRequest.SessionIdsToMerge.Should().Equal("s1", "s2");
        h.Vm.Outcome!.Summary.Should().Contain("Prices averaged from 40 market rows");
    }

    [Fact]
    public async Task Build_with_a_ticked_partial_match_asks_to_create_the_union_fleet_and_uses_it_when_accepted()
    {
        var h = new Harness(confirmResult: true);
        h.Spans.Spans.Add(new SessionSpan("partial", "Moon pop", M(30), M(40), new[] { Pilot(2, "Bjorn", PilotRole.Booster), Pilot(9, "Zed", PilotRole.Cyno) }));
        await h.OpenDateRangeAsync();
        await h.Vm.CheckOverlapsCommand.ExecuteAsync(null);
        h.Vm.Overlaps[0].IsChecked = true;

        await h.Vm.BuildCommand.ExecuteAsync(null);

        h.Confirm.ShowCount.Should().Be(1);
        h.Confirm.LastBody.Should().Contain("Zed").And.Contain("Alpha + Zed");
        h.Fleets.Created.Should().ContainSingle().Which.Name.Should().Be("Alpha + Zed");
        h.Fleets.Created[0].Members.Select(m => (m.PilotId, m.Role)).Should().Equal((1L, PilotRole.Miner), (2L, PilotRole.Booster), (9L, PilotRole.Cyno));
        var req = h.Builder.LastRequest!;
        req.Roster.Select(m => m.PilotId).Should().Equal(1L, 2L, 9L);
        req.SessionIdsToMerge.Should().Equal("partial");
        req.Exclusions.Should().BeEmpty();
        h.Reader.LastPilotIds.Should().BeEquivalentTo(new long[] { 1, 2, 9 }, "the union roster drives the log read");
    }

    [Fact]
    public async Task Build_with_a_ticked_partial_match_leaves_it_out_when_the_union_fleet_is_declined()
    {
        var h = new Harness(confirmResult: false);
        h.Spans.Spans.Add(new SessionSpan("partial", "Moon pop", M(30), M(40), new[] { Pilot(2, "Bjorn", PilotRole.Booster), Pilot(9, "Zed", PilotRole.Cyno) }));
        await h.OpenDateRangeAsync();
        await h.Vm.CheckOverlapsCommand.ExecuteAsync(null);
        h.Vm.Overlaps[0].IsChecked = true;

        await h.Vm.BuildCommand.ExecuteAsync(null);

        h.Fleets.Created.Should().BeEmpty();
        var req = h.Builder.LastRequest!;
        req.Roster.Should().Equal(FleetA.Members);
        req.SessionIdsToMerge.Should().BeEmpty();
        req.Exclusions.Should().ContainSingle().Which.Should().Be(new PilotExclusion(2, M(30), M(40), "partial"));
    }

    [Fact]
    public async Task Build_reports_a_builder_failure_and_stays_open()
    {
        var h = new Harness();
        h.Builder.Throw = new InvalidOperationException("A session named 'x' starting at y already exists; pick a different name.");
        await h.OpenDateRangeAsync();
        await h.Vm.CheckOverlapsCommand.ExecuteAsync(null);

        await h.Vm.BuildCommand.ExecuteAsync(null);

        h.Vm.StatusMessage.Should().StartWith("Build failed:").And.Contain("already exists");
        h.Vm.ShouldClose.Should().BeFalse();
        h.Vm.Outcome.Should().BeNull();
        h.Pricer.LastSessionId.Should().BeNull("nothing is priced when the build failed");
    }

    [Fact]
    public void BuildSummary_describes_the_mixed_pricing_case()
    {
        var result = new HistoricalSessionResult("id", M(0), M(60), 0, 12, 0, 0, 0, 1, 2, Array.Empty<string>());
        var pricing = new HistoricalPricingResult("id", 1, HistoricalPriceBasis.Mixed, 3, 9, 2, 0);

        var text = HistoricalSessionDialogViewModel.BuildSummary("N", result, pricing, Array.Empty<long>(), FleetA.Members);

        text.Should().Be("Built 'N': 0 copied + 12 parsed mining rows, 0 session(s) archived. Prices averaged from 9 window rows; 2 material(s) had no window data and use TODAY's prices.");
    }

    [Fact]
    public void Cancel_closes_without_an_outcome()
    {
        var h = new Harness();
        h.Vm.ResetForOpen(null);

        h.Vm.CancelCommand.Execute(null);

        h.Vm.ShouldClose.Should().BeTrue();
        h.Vm.Outcome.Should().BeNull();
    }

    private sealed class FakeFleetRepository : IFleetRepository
    {
        public List<Fleet> Fleets { get; } = new();
        public List<(string Name, IReadOnlyList<FleetMember> Members)> Created { get; } = new();

        public Task<IReadOnlyList<Fleet>> GetFleetsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Fleet>>(Fleets.ToList());

        public Task<FleetCreateResult> CreateFleetAsync(string name, IReadOnlyList<FleetMember> members, CancellationToken ct = default)
        {
            Created.Add((name, members));
            return Task.FromResult(new FleetCreateResult(FleetCreateStatus.Created, "new-fleet"));
        }

        public Task DeleteFleetAsync(string fleetId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
