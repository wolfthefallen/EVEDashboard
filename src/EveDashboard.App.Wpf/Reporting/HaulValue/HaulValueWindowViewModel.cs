namespace EveDashboard.App.Wpf.Reporting.HaulValue;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Settings;
using EveDashboard.MarketData.Pricing;
using EveDashboard.Reporting.Queries;
using EveDashboard.Stats.Queries;
using Microsoft.Extensions.Logging;

/// <summary>A saved session offered in the Haul Value session picker (multi-select).</summary>
public sealed partial class SessionPickItem : ObservableObject
{
    /// <summary>Session sha256 id.</summary>
    public string SessionId { get; }

    /// <summary>Human label (session name).</summary>
    public string Display { get; }

    /// <summary>Whether this session is checked for inclusion in the haul.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Construct a picker item.</summary>
    /// <param name="sessionId">Session sha256 id.</param>
    /// <param name="display">Session display name.</param>
    public SessionPickItem(string sessionId, string display)
    {
        SessionId = sessionId;
        Display = display;
    }
}

/// <summary>One station row in the results grid (pre-formatted for display).</summary>
/// <param name="Station">Station label.</param>
/// <param name="TotalValue">Total haul value, formatted with thousands separators.</param>
/// <param name="Note">Empty, or "(N unpriced)" when some lines had no order there.</param>
public sealed record StationValueRow(string Station, string TotalValue, string Note);

/// <summary>One per-material row in the breakdown grid for the best station.</summary>
/// <param name="Name">Material/item name.</param>
/// <param name="Quantity">Units, formatted.</param>
/// <param name="UnitPrice">Per-unit price, formatted, or "-" if unpriced.</param>
/// <param name="Value">Line value, formatted, or "-" if unpriced.</param>
public sealed record LineValueRow(string Name, string Quantity, string UnitPrice, string Value);

/// <summary>
/// View-model for the Haul Value window (plan 55): price a haul across every
/// station in the market catalog at once and show where it is worth the most.
/// Two input modes — a pasted item list (any items, resolved via
/// <see cref="ISdeTypeNameResolver"/>) or a saved session's refined output.
/// Uses the plan-53 price-source router (via <see cref="HaulValuationCalculator"/>),
/// so results honour the user's GoonMetrics/ESI choice; ad-hoc pricing never
/// writes to <c>market_data</c>.
/// </summary>
public sealed partial class HaulValueWindowViewModel : ObservableObject
{
    private readonly IReportingQueries _reporting;
    private readonly ISessionStatsRepository _stats;
    private readonly IMarketStationCatalog _stations;
    private readonly ISettingsRepository _settings;
    private readonly ISdeTypeNameResolver _nameResolver;
    private readonly HaulValuationCalculator _calculator;
    private readonly ILogger<HaulValueWindowViewModel> _log;

    /// <summary>Saved sessions available in the session-mode picker.</summary>
    public ObservableCollection<SessionPickItem> Sessions { get; } = new();

    /// <summary>Per-station totals, best first, after the last valuation.</summary>
    public ObservableCollection<StationValueRow> StationResults { get; } = new();

    /// <summary>Per-material breakdown for the best-valued station.</summary>
    public ObservableCollection<LineValueRow> Breakdown { get; } = new();

    /// <summary><c>true</c> = value the pasted list; <c>false</c> = value the selected session.</summary>
    [ObservableProperty]
    private bool _usePasteMode = true;

    /// <summary>The pasted haul text (name / quantity per line).</summary>
    [ObservableProperty]
    private string _pasteText = string.Empty;

    /// <summary>Status / summary line shown under the results.</summary>
    [ObservableProperty]
    private string _statusText = "Paste a haul or pick session(s), then Value haul.";

    /// <summary>Header over the breakdown grid; names the best station after a valuation.</summary>
    [ObservableProperty]
    private string _breakdownHeader = "Breakdown (best station)";

    /// <summary><c>true</c> while a valuation is in flight (disables the button).</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Construct the view-model.</summary>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public HaulValueWindowViewModel(
        IReportingQueries reporting,
        ISessionStatsRepository stats,
        IMarketStationCatalog stations,
        ISettingsRepository settings,
        ISdeTypeNameResolver nameResolver,
        HaulValuationCalculator calculator,
        ILogger<HaulValueWindowViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(reporting);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(nameResolver);
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(log);
        _reporting = reporting;
        _stats = stats;
        _stations = stations;
        _settings = settings;
        _nameResolver = nameResolver;
        _calculator = calculator;
        _log = log;
    }

    /// <summary>
    /// (Re)load the saved-session picker. Called when the window opens and on each
    /// re-show, so a just-finished session appears. Preserves the user's current
    /// checkbox selections by session id.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task RefreshSessionsAsync(CancellationToken ct = default)
    {
        var report = await _reporting.GetAllSessionSummariesAsync(ct).ConfigureAwait(true);
        var previouslyChecked = Sessions.Where(s => s.IsSelected).Select(s => s.SessionId).ToHashSet();

        Sessions.Clear();
        foreach (var s in report.Sessions)
        {
            Sessions.Add(new SessionPickItem(s.SessionId, s.Name)
            {
                IsSelected = previouslyChecked.Contains(s.SessionId),
            });
        }
    }

    /// <summary>
    /// Build the haul from the active input mode, price it at every catalog
    /// station, and populate the results. Never throws to the caller — input and
    /// pricing problems surface via <see cref="StatusText"/>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task ValueHaulAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var (haul, inputNote) = UsePasteMode
                ? await BuildFromPasteAsync(ct).ConfigureAwait(true)
                : await BuildFromSessionAsync(ct).ConfigureAwait(true);

            StationResults.Clear();
            Breakdown.Clear();
            BreakdownHeader = "Breakdown (best station)";

            if (haul.Count == 0)
            {
                StatusText = inputNote.Length > 0 ? inputNote : "Nothing to value.";
                return;
            }

            var stations = (await _stations.GetAllAsync(ct).ConfigureAwait(true))
                .Select(s => (s.StationId, Label: StationLabel(s)))
                .ToList();
            if (stations.Count == 0)
            {
                StatusText = "No market stations configured (add one in Settings).";
                return;
            }

            var mode = await _settings.GetPriceModeAsync(ct).ConfigureAwait(true);
            var valuation = await _calculator.ValueAsync(haul, stations, mode, ct).ConfigureAwait(true);

            foreach (var st in valuation.Stations)
            {
                var note = st.UnpricedCount > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"({st.UnpricedCount} unpriced)")
                    : string.Empty;
                StationResults.Add(new StationValueRow(st.StationLabel, FormatIsk(st.TotalValue), note));
            }

            // Breakdown = the best (top) station's per-line detail.
            var best = valuation.Stations.FirstOrDefault();
            if (best is not null)
            {
                BreakdownHeader = $"Breakdown - {best.StationLabel}";
                foreach (var line in best.Lines.OrderByDescending(l => l.LineValue ?? -1))
                {
                    Breakdown.Add(new LineValueRow(
                        line.Name,
                        line.Quantity.ToString("N0", CultureInfo.InvariantCulture),
                        line.UnitPrice is double up ? up.ToString("N2", CultureInfo.InvariantCulture) : "-",
                        line.LineValue is double lv ? FormatIsk(lv) : "-"));
                }
            }

            var modeText = mode.ToString().ToLowerInvariant();
            StatusText = string.Create(CultureInfo.InvariantCulture,
                $"Valued {haul.Count} item(s) at {stations.Count} station(s) using {modeText} prices.{(inputNote.Length > 0 ? " " + inputNote : string.Empty)}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Haul valuation failed.");
            StatusText = "Valuation failed - see the log. Prices may be temporarily unreachable.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Parse + resolve the pasted list into haul lines; note any unresolved names.</summary>
    private async Task<(IReadOnlyList<HaulLine> Haul, string Note)> BuildFromPasteAsync(CancellationToken ct)
    {
        var parsed = EveInventoryPasteParser.Parse(PasteText);
        if (parsed.Count == 0)
        {
            return (System.Array.Empty<HaulLine>(), "Paste box is empty.");
        }

        var haul = new List<HaulLine>();
        var unresolved = new List<string>();
        foreach (var line in parsed)
        {
            var typeId = await _nameResolver.ResolveTypeIdAsync(line.Name, ct).ConfigureAwait(true);
            if (typeId is long id)
            {
                haul.Add(new HaulLine(id, line.Name, line.Quantity));
            }
            else
            {
                unresolved.Add(line.Name);
            }
        }

        var note = unresolved.Count == 0
            ? string.Empty
            : $"Not found: {string.Join(", ", unresolved.Take(8))}{(unresolved.Count > 8 ? ", ..." : string.Empty)}.";
        return (haul, note);
    }

    /// <summary>Sum the checked sessions' refined output into one haul.</summary>
    private async Task<(IReadOnlyList<HaulLine> Haul, string Note)> BuildFromSessionAsync(CancellationToken ct)
    {
        var selected = Sessions.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return (System.Array.Empty<HaulLine>(), "Pick at least one session first.");
        }

        // Gather each session's refined output and sum per material across them via
        // the shared aggregator, so several sessions combine into one haul.
        var perSession = new List<IReadOnlyList<RefinedMaterialAmount>>(selected.Count);
        foreach (var s in selected)
        {
            var stats = await _stats.GetSessionStatsAsync(
                s.SessionId, DateTimeOffset.UtcNow, allowLazyFetch: false, ct).ConfigureAwait(true);
            perSession.Add(stats.RefinedOutput);
        }

        var haul = RefinedOutputAggregator.SumAcross(perSession)
            .Where(m => m.Quantity > 0)
            .Select(m => new HaulLine(m.TypeId, m.Name, m.Quantity))
            .ToList();
        var note = haul.Count == 0
            ? "The selected session(s) have no refined output to value."
            : (selected.Count > 1 ? $"Combined {selected.Count} sessions." : string.Empty);
        return (haul, note);
    }

    private static string StationLabel(MarketStation s) =>
        string.IsNullOrWhiteSpace(s.StationName) ? s.SystemName : $"{s.SystemName} - {s.StationName}";

    private static string FormatIsk(double value) =>
        value.ToString("N0", CultureInfo.InvariantCulture) + " ISK";
}
