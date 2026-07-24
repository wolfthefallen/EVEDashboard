namespace EveDashboard.MarketData.Tests.Pricing;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EveDashboard.Core.Layout;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;

/// <summary>
/// Shared in-memory <see cref="ISettingsRepository"/> test double for the
/// MarketData tests. Only the pricing-relevant knobs are settable
/// (<see cref="SelectedStationId"/>, <see cref="PriceMode"/>,
/// <see cref="PriceSource"/>, <see cref="RefiningYieldPercent"/>); everything else
/// returns a benign default. <c>SetSelectedStationIdAsync</c> and
/// <c>SetPriceSourceAsync</c> raise <see cref="SettingChanged"/> so reactive
/// paths (the fetcher's station-change re-fetch, the price-source router) can be
/// exercised.
/// </summary>
internal sealed class StubSettingsRepository : ISettingsRepository
{
    public long SelectedStationId { get; set; } = 60003760L;
    public PriceMode PriceMode { get; set; } = PriceMode.Buy;
    public double RefiningYieldPercent { get; set; } = 90.6;
    public PriceSource PriceSource { get; set; } = PriceSource.GoonMetrics;

    public Task<ThemeMode> GetThemeAsync(CancellationToken ct = default) => Task.FromResult(ThemeMode.Dark);
    public Task SetThemeAsync(ThemeMode value, CancellationToken ct = default) => Task.CompletedTask;
    public Task<PriceMode> GetPriceModeAsync(CancellationToken ct = default) => Task.FromResult(PriceMode);
    public Task SetPriceModeAsync(PriceMode value, CancellationToken ct = default) => Task.CompletedTask;
    public Task<PriceSource> GetPriceSourceAsync(CancellationToken ct = default) => Task.FromResult(PriceSource);
    public Task SetPriceSourceAsync(PriceSource value, CancellationToken ct = default)
    {
        PriceSource = value;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("price_source"));
        return Task.CompletedTask;
    }
    public Task<double> GetRefiningYieldPercentAsync(CancellationToken ct = default) => Task.FromResult(RefiningYieldPercent);
    public Task SetRefiningYieldPercentAsync(double value, CancellationToken ct = default) => Task.CompletedTask;
    public Task<long> GetSelectedStationIdAsync(CancellationToken ct = default) => Task.FromResult(SelectedStationId);
    public Task SetSelectedStationIdAsync(long stationId, CancellationToken ct = default)
    {
        // Mirror the real SqliteSettingsRepository: persist AND raise SettingChanged
        // so the fetcher's station-change re-fetch (decision 1.4) is exercised.
        SelectedStationId = stationId;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("selected_station_id"));
        return Task.CompletedTask;
    }
    public Task<WindowGeometry?> GetWindowGeometryAsync(CancellationToken ct = default) => Task.FromResult<WindowGeometry?>(null);
    public Task SetWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default) => Task.CompletedTask;
    public Task<WidgetLayout?> GetWidgetLayoutAsync(CancellationToken ct = default) => Task.FromResult<WidgetLayout?>(null);
    public Task SetWidgetLayoutAsync(WidgetLayout value, CancellationToken ct = default) => Task.CompletedTask;
    public Task<WidgetStatsConfig?> GetSessionStatsConfigAsync(CancellationToken ct = default) => Task.FromResult<WidgetStatsConfig?>(null);
    public Task SetSessionStatsConfigAsync(WidgetStatsConfig value, CancellationToken ct = default) => Task.CompletedTask;
    public Task<WidgetStatsConfig?> GetPerPilotStatsConfigAsync(long pilotId, CancellationToken ct = default) => Task.FromResult<WidgetStatsConfig?>(null);
    public Task SetPerPilotStatsConfigAsync(long pilotId, WidgetStatsConfig value, CancellationToken ct = default) => Task.CompletedTask;
    public Task<WindowGeometry?> GetReportsWindowGeometryAsync(CancellationToken ct = default) => Task.FromResult<WindowGeometry?>(null);
    public Task SetReportsWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default) => Task.CompletedTask;
    public Task<WindowGeometry?> GetCrystalDiagramWindowGeometryAsync(CancellationToken ct = default) => Task.FromResult<WindowGeometry?>(null);
    public Task SetCrystalDiagramWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default) => Task.CompletedTask;
    public Task<WindowGeometry?> GetLifetimeTotalsWindowGeometryAsync(CancellationToken ct = default) => Task.FromResult<WindowGeometry?>(null);
    public Task SetLifetimeTotalsWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default) => Task.CompletedTask;
    public Task<WindowGeometry?> GetAboutWindowGeometryAsync(CancellationToken ct = default) => Task.FromResult<WindowGeometry?>(null);
    public Task SetAboutWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default) => Task.CompletedTask;
    public Task<string?> GetSelectedCrystalAnomalyAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task SetSelectedCrystalAnomalyAsync(string value, CancellationToken ct = default) => Task.CompletedTask;
    public Task<string?> GetDefaultFleetIdAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task SetDefaultFleetIdAsync(string? fleetId, CancellationToken ct = default) => Task.CompletedTask;
    public Task<EveDashboard.Core.Sessions.PilotSortOrder> GetPilotSortOrderAsync(CancellationToken ct = default) => Task.FromResult(EveDashboard.Core.Sessions.PilotSortOrder.LastActiveNewest);
    public Task SetPilotSortOrderAsync(EveDashboard.Core.Sessions.PilotSortOrder order, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> GetAutoShowRefinedMaterialsAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task SetAutoShowRefinedMaterialsAsync(bool enabled, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> GetShowPilotRefinedOutputAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task SetShowPilotRefinedOutputAsync(bool enabled, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyDictionary<long, bool>> GetRefinedMaterialsVisibilityAsync(string scopeKey, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<long, bool>>(new Dictionary<long, bool>());
    public Task SetRefinedMaterialsVisibilityAsync(string scopeKey, IReadOnlyDictionary<long, bool> visibility, CancellationToken ct = default) => Task.CompletedTask;
    public Task<double?> GetReportsSidebarWidthAsync(CancellationToken ct = default) => Task.FromResult<double?>(null);
    public Task SetReportsSidebarWidthAsync(double value, CancellationToken ct = default) => Task.CompletedTask;
    public Task<int> GetAutoPauseIdleMinutesAsync(CancellationToken ct = default) => Task.FromResult(5);
    public Task SetAutoPauseIdleMinutesAsync(int minutes, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> GetAutoPauseSoundEnabledAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task SetAutoPauseSoundEnabledAsync(bool enabled, CancellationToken ct = default) => Task.CompletedTask;
    public Task<int> GetAutoPauseSoundVolumeAsync(CancellationToken ct = default) => Task.FromResult(65);
    public Task SetAutoPauseSoundVolumeAsync(int volume, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyDictionary<int, double>?> GetReportsColumnWidthsAsync(string gridKey, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<int, double>?>(null);
    public Task SetReportsColumnWidthsAsync(string gridKey, IReadOnlyDictionary<int, double> widths, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyDictionary<string, StatEmphasis>?> GetReportsTextSizeAsync(string widgetTypeKey, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<string, StatEmphasis>?>(null);
    public Task SetReportsTextSizeAsync(string widgetTypeKey, IReadOnlyDictionary<string, StatEmphasis> textSize, CancellationToken ct = default) => Task.CompletedTask;
    public Task EnsureSeedDefaultsAsync(CancellationToken ct = default) => Task.CompletedTask;
    public event EventHandler<SettingChangedEventArgs>? SettingChanged;
}
