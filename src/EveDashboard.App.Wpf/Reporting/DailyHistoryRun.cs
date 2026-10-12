namespace EveDashboard.App.Wpf.Reporting;

using EveDashboard.Core.Sessions.Historical;
using EveDashboard.MarketData.Pricing;
using EveDashboard.Stats.Historical;

/// <summary>
/// What to build: a range of days, and how to treat what is already saved.
/// </summary>
/// <param name="RangeStart">Inclusive start of the range to read logs for (UTC).</param>
/// <param name="RangeStop">Inclusive end of the range to read logs for (UTC).</param>
/// <param name="NameTemplate">Session-name template, expanded per day (see <c>SessionNameTemplateExpander</c>).</param>
/// <param name="Notes">Optional notes, copied to every session built.</param>
/// <param name="IdleMinutes">The auto-pause idle threshold (1..60): the gap that ends a mining run and becomes a pause.</param>
/// <param name="MergeSameDaySessions">When true, a saved session belonging to one of these days is folded into it; when false, its time is excluded instead so nothing is counted twice.</param>
public sealed record DailyHistoryRunRequest(
    DateTimeOffset RangeStart,
    DateTimeOffset RangeStop,
    string NameTemplate,
    string? Notes,
    int IdleMinutes,
    bool MergeSameDaySessions);

/// <summary>
/// A planned batch: what would be built if it ran now. Held between the check
/// and the build so the user is shown exactly what then happens.
/// </summary>
/// <param name="Result">The days to build and the days to skip.</param>
/// <param name="PilotCount">How many pilots had logs in the folder.</param>
/// <param name="Note">Set when the range could not be planned at all (no gamelogs); the reason to show instead of a day list.</param>
public sealed record DailyHistoryPlan(DailyPlanResult Result, int PilotCount, string? Note);

/// <summary>Progress while a batch runs, for a status line.</summary>
/// <param name="CurrentDay">The day being worked on, <c>yyyy-MM-dd</c>, or a phase name before the days begin.</param>
/// <param name="DaysDone">How many days have been built or failed so far.</param>
/// <param name="DaysTotal">How many days the batch will attempt.</param>
public sealed record DailyHistoryProgress(string CurrentDay, int DaysDone, int DaysTotal);

/// <summary>One session the batch created.</summary>
/// <param name="Day">The day it covers.</param>
/// <param name="SessionId">The new session's id.</param>
/// <param name="Name">Its expanded name.</param>
/// <param name="Result">What the builder wrote.</param>
public sealed record DailyHistoryBuilt(DateOnly Day, string SessionId, string Name, HistoricalSessionResult Result);

/// <summary>A day that was attempted and failed, leaving the rest of the batch intact.</summary>
/// <param name="Day">The day that failed.</param>
/// <param name="Name">The name it would have had.</param>
/// <param name="Message">The failure, as the user should see it.</param>
public sealed record DailyHistoryFailure(DateOnly Day, string Name, string Message);

/// <summary>Everything a batch did, for the Reports status line and the log.</summary>
/// <param name="Built">The sessions created, in day order.</param>
/// <param name="Skipped">Days deliberately passed over, with reasons.</param>
/// <param name="Failures">Days that errored; the rest of the batch still completed.</param>
/// <param name="Pricing">One pricing outcome per built session.</param>
/// <param name="Summary">One-line human summary of all of the above.</param>
public sealed record DailyHistoryRunResult(
    IReadOnlyList<DailyHistoryBuilt> Built,
    IReadOnlyList<DailySessionSkip> Skipped,
    IReadOnlyList<DailyHistoryFailure> Failures,
    IReadOnlyList<HistoricalPricingResult> Pricing,
    string Summary);
