namespace EveDashboard.App.Wpf.Tests.Helpers;

using EveDashboard.Core.Layout;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;

/// <summary>
/// Hand-rolled fake of <see cref="ISettingsRepository"/> for unit tests
/// (plan 22.1 decision 22.M — no Moq / NSubstitute in this project).
/// Stored values round-trip through <c>Get*Async</c> and <c>Set*Async</c>;
/// each setter raises <see cref="SettingChanged"/> with the matching key
/// and counts invocations on the test-side counters.
/// </summary>
internal sealed class FakeSettingsRepository : ISettingsRepository
{
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    public PriceMode PriceMode { get; set; } = PriceMode.Buy;
    public double RefiningYieldPercent { get; set; } = 90.6;
    public long SelectedStationId { get; set; } = 60003760L;
    public WindowGeometry? WindowGeometry { get; set; }
    public WidgetLayout? WidgetLayout { get; set; }
    public WindowGeometry? ReportsWindowGeometry { get; set; }
    public WindowGeometry? CrystalDiagramWindowGeometry { get; set; }
    public string? SelectedCrystalAnomaly { get; set; }
    public double? ReportsSidebarWidth { get; set; }

    /// <summary>Plan 20 — singleton SessionStats stat config (one slot).</summary>
    public WidgetStatsConfig? SessionStatsConfig { get; set; }

    /// <summary>Plan 20 — per-pilot stat config (one row per pilot id).</summary>
    public Dictionary<long, WidgetStatsConfig> PerPilotStatsConfigs { get; } = new();

    public int SetThemeCount { get; private set; }
    public int SetPriceModeCount { get; private set; }
    public int SetRefiningYieldCount { get; private set; }
    public int SetSelectedStationIdCount { get; private set; }
    public int SetWidgetLayoutCount { get; private set; }
    public int SetSessionStatsConfigCount { get; private set; }
    public int SetPerPilotStatsConfigCount { get; private set; }

    public Task<ThemeMode> GetThemeAsync(CancellationToken ct = default) => Task.FromResult(Theme);
    public Task SetThemeAsync(ThemeMode value, CancellationToken ct = default)
    {
        Theme = value;
        SetThemeCount++;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("theme"));
        return Task.CompletedTask;
    }

    public Task<PriceMode> GetPriceModeAsync(CancellationToken ct = default) => Task.FromResult(PriceMode);
    public Task SetPriceModeAsync(PriceMode value, CancellationToken ct = default)
    {
        PriceMode = value;
        SetPriceModeCount++;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("price_mode"));
        return Task.CompletedTask;
    }

    public Task<double> GetRefiningYieldPercentAsync(CancellationToken ct = default) => Task.FromResult(RefiningYieldPercent);
    public Task SetRefiningYieldPercentAsync(double value, CancellationToken ct = default)
    {
        RefiningYieldPercent = value;
        SetRefiningYieldCount++;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("refining_yield_percent"));
        return Task.CompletedTask;
    }

    public Task<long> GetSelectedStationIdAsync(CancellationToken ct = default) => Task.FromResult(SelectedStationId);
    public Task SetSelectedStationIdAsync(long stationId, CancellationToken ct = default)
    {
        SelectedStationId = stationId;
        SetSelectedStationIdCount++;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("selected_station_id"));
        return Task.CompletedTask;
    }

    public Task<WindowGeometry?> GetWindowGeometryAsync(CancellationToken ct = default) => Task.FromResult(WindowGeometry);
    public Task SetWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        WindowGeometry = value;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("window_geometry"));
        return Task.CompletedTask;
    }

    public Task<WidgetLayout?> GetWidgetLayoutAsync(CancellationToken ct = default) => Task.FromResult(WidgetLayout);
    public Task SetWidgetLayoutAsync(WidgetLayout value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        WidgetLayout = value;
        SetWidgetLayoutCount++;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("widget_layout"));
        return Task.CompletedTask;
    }

    public Task<WidgetStatsConfig?> GetSessionStatsConfigAsync(CancellationToken ct = default) =>
        Task.FromResult(SessionStatsConfig);

    public Task SetSessionStatsConfigAsync(WidgetStatsConfig value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        SessionStatsConfig = value;
        SetSessionStatsConfigCount++;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("session_stats_config"));
        return Task.CompletedTask;
    }

    public Task<WidgetStatsConfig?> GetPerPilotStatsConfigAsync(long pilotId, CancellationToken ct = default) =>
        Task.FromResult(PerPilotStatsConfigs.TryGetValue(pilotId, out var cfg) ? cfg : null);

    public Task SetPerPilotStatsConfigAsync(long pilotId, WidgetStatsConfig value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        PerPilotStatsConfigs[pilotId] = value;
        SetPerPilotStatsConfigCount++;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs($"per_pilot_stats_config.{pilotId}"));
        return Task.CompletedTask;
    }

    public Task<WindowGeometry?> GetReportsWindowGeometryAsync(CancellationToken ct = default) =>
        Task.FromResult(ReportsWindowGeometry);

    public Task SetReportsWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        ReportsWindowGeometry = value;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("reports_window_geometry"));
        return Task.CompletedTask;
    }

    public Task<WindowGeometry?> GetCrystalDiagramWindowGeometryAsync(CancellationToken ct = default) =>
        Task.FromResult(CrystalDiagramWindowGeometry);

    public Task SetCrystalDiagramWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        CrystalDiagramWindowGeometry = value;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("crystal_diagram_window_geometry"));
        return Task.CompletedTask;
    }

    public int SetSelectedCrystalAnomalyCount { get; private set; }

    public Task<string?> GetSelectedCrystalAnomalyAsync(CancellationToken ct = default) =>
        Task.FromResult(SelectedCrystalAnomaly);

    public Task SetSelectedCrystalAnomalyAsync(string value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        SelectedCrystalAnomaly = value;
        SetSelectedCrystalAnomalyCount++;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("crystal_diagram_selected_anomaly"));
        return Task.CompletedTask;
    }

    public Task<double?> GetReportsSidebarWidthAsync(CancellationToken ct = default) =>
        Task.FromResult(ReportsSidebarWidth);

    public Task SetReportsSidebarWidthAsync(double value, CancellationToken ct = default)
    {
        ReportsSidebarWidth = value;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("reports_sidebar_width"));
        return Task.CompletedTask;
    }

    /// <summary>Plan 37 — auto-pause idle threshold in minutes.</summary>
    public int AutoPauseIdleMinutes { get; set; } = 5;

    /// <summary>Plan 37 — auto-pause / auto-resume notification sounds on/off.</summary>
    public bool AutoPauseSoundEnabled { get; set; } = true;

    public int SetAutoPauseIdleMinutesCount { get; private set; }
    public int SetAutoPauseSoundEnabledCount { get; private set; }

    public Task<int> GetAutoPauseIdleMinutesAsync(CancellationToken ct = default) =>
        Task.FromResult(AutoPauseIdleMinutes);

    public Task SetAutoPauseIdleMinutesAsync(int minutes, CancellationToken ct = default)
    {
        AutoPauseIdleMinutes = minutes;
        SetAutoPauseIdleMinutesCount++;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("auto_pause_idle_minutes"));
        return Task.CompletedTask;
    }

    public Task<bool> GetAutoPauseSoundEnabledAsync(CancellationToken ct = default) =>
        Task.FromResult(AutoPauseSoundEnabled);

    public Task SetAutoPauseSoundEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        AutoPauseSoundEnabled = enabled;
        SetAutoPauseSoundEnabledCount++;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("auto_pause_sound_enabled"));
        return Task.CompletedTask;
    }

    /// <summary>Plan 42 — default fleet id (null = no default).</summary>
    public string? DefaultFleetId { get; set; }

    /// <summary>Plan 42 — persisted pilot-picker sort order.</summary>
    public PilotSortOrder PilotSortOrder { get; set; } = PilotSortOrder.LastActiveNewest;

    public int SetDefaultFleetIdCount { get; private set; }
    public int SetPilotSortOrderCount { get; private set; }

    public Task<string?> GetDefaultFleetIdAsync(CancellationToken ct = default) =>
        Task.FromResult(DefaultFleetId);

    public Task SetDefaultFleetIdAsync(string? fleetId, CancellationToken ct = default)
    {
        DefaultFleetId = fleetId;
        SetDefaultFleetIdCount++;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("default_fleet_id"));
        return Task.CompletedTask;
    }

    public Task<PilotSortOrder> GetPilotSortOrderAsync(CancellationToken ct = default) =>
        Task.FromResult(PilotSortOrder);

    public Task SetPilotSortOrderAsync(PilotSortOrder order, CancellationToken ct = default)
    {
        PilotSortOrder = order;
        SetPilotSortOrderCount++;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("pilot_sort_order"));
        return Task.CompletedTask;
    }

    /// <summary>Plan 43 — refined-output auto-show toggle (default on).</summary>
    public bool AutoShowRefinedMaterials { get; set; } = true;

    /// <summary>Plan 43 — per-widget refined-materials visibility overrides, keyed by scope ("" or pilot id string).</summary>
    public Dictionary<string, IReadOnlyDictionary<long, bool>> RefinedMaterialsVisibility { get; } = new();

    public int SetAutoShowRefinedMaterialsCount { get; private set; }
    public int SetRefinedMaterialsVisibilityCount { get; private set; }

    public Task<bool> GetAutoShowRefinedMaterialsAsync(CancellationToken ct = default) =>
        Task.FromResult(AutoShowRefinedMaterials);

    public Task SetAutoShowRefinedMaterialsAsync(bool enabled, CancellationToken ct = default)
    {
        AutoShowRefinedMaterials = enabled;
        SetAutoShowRefinedMaterialsCount++;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs("auto_show_refined_materials"));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<long, bool>> GetRefinedMaterialsVisibilityAsync(string scopeKey, CancellationToken ct = default) =>
        Task.FromResult(RefinedMaterialsVisibility.TryGetValue(scopeKey, out var v) ? v : new Dictionary<long, bool>());

    public Task SetRefinedMaterialsVisibilityAsync(string scopeKey, IReadOnlyDictionary<long, bool> visibility, CancellationToken ct = default)
    {
        RefinedMaterialsVisibility[scopeKey] = visibility;
        SetRefinedMaterialsVisibilityCount++;
        var key = scopeKey.Length == 0 ? "refined_materials_visibility" : $"refined_materials_visibility.{scopeKey}";
        SettingChanged?.Invoke(this, new SettingChangedEventArgs(key));
        return Task.CompletedTask;
    }

    public Task EnsureSeedDefaultsAsync(CancellationToken ct = default) => Task.CompletedTask;

    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    /// <summary>Test seam: directly raise <see cref="SettingChanged"/> with the given key. Mirrors the production repo's <c>RaiseSettingChanged</c>.</summary>
    public void RaiseSettingChangedForTests(string key) =>
        SettingChanged?.Invoke(this, new SettingChangedEventArgs(key));
}
