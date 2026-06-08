namespace EveDashboard.App.Wpf.Reporting.Widgets;

using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.App.Wpf.Widgets;
using EveDashboard.Reporting;
using EveDashboard.Reporting.Queries;
using Microsoft.Extensions.Logging;

/// <summary>
/// Plan 25 widget VM for the per-ore breakdown table — Type | m^3 | ISK
/// columns, sortable. Reused at session-level (<c>pilotId == null</c>) and
/// per-pilot-level (<c>pilotId == &lt;id&gt;</c>) inside the Reports window.
/// </summary>
/// <remarks>
/// No stat-config popup (the existing popup model assumes a fixed key set;
/// ore types are dynamic per session). Column-click sort is provided by the
/// view's <c>DataGrid.CanUserSortColumns="True"</c>. Refresh is driven
/// externally by <see cref="LoadAsync"/>; <see cref="WidgetBaseViewModel.RefreshAsync"/>
/// is a no-op.
/// </remarks>
public sealed partial class PerOreBreakdownViewModel : WidgetBaseViewModel
{
    private readonly IReportingQueries _queries;
    private readonly ILogger<PerOreBreakdownViewModel> _log;

    /// <summary>Bound rows. Replaced wholesale on each <see cref="LoadAsync"/>.</summary>
    [ObservableProperty]
    private IReadOnlyList<OreBreakdownRow> _rows = Array.Empty<OreBreakdownRow>();

    /// <summary>Title text shown in the widget header (set by parent based on session/pilot scope).</summary>
    [ObservableProperty]
    private string _title = "Per-Ore Breakdown";

    /// <summary>Construct the per-ore breakdown VM.</summary>
    /// <param name="queries">Reporting queries facade.</param>
    /// <param name="log">Logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public PerOreBreakdownViewModel(
        IReportingQueries queries,
        ILogger<PerOreBreakdownViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(log);
        _queries = queries;
        _log = log;
    }

    /// <summary>
    /// Load the per-ore breakdown for a given session (and optional pilot
    /// filter). Replaces <see cref="Rows"/> with the query result; stamps
    /// <see cref="WidgetBaseViewModel.LastUpdated"/>.
    /// </summary>
    /// <param name="sessionId">Saved session id.</param>
    /// <param name="pilotId">Pilot id to filter by; null for session-wide.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadAsync(string sessionId, long? pilotId, CancellationToken ct = default)
    {
        try
        {
            Rows = await _queries.GetOreBreakdownAsync(sessionId, pilotId, ct).ConfigureAwait(true);
            LastUpdated = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "PerOreBreakdownViewModel.LoadAsync(sessionId={SessionId}, pilotId={PilotId}) failed; rendering empty.",
                sessionId, pilotId);
            Rows = Array.Empty<OreBreakdownRow>();
        }
    }

    /// <inheritdoc />
    public override Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
}
