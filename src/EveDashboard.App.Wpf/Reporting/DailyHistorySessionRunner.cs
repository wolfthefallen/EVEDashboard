namespace EveDashboard.App.Wpf.Reporting;

using System.Data.Common;
using System.Globalization;
using System.IO;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Sessions.Historical;
using EveDashboard.MarketData.Pricing;
using EveDashboard.Mining.Historical;
using EveDashboard.Stats.Historical;
using Microsoft.Extensions.Logging;

/// <summary>
/// Builds one session per mining day across a range of gamelogs: surveys the
/// logs, reads them once, plans the whole batch, then writes a session per day
/// and prices them together.
/// </summary>
/// <remarks>
/// <para>
/// This is orchestration only - every decision lives in
/// <see cref="DailySessionPlanner"/>, which is pure and tested on its own. What
/// happens here is the ordering that keeps a batch honest: the logs are parsed
/// ONCE for the whole range, the saved sessions are read ONCE before anything is
/// written, and only then are the days written one at a time.
/// </para>
/// <para>
/// Reading the saved sessions up front is what stops the batch feeding itself.
/// Every session this runner creates is itself a saved, non-archived session, so
/// a query between days would return the previous day's work as an overlap - and
/// with merging on, the whole range would chain into a single blob.
/// </para>
/// <para>
/// Each day is written in its own transaction, so one bad day cannot lose the
/// others; it is recorded as a failure and the batch carries on.
/// </para>
/// <para>
/// Nothing here touches the UI, and every await is context-free, because this is
/// expected to run on a background thread: the SQLite wrapper completes its work
/// synchronously, so a long batch run inline on the dispatcher would block every
/// repaint until it finished. Callers report progress through an
/// <see cref="IProgress{T}"/>, which is what marshals back to the UI thread.
/// </para>
/// </remarks>
public sealed class DailyHistorySessionRunner
{
    private readonly IHistoricalGamelogReader _reader;
    private readonly ISessionSpanQuery _spans;
    private readonly IHistoricalSessionBuilder _builder;
    private readonly IHistoricalMarketPricer _pricer;
    private readonly ILogger<DailyHistorySessionRunner> _log;

    /// <summary>Construct the runner.</summary>
    /// <param name="reader">Gamelog inventory + window reader.</param>
    /// <param name="spans">Saved-session span query.</param>
    /// <param name="builder">Writes one session per day.</param>
    /// <param name="pricer">Prices the built sessions in one batch.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public DailyHistorySessionRunner(
        IHistoricalGamelogReader reader,
        ISessionSpanQuery spans,
        IHistoricalSessionBuilder builder,
        IHistoricalMarketPricer pricer,
        ILogger<DailyHistorySessionRunner> log)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _spans = spans ?? throw new ArgumentNullException(nameof(spans));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _pricer = pricer ?? throw new ArgumentNullException(nameof(pricer));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Work out what the range holds, without writing anything.
    /// </summary>
    /// <param name="request">The range and the merge choice.</param>
    /// <param name="progress">Optional progress sink for the survey and read phases.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The days that would be built and the days that would be skipped.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if the range is inverted or the name template is blank.</exception>
    /// <remarks>
    /// Separate from <see cref="ExecuteAsync"/> so the dialog can show the user
    /// exactly what it is about to do and then do precisely that - the preview and
    /// the build are the same plan object, not two walks of the same logic that
    /// could drift apart.
    /// </remarks>
    public async Task<DailyHistoryPlan> PlanAsync(
        DailyHistoryRunRequest request,
        IProgress<DailyHistoryProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RangeStop < request.RangeStart) throw new ArgumentException("RangeStop must not precede RangeStart.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.NameTemplate)) throw new ArgumentException("NameTemplate must not be blank.", nameof(request));

        progress?.Report(new DailyHistoryProgress("Surveying the gamelogs", 0, 0));
        var inventory = await _reader.InventoryAsync(ct).ConfigureAwait(false);
        if (inventory.Pilots.Count == 0)
        {
            return new DailyHistoryPlan(
                new DailyPlanResult(Array.Empty<DailySessionPlan>(), Array.Empty<DailySessionSkip>()),
                0,
                "No gamelogs were found, so there is nothing to build.");
        }

        progress?.Report(new DailyHistoryProgress("Reading the gamelogs", 0, 0));
        var read = await _reader.ReadAsync(
            inventory.Pilots.Select(p => p.PilotId).ToList(), request.RangeStart, request.RangeStop, ct).ConfigureAwait(false);

        var spans = await _spans.GetAllAsync(ct: ct).ConfigureAwait(false);
        var plan = DailySessionPlanner.Plan(
            read.Events,
            spans,
            PilotRoleHistory.Resolve(spans),
            inventory.Pilots.ToDictionary(p => p.PilotId, p => p.Name),
            request.RangeStart,
            request.RangeStop,
            TimeSpan.FromMinutes(request.IdleMinutes),
            request.MergeSameDaySessions);

        _log.LogInformation(
            "Daily history plan for {Start:O}..{Stop:O}: {Events} event(s) from {Files} file(s) for {Pilots} pilot(s) -> {Plans} session(s) to build, {Skipped} day(s) skipped (merge same-day: {Merge}).",
            request.RangeStart, request.RangeStop, read.Events.Count, read.FilesRead.Count, inventory.Pilots.Count,
            plan.Plans.Count, plan.Skipped.Count, request.MergeSameDaySessions);

        return new DailyHistoryPlan(plan, inventory.Pilots.Count, null);
    }

    /// <summary>
    /// Write the sessions a plan describes, one transaction per day, then price
    /// them together.
    /// </summary>
    /// <param name="plan">A plan from <see cref="PlanAsync"/>.</param>
    /// <param name="request">The same request the plan was made from (its template and notes are applied here).</param>
    /// <param name="progress">Optional progress sink, reported per day.</param>
    /// <param name="ct">Cancellation token, checked between days.</param>
    /// <returns>What was built, skipped and failed, plus a one-line summary.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either argument is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="ct"/> is cancelled; days already written stay written.</exception>
    public async Task<DailyHistoryRunResult> ExecuteAsync(
        DailyHistoryPlan plan,
        DailyHistoryRunRequest request,
        IProgress<DailyHistoryProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(request);

        if (plan.Note is not null) return Empty(plan.Note);
        if (plan.Result.Plans.Count == 0)
        {
            return new DailyHistoryRunResult(
                Array.Empty<DailyHistoryBuilt>(), plan.Result.Skipped, Array.Empty<DailyHistoryFailure>(),
                Array.Empty<HistoricalPricingResult>(),
                Describe(Array.Empty<DailyHistoryBuilt>(), plan.Result.Skipped, Array.Empty<DailyHistoryFailure>(), Array.Empty<HistoricalPricingResult>()));
        }

        var built = new List<DailyHistoryBuilt>();
        var failures = new List<DailyHistoryFailure>();
        var done = 0;
        foreach (var day in plan.Result.Plans)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new DailyHistoryProgress(HistoricalPricingSummary.Day(day.Day), done, plan.Result.Plans.Count));

            var name = SessionNameTemplateExpander.Expand(
                request.NameTemplate,
                day.Roster.Select(m => new Character(m.PilotId, m.Name, string.Empty)).ToList(),
                day.WindowStart,
                windowStart: day.WindowStart,
                windowStop: day.WindowStop);

            try
            {
                var result = await _builder.BuildAsync(
                    new HistoricalSessionRequest(
                        name,
                        request.Notes,
                        day.WindowStart,
                        day.WindowStop,
                        day.Roster,
                        day.Events,
                        day.Merge.Select(s => s.SessionId).ToList(),
                        day.Exclusions,
                        request.IdleMinutes),
                    ct).ConfigureAwait(false);
                built.Add(new DailyHistoryBuilt(day.Day, result.SessionId, name, result));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or DbException or IOException)
            {
                // One day's problem - a name that already exists, a source that
                // moved under us - must not cost the other days their work.
                _log.LogWarning(ex, "Building the historical session for {Day} failed; continuing with the rest of the batch.", day.Day);
                failures.Add(new DailyHistoryFailure(day.Day, name, ex.Message));
            }

            done++;
        }

        progress?.Report(new DailyHistoryProgress("Pricing", done, plan.Result.Plans.Count));
        var pricing = built.Count == 0
            ? Array.Empty<HistoricalPricingResult>()
            : await _pricer.PriceBatchAsync(
                built.Select(b => new HistoricalPricingTarget(b.SessionId, b.Result.EffectiveStart, b.Result.EffectiveStop)).ToList(),
                ct).ConfigureAwait(false);

        var summary = Describe(built, plan.Result.Skipped, failures, pricing);
        _log.LogInformation("Daily history batch finished: {Summary}", summary);
        return new DailyHistoryRunResult(built, plan.Result.Skipped, failures, pricing, summary);
    }

    /// <summary>
    /// Plan the range and build it in one call.
    /// </summary>
    /// <param name="request">The range and the merge choice.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was built, skipped and failed, plus a one-line summary.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if the range is inverted or the name template is blank.</exception>
    public async Task<DailyHistoryRunResult> RunAsync(
        DailyHistoryRunRequest request,
        IProgress<DailyHistoryProgress>? progress = null,
        CancellationToken ct = default)
    {
        var plan = await PlanAsync(request, progress, ct).ConfigureAwait(false);
        return await ExecuteAsync(plan, request, progress, ct).ConfigureAwait(false);
    }

    private static DailyHistoryRunResult Empty(string summary) =>
        new(Array.Empty<DailyHistoryBuilt>(), Array.Empty<DailySessionSkip>(), Array.Empty<DailyHistoryFailure>(),
            Array.Empty<HistoricalPricingResult>(), summary);

    /// <summary>Compose the one-line outcome the Reports status bar shows.</summary>
    private static string Describe(
        IReadOnlyList<DailyHistoryBuilt> built,
        IReadOnlyList<DailySessionSkip> skipped,
        IReadOnlyList<DailyHistoryFailure> failures,
        IReadOnlyList<HistoricalPricingResult> pricing)
    {
        var parts = new List<string>();
        if (built.Count == 0)
        {
            parts.Add("No sessions were built.");
        }
        else
        {
            var first = HistoricalPricingSummary.Day(built[0].Day);
            var last = HistoricalPricingSummary.Day(built[^1].Day);
            var span = built.Count == 1 ? first : first + " to " + last;
            var rows = built.Sum(b => b.Result.CopiedLogRows + b.Result.ParsedLogRows);
            var archived = built.Sum(b => b.Result.ArchivedSessionIds.Count);
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Built {built.Count} session(s) for {span}: {rows} mining rows, {archived} saved session(s) folded in."));
        }

        if (skipped.Count > 0) parts.Add($"{skipped.Count} day(s) skipped.");
        if (failures.Count > 0) parts.Add($"{failures.Count} day(s) failed: {failures[0].Message}");

        var prices = HistoricalPricingSummary.DescribeBatch(pricing);
        if (prices.Length > 0) parts.Add(prices);

        return string.Join(" ", parts);
    }
}
