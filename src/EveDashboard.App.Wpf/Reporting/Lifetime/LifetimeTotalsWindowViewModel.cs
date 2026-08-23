namespace EveDashboard.App.Wpf.Reporting.Lifetime;

using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.Core.Settings;
using EveDashboard.Reporting.Queries;
using Microsoft.Extensions.Logging;

/// <summary>
/// VM for the standalone Lifetime Totals window. Owns the reusable
/// <see cref="LifetimeSummaryViewModel"/> widget VM and re-fetches the all-time
/// rollup (<see cref="IReportingQueries.GetAllSessionSummariesAsync"/>) on every
/// window show so the totals reflect sessions mined / imported / deleted while
/// the window was hidden.
/// </summary>
/// <remarks>
/// The window is a singleton hide-on-close (mirrors Reports / Crystal). Because
/// the lifetime rollup is global and has no in-window action that would refresh
/// it, the adapter drives <see cref="RefreshAsync"/> on each <c>Show</c>. The
/// owned <see cref="LifetimeSummaryViewModel"/> is <see cref="IDisposable"/>
/// (settings subscription for text size); the prior instance is disposed on
/// each refresh and on this VM's own <see cref="Dispose"/>.
/// </remarks>
public sealed partial class LifetimeTotalsWindowViewModel : ObservableObject, IDisposable
{
    private readonly IReportingQueries _queries;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<LifetimeTotalsWindowViewModel> _log;

    /// <summary>
    /// The reusable lifetime-rollup widget VM, or <c>null</c> before the first
    /// successful <see cref="RefreshAsync"/>. Bound by the window's
    /// <c>ContentPresenter</c>.
    /// </summary>
    [ObservableProperty]
    private LifetimeSummaryViewModel? _summary;

    /// <summary>Construct the window VM.</summary>
    /// <param name="queries">Reporting queries facade — supplies the all-time rollup.</param>
    /// <param name="settings">Settings repository (passed through to the widget VM for text size).</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public LifetimeTotalsWindowViewModel(
        IReportingQueries queries,
        ISettingsRepository settings,
        ILogger<LifetimeTotalsWindowViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);
        _queries = queries;
        _settings = settings;
        _log = log;
    }

    /// <summary>
    /// Re-fetch the all-time rollup and rebuild the widget VM. Re-fetched on
    /// every window show so the totals reflect sessions added / removed while
    /// the window was hidden. Disposes the prior <see cref="Summary"/> after the
    /// swap. On query failure the prior <see cref="Summary"/> is left in place
    /// (Warning logged).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var report = await _queries.GetAllSessionSummariesAsync(ct: ct).ConfigureAwait(true);
            var fresh = new LifetimeSummaryViewModel(report.Lifetime, _settings);
            var old = Summary;
            Summary = fresh;
            old?.Dispose();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "LifetimeTotalsWindowViewModel.RefreshAsync failed; keeping the prior rollup.");
        }
    }

    /// <summary>Dispose the currently-owned widget VM (releases its settings subscription).</summary>
    public void Dispose()
    {
        Summary?.Dispose();
    }
}
