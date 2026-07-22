namespace EveDashboard.App.Wpf.Dialogs;

using EveDashboard.Core.Layout;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;

/// <summary>
/// Throwing stub for <see cref="ISettingsRepository"/> used by
/// <see cref="SettingsDialogViewModel"/> when the dialog is opened in
/// bootstrap-failure mode (plan 22.2 decisions 22.T / 22.U).
/// </summary>
/// <remarks>
/// <para>
/// Per <c>MEMORY.md -&gt; feedback_no_test_shimming</c>: this stub is NOT a shim
/// around unbuilt code — the real <c>SqliteSettingsRepository</c> ships in plan
/// 14. The stub exists because at the bootstrap-failure points (line 84 / 102
/// in <c>App.xaml.cs</c>), <c>SqliteSettingsRepository</c> has not yet been
/// constructed (it depends on the session SQLite which hasn't been opened).
/// The dialog's <c>IsBootstrapFailureMode</c> gate (on
/// <see cref="SettingsDialogViewModel"/>) suppresses every reachable call to
/// this type; the throws exist as defense in depth so a future regression
/// that breaks the gate fails loudly.
/// </para>
/// <para>
/// Singleton via <see cref="Instance"/>. The constructor is private to prevent
/// accidental DI registration as transient.
/// </para>
/// </remarks>
internal sealed class NoOpSettingsRepository : ISettingsRepository
{
    /// <summary>Singleton instance — registered in the bootstrap-failure mini-container.</summary>
    public static readonly NoOpSettingsRepository Instance = new();

    private const string Reason = "Settings repository not available in bootstrap-failure mode.";

    private NoOpSettingsRepository() { }

    /// <inheritdoc />
    public Task<ThemeMode> GetThemeAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetThemeAsync(ThemeMode value, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<PriceMode> GetPriceModeAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetPriceModeAsync(PriceMode value, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<double> GetRefiningYieldPercentAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetRefiningYieldPercentAsync(double value, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<long> GetSelectedStationIdAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetSelectedStationIdAsync(long stationId, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<WindowGeometry?> GetWindowGeometryAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<WidgetLayout?> GetWidgetLayoutAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetWidgetLayoutAsync(WidgetLayout value, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<WidgetStatsConfig?> GetSessionStatsConfigAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetSessionStatsConfigAsync(WidgetStatsConfig value, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<WidgetStatsConfig?> GetPerPilotStatsConfigAsync(long pilotId, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetPerPilotStatsConfigAsync(long pilotId, WidgetStatsConfig value, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<WindowGeometry?> GetReportsWindowGeometryAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetReportsWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<WindowGeometry?> GetCrystalDiagramWindowGeometryAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetCrystalDiagramWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<WindowGeometry?> GetLifetimeTotalsWindowGeometryAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetLifetimeTotalsWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<WindowGeometry?> GetAboutWindowGeometryAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetAboutWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<string?> GetSelectedCrystalAnomalyAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetSelectedCrystalAnomalyAsync(string value, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<string?> GetDefaultFleetIdAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetDefaultFleetIdAsync(string? fleetId, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<PilotSortOrder> GetPilotSortOrderAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetPilotSortOrderAsync(PilotSortOrder order, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<bool> GetAutoShowRefinedMaterialsAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetAutoShowRefinedMaterialsAsync(bool enabled, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<bool> GetShowPilotRefinedOutputAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetShowPilotRefinedOutputAsync(bool enabled, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<long, bool>> GetRefinedMaterialsVisibilityAsync(string scopeKey, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetRefinedMaterialsVisibilityAsync(string scopeKey, IReadOnlyDictionary<long, bool> visibility, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<double?> GetReportsSidebarWidthAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetReportsSidebarWidthAsync(double value, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<int> GetAutoPauseIdleMinutesAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetAutoPauseIdleMinutesAsync(int minutes, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<bool> GetAutoPauseSoundEnabledAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetAutoPauseSoundEnabledAsync(bool enabled, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<int> GetAutoPauseSoundVolumeAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetAutoPauseSoundVolumeAsync(int volume, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<int, double>?> GetReportsColumnWidthsAsync(string gridKey, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetReportsColumnWidthsAsync(string gridKey, IReadOnlyDictionary<int, double> widths, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, StatEmphasis>?> GetReportsTextSizeAsync(string widgetTypeKey, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task SetReportsTextSizeAsync(string widgetTypeKey, IReadOnlyDictionary<string, StatEmphasis> textSize, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task EnsureSeedDefaultsAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public event EventHandler<SettingChangedEventArgs>? SettingChanged
    {
        add { /* no-op: bootstrap-failure dialog never subscribes. */ }
        remove { /* no-op */ }
    }
}

/// <summary>
/// Throwing stub for <see cref="IMarketStationCatalog"/> used in
/// bootstrap-failure mode. See <see cref="NoOpSettingsRepository"/> for the
/// rationale.
/// </summary>
internal sealed class NoOpMarketStationCatalog : IMarketStationCatalog
{
    /// <summary>Singleton instance — registered in the bootstrap-failure mini-container.</summary>
    public static readonly NoOpMarketStationCatalog Instance = new();

    private const string Reason = "Market station catalog not available in bootstrap-failure mode.";

    private NoOpMarketStationCatalog() { }

    /// <inheritdoc />
    public Task<IReadOnlyList<MarketStation>> GetAllAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task<MarketStation?> GetAsync(long stationId, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task AddAsync(MarketStation station, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task UpdateAsync(MarketStation station, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task RemoveAsync(long stationId, CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);

    /// <inheritdoc />
    public Task EnsureSeedDefaultsAsync(CancellationToken ct = default)
        => throw new InvalidOperationException(Reason);
}

/// <summary>
/// Throwing stub for <see cref="IAddStationDialog"/> used in bootstrap-failure
/// mode. See <see cref="NoOpSettingsRepository"/> for the rationale.
/// </summary>
internal sealed class NoOpAddStationDialog : IAddStationDialog
{
    /// <summary>Singleton instance — registered in the bootstrap-failure mini-container.</summary>
    public static readonly NoOpAddStationDialog Instance = new();

    private NoOpAddStationDialog() { }

    /// <inheritdoc />
    public MarketStation? Show()
        => throw new InvalidOperationException("Add-station dialog not available in bootstrap-failure mode.");
}
