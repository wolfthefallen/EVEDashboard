namespace EveDashboard.App.Wpf.Reporting.Widgets;

using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.App.Wpf.Widgets;
using EveDashboard.Reporting;
using EveDashboard.Reporting.Queries;
using Microsoft.Extensions.Logging;

/// <summary>
/// Plan 43 widget VM for the reporting "Refined output" table — Material |
/// Quantity columns, sortable. Reused at session-level (<c>pilotId == null</c>)
/// and per-pilot-level (<c>pilotId == &lt;id&gt;</c>) inside the Reports window.
/// </summary>
/// <remarks>
/// No stat-config popup and no auto-show / hide: reporting shows ALL refined
/// materials for the saved session (decision 1.3 = A). Column-click sort is
/// provided by the view's <c>DataGrid.CanUserSortColumns="True"</c>; refresh is
/// driven externally by <see cref="LoadAsync"/>;
/// <see cref="WidgetBaseViewModel.RefreshAsync"/> is a no-op. Mirrors
/// <see cref="PerOreBreakdownViewModel"/>.
/// </remarks>
public sealed partial class PerRefinedBreakdownViewModel : WidgetBaseViewModel
{
    private readonly IReportingQueries _queries;
    private readonly ILogger<PerRefinedBreakdownViewModel> _log;

    /// <summary>Bound rows. Replaced wholesale on each <see cref="LoadAsync"/>.</summary>
    [ObservableProperty]
    private IReadOnlyList<RefineMatBreakdownRow> _rows = Array.Empty<RefineMatBreakdownRow>();

    /// <summary>Title text shown in the widget header (set by parent based on session/pilot scope).</summary>
    [ObservableProperty]
    private string _title = "Refined Output";

    /// <summary>Construct the per-refined breakdown VM.</summary>
    /// <param name="queries">Reporting queries facade.</param>
    /// <param name="log">Logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public PerRefinedBreakdownViewModel(
        IReportingQueries queries,
        ILogger<PerRefinedBreakdownViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(log);
        _queries = queries;
        _log = log;
    }

    /// <summary>
    /// Load the refined-output breakdown for a given session (and optional pilot
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
            Rows = await _queries.GetRefinedMatBreakdownAsync(sessionId, pilotId, ct).ConfigureAwait(true);
            LastUpdated = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "PerRefinedBreakdownViewModel.LoadAsync(sessionId={SessionId}, pilotId={PilotId}) failed; rendering empty.",
                sessionId, pilotId);
            Rows = Array.Empty<RefineMatBreakdownRow>();
        }
    }

    /// <inheritdoc />
    public override Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
}
