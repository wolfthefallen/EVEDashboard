namespace EveDashboard.Stats.Settings;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EveDashboard.Core.Layout;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using EveDashboard.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="ISettingsRepository"/> implementation backed by an
/// injected <see cref="ISqliteDatabase"/>. Reads/writes the <c>setting</c>
/// key-value table directly on every call (no in-memory cache; settings are
/// touched at startup and on user-config changes only).
/// </summary>
/// <remarks>
/// <para>
/// Holds no mutable state beyond the <see cref="ISettingsRepository.SettingChanged"/>
/// event subscriptions — safe to register as a singleton in DI alongside the
/// singleton <see cref="ISqliteDatabase"/> (plan 16 wires both up at
/// <c>App.xaml.cs OnStartup</c>).
/// </para>
/// <para>
/// Validation policy mirrors plan 14 round-2 5.1: out-of-range numeric values
/// are clamped, unrecognised enum strings return the default with a Warning
/// log, malformed JSON returns <c>null</c> with a Warning log.
/// </para>
/// </remarks>
public sealed class SqliteSettingsRepository : ISettingsRepository
{
    private const double YieldMin = 50.0;
    private const double YieldMax = 90.6;
    private const double YieldDefault = 90.6;
    private const long DefaultStationId = 60003760L; // Jita

    // Plan 37 — auto-pause idle threshold bounds + default.
    private const int IdleMinutesMin = 1;
    private const int IdleMinutesMax = 60;
    private const int IdleMinutesDefault = 5;

    private static readonly JsonSerializerOptions WidgetLayoutJsonOptions = CreateWidgetLayoutJsonOptions();

    private static JsonSerializerOptions CreateWidgetLayoutJsonOptions()
    {
        var opts = new JsonSerializerOptions();
        opts.Converters.Add(new WidgetLayoutItemJsonConverter());
        opts.Converters.Add(new WidgetDescriptorJsonConverter());
        return opts;
    }

    private readonly ISqliteDatabase _db;
    private readonly ILogger<SqliteSettingsRepository> _log;

    /// <summary>Construct a settings repository over the given SQLite database.</summary>
    /// <param name="db">SQLite database wrapper. Typically the singleton
    ///     <c>SqliteDatabase</c> registered at app startup against the
    ///     session SQLite file.</param>
    /// <param name="log">Logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="db"/> or <paramref name="log"/> is null.
    /// </exception>
    public SqliteSettingsRepository(
        ISqliteDatabase db,
        ILogger<SqliteSettingsRepository> log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(log);
        _db = db;
        _log = log;
    }

    /// <inheritdoc />
    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    /// <inheritdoc />
    public async Task<ThemeMode> GetThemeAsync(CancellationToken ct = default)
    {
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'theme'", ct: ct);
        if (raw is null) return ThemeMode.Dark;
        return raw switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => LogWarnAndReturn(
                $"Unknown theme value '{raw}' in setting table - returning default 'dark'.",
                ThemeMode.Dark),
        };
    }

    /// <inheritdoc />
    public async Task SetThemeAsync(ThemeMode value, CancellationToken ct = default)
    {
        var stored = value == ThemeMode.Light ? "light" : "dark";
        await UpsertAsync("theme", stored, ct);
        RaiseSettingChanged("theme");
    }

    /// <inheritdoc />
    public async Task<PriceMode> GetPriceModeAsync(CancellationToken ct = default)
    {
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'price_mode'", ct: ct);
        if (raw is null) return PriceMode.Buy;
        return raw switch
        {
            "buy" => PriceMode.Buy,
            "sell" => PriceMode.Sell,
            "split" => PriceMode.Split,
            _ => LogWarnAndReturn(
                $"Unknown price_mode value '{raw}' in setting table - returning default 'buy'.",
                PriceMode.Buy),
        };
    }

    /// <inheritdoc />
    public async Task SetPriceModeAsync(PriceMode value, CancellationToken ct = default)
    {
        var stored = value switch
        {
            PriceMode.Buy => "buy",
            PriceMode.Sell => "sell",
            PriceMode.Split => "split",
            _ => "buy",
        };
        await UpsertAsync("price_mode", stored, ct);
        RaiseSettingChanged("price_mode");
    }

    /// <inheritdoc />
    public async Task<double> GetRefiningYieldPercentAsync(CancellationToken ct = default)
    {
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'refining_yield_percent'", ct: ct);
        if (raw is null) return YieldDefault;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            _log.LogWarning(
                "refining_yield_percent value '{Raw}' is not a valid number - returning default {Default}.",
                raw, YieldDefault);
            return YieldDefault;
        }
        return parsed;
    }

    /// <inheritdoc />
    public async Task SetRefiningYieldPercentAsync(double value, CancellationToken ct = default)
    {
        var clamped = Math.Clamp(value, YieldMin, YieldMax);
        if (clamped != value)
        {
            _log.LogWarning(
                "Refining yield {Original} out of range [{Min}, {Max}] - clamping to {Clamped}.",
                value, YieldMin, YieldMax, clamped);
        }
        await UpsertAsync(
            "refining_yield_percent",
            clamped.ToString("R", CultureInfo.InvariantCulture), ct);
        RaiseSettingChanged("refining_yield_percent");
    }

    /// <inheritdoc />
    public async Task<int> GetAutoPauseIdleMinutesAsync(CancellationToken ct = default)
    {
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'auto_pause_idle_minutes'", ct: ct);
        if (raw is null) return IdleMinutesDefault;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            _log.LogWarning(
                "auto_pause_idle_minutes value '{Raw}' is not a valid integer - returning default {Default}.",
                raw, IdleMinutesDefault);
            return IdleMinutesDefault;
        }
        var clamped = Math.Clamp(parsed, IdleMinutesMin, IdleMinutesMax);
        if (clamped != parsed)
        {
            _log.LogWarning(
                "auto_pause_idle_minutes stored value {Stored} out of range [{Min}, {Max}] - clamping to {Clamped}.",
                parsed, IdleMinutesMin, IdleMinutesMax, clamped);
        }
        return clamped;
    }

    /// <inheritdoc />
    public async Task SetAutoPauseIdleMinutesAsync(int minutes, CancellationToken ct = default)
    {
        var clamped = Math.Clamp(minutes, IdleMinutesMin, IdleMinutesMax);
        if (clamped != minutes)
        {
            _log.LogWarning(
                "auto_pause_idle_minutes {Original} out of range [{Min}, {Max}] - clamping to {Clamped}.",
                minutes, IdleMinutesMin, IdleMinutesMax, clamped);
        }
        await UpsertAsync(
            "auto_pause_idle_minutes",
            clamped.ToString(CultureInfo.InvariantCulture), ct);
        RaiseSettingChanged("auto_pause_idle_minutes");
    }

    /// <inheritdoc />
    public async Task<bool> GetAutoPauseSoundEnabledAsync(CancellationToken ct = default)
    {
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'auto_pause_sound_enabled'", ct: ct);
        if (raw is null) return true;
        return raw switch
        {
            "true" => true,
            "false" => false,
            _ => LogWarnAndReturn(
                $"Unknown auto_pause_sound_enabled value '{raw}' - returning default 'true'.",
                true),
        };
    }

    /// <inheritdoc />
    public async Task SetAutoPauseSoundEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        await UpsertAsync("auto_pause_sound_enabled", enabled ? "true" : "false", ct);
        RaiseSettingChanged("auto_pause_sound_enabled");
    }

    /// <inheritdoc />
    public async Task<long> GetSelectedStationIdAsync(CancellationToken ct = default)
    {
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'selected_station_id'", ct: ct);
        if (raw is null) return DefaultStationId;
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            _log.LogWarning(
                "selected_station_id value '{Raw}' is not a valid long - returning default Jita ({Default}).",
                raw, DefaultStationId);
            return DefaultStationId;
        }
        var exists = await _db.QueryScalarAsync<long>(
            "SELECT 1 FROM market_stations WHERE station_id = $sid LIMIT 1",
            new Dictionary<string, object?> { ["$sid"] = id }, ct);
        if (exists == 0)
        {
            _log.LogWarning(
                "selected_station_id={Id} not present in market_stations - returning default Jita ({Default}).",
                id, DefaultStationId);
            return DefaultStationId;
        }
        return id;
    }

    /// <inheritdoc />
    public async Task SetSelectedStationIdAsync(long stationId, CancellationToken ct = default)
    {
        await UpsertAsync(
            "selected_station_id",
            stationId.ToString(CultureInfo.InvariantCulture), ct);
        RaiseSettingChanged("selected_station_id");
    }

    /// <inheritdoc />
    public async Task<WindowGeometry?> GetWindowGeometryAsync(CancellationToken ct = default)
    {
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'window_geometry'", ct: ct);
        if (raw is null) return null;
        try
        {
            return JsonSerializer.Deserialize<WindowGeometry>(raw);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "window_geometry JSON malformed - returning null.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var json = JsonSerializer.Serialize(value);
        await UpsertAsync("window_geometry", json, ct);
        RaiseSettingChanged("window_geometry");
    }

    /// <inheritdoc />
    public async Task<WindowGeometry?> GetReportsWindowGeometryAsync(CancellationToken ct = default)
    {
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'reports_window_geometry'", ct: ct);
        if (raw is null) return null;
        try
        {
            return JsonSerializer.Deserialize<WindowGeometry>(raw);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "reports_window_geometry JSON malformed - returning null.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetReportsWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var json = JsonSerializer.Serialize(value);
        await UpsertAsync("reports_window_geometry", json, ct);
        RaiseSettingChanged("reports_window_geometry");
    }

    /// <inheritdoc />
    public async Task<WindowGeometry?> GetCrystalDiagramWindowGeometryAsync(CancellationToken ct = default)
    {
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'crystal_diagram_window_geometry'", ct: ct);
        if (raw is null) return null;
        try
        {
            return JsonSerializer.Deserialize<WindowGeometry>(raw);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "crystal_diagram_window_geometry JSON malformed - returning null.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetCrystalDiagramWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var json = JsonSerializer.Serialize(value);
        await UpsertAsync("crystal_diagram_window_geometry", json, ct);
        RaiseSettingChanged("crystal_diagram_window_geometry");
    }

    /// <inheritdoc />
    public Task<string?> GetSelectedCrystalAnomalyAsync(CancellationToken ct = default) =>
        _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'crystal_diagram_selected_anomaly'", ct: ct);

    /// <inheritdoc />
    public async Task SetSelectedCrystalAnomalyAsync(string value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        await UpsertAsync("crystal_diagram_selected_anomaly", value, ct);
        RaiseSettingChanged("crystal_diagram_selected_anomaly");
    }

    /// <inheritdoc />
    public Task<string?> GetDefaultFleetIdAsync(CancellationToken ct = default) =>
        _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'default_fleet_id'", ct: ct);

    /// <inheritdoc />
    public async Task SetDefaultFleetIdAsync(string? fleetId, CancellationToken ct = default)
    {
        if (fleetId is null)
        {
            // setting.value is NOT NULL, so "no default" is the row's absence:
            // clearing the default deletes the row.
            await _db.ExecuteNonQueryAsync(
                "DELETE FROM setting WHERE key = 'default_fleet_id'", null, ct);
        }
        else
        {
            await UpsertAsync("default_fleet_id", fleetId, ct);
        }
        RaiseSettingChanged("default_fleet_id");
    }

    /// <inheritdoc />
    public async Task<PilotSortOrder> GetPilotSortOrderAsync(CancellationToken ct = default)
    {
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'pilot_sort_order'", ct: ct);
        if (raw is null) return PilotSortOrder.LastActiveNewest;
        if (!PilotSortOrderExtensions.TryParse(raw, out var order))
        {
            _log.LogWarning(
                "pilot_sort_order value '{Raw}' is not recognised - returning default LastActiveNewest.", raw);
        }
        return order;
    }

    /// <inheritdoc />
    public async Task SetPilotSortOrderAsync(PilotSortOrder order, CancellationToken ct = default)
    {
        await UpsertAsync("pilot_sort_order", PilotSortOrderExtensions.ToCanonicalString(order), ct);
        RaiseSettingChanged("pilot_sort_order");
    }

    private static readonly IReadOnlyDictionary<long, bool> EmptyVisibility = new Dictionary<long, bool>();

    /// <summary>Compose the per-widget refined-materials visibility setting key (plan 43).</summary>
    internal static string RefinedMaterialsVisibilityKey(string scopeKey) =>
        scopeKey.Length == 0
            ? "refined_materials_visibility"
            : $"refined_materials_visibility.{scopeKey}";

    /// <inheritdoc />
    public async Task<bool> GetAutoShowRefinedMaterialsAsync(CancellationToken ct = default)
    {
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'auto_show_refined_materials'", ct: ct);
        if (raw is null) return true;
        return raw switch
        {
            "true" => true,
            "false" => false,
            _ => LogWarnAndReturn(
                $"Unknown auto_show_refined_materials value '{raw}' - returning default 'true'.",
                true),
        };
    }

    /// <inheritdoc />
    public async Task SetAutoShowRefinedMaterialsAsync(bool enabled, CancellationToken ct = default)
    {
        await UpsertAsync("auto_show_refined_materials", enabled ? "true" : "false", ct);
        RaiseSettingChanged("auto_show_refined_materials");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, bool>> GetRefinedMaterialsVisibilityAsync(string scopeKey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scopeKey);
        var key = RefinedMaterialsVisibilityKey(scopeKey);
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = $k LIMIT 1",
            new Dictionary<string, object?> { ["$k"] = key }, ct);
        if (raw is null) return EmptyVisibility;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<long, bool>>(raw) ?? EmptyVisibility;
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "{Key} JSON malformed - returning empty.", key);
            return EmptyVisibility;
        }
    }

    /// <inheritdoc />
    public async Task SetRefinedMaterialsVisibilityAsync(string scopeKey, IReadOnlyDictionary<long, bool> visibility, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scopeKey);
        ArgumentNullException.ThrowIfNull(visibility);
        var key = RefinedMaterialsVisibilityKey(scopeKey);
        var json = JsonSerializer.Serialize(visibility);
        await UpsertAsync(key, json, ct);
        RaiseSettingChanged(key);
    }

    /// <inheritdoc />
    public async Task<double?> GetReportsSidebarWidthAsync(CancellationToken ct = default)
    {
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'reports_sidebar_width'", ct: ct);
        if (raw is null) return null;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            _log.LogWarning(
                "reports_sidebar_width value '{Raw}' is not a valid number - returning null.", raw);
            return null;
        }
        return parsed;
    }

    /// <inheritdoc />
    public async Task SetReportsSidebarWidthAsync(double value, CancellationToken ct = default)
    {
        await UpsertAsync(
            "reports_sidebar_width",
            value.ToString("R", CultureInfo.InvariantCulture), ct);
        RaiseSettingChanged("reports_sidebar_width");
    }

    /// <inheritdoc />
    public async Task<WidgetLayout?> GetWidgetLayoutAsync(CancellationToken ct = default)
    {
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = 'widget_layout'", ct: ct);
        if (raw is null) return null;
        try
        {
            return JsonSerializer.Deserialize<WidgetLayout>(raw, WidgetLayoutJsonOptions);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "widget_layout JSON malformed - returning null.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetWidgetLayoutAsync(WidgetLayout value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var json = JsonSerializer.Serialize(value, WidgetLayoutJsonOptions);
        await UpsertAsync("widget_layout", json, ct);
        RaiseSettingChanged("widget_layout");
    }

    /// <inheritdoc />
    public Task<WidgetStatsConfig?> GetSessionStatsConfigAsync(CancellationToken ct = default) =>
        GetStatsConfigAsync(SessionStatsConfigKey, ct);

    /// <inheritdoc />
    public async Task SetSessionStatsConfigAsync(WidgetStatsConfig value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var json = JsonSerializer.Serialize(value);
        await UpsertAsync(SessionStatsConfigKey, json, ct);
        RaiseSettingChanged(SessionStatsConfigKey);
    }

    /// <inheritdoc />
    public Task<WidgetStatsConfig?> GetPerPilotStatsConfigAsync(long pilotId, CancellationToken ct = default) =>
        GetStatsConfigAsync(PerPilotStatsConfigKey(pilotId), ct);

    /// <inheritdoc />
    public async Task SetPerPilotStatsConfigAsync(long pilotId, WidgetStatsConfig value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var key = PerPilotStatsConfigKey(pilotId);
        var json = JsonSerializer.Serialize(value);
        await UpsertAsync(key, json, ct);
        RaiseSettingChanged(key);
    }

    /// <summary>Setting-table key for the singleton SessionStats stat config.</summary>
    internal const string SessionStatsConfigKey = "session_stats_config";

    /// <summary>Compose the per-pilot stat config setting-table key for a given pilot id.</summary>
    internal static string PerPilotStatsConfigKey(long pilotId) =>
        $"per_pilot_stats_config.{pilotId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Shared Get implementation for the two stats-config key shapes
    /// (singleton SessionStats key + per-pilot key family). Returns
    /// <c>null</c> on missing OR malformed JSON; logs Warning on the latter.
    /// Uses the converter registered via the <see cref="WidgetStatsConfig"/>
    /// type-level <c>JsonConverterAttribute</c> — no <see cref="JsonSerializerOptions"/>
    /// needed.
    /// </summary>
    private async Task<WidgetStatsConfig?> GetStatsConfigAsync(string key, CancellationToken ct)
    {
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = $k LIMIT 1",
            new Dictionary<string, object?> { ["$k"] = key }, ct);
        if (raw is null) return null;
        try
        {
            return JsonSerializer.Deserialize<WidgetStatsConfig>(raw);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "{Key} JSON malformed - returning null.", key);
            return null;
        }
    }

    private static readonly JsonSerializerOptions ReportsTextSizeJsonOptions = CreateReportsTextSizeJsonOptions();

    private static JsonSerializerOptions CreateReportsTextSizeJsonOptions()
    {
        // Persist StatEmphasis by NAME (not ordinal) so the stored JSON survives
        // enum-member reordering. Unknown names throw JsonException on read ->
        // null + Warning (the malformed-JSON policy), same as every other JSON setting.
        var opts = new JsonSerializerOptions();
        opts.Converters.Add(new JsonStringEnumConverter());
        return opts;
    }

    /// <summary>Compose the per-grid-type Reports column-widths setting key (plan 47).</summary>
    internal static string ReportsColumnWidthsKey(string gridKey) => $"reports_column_widths.{gridKey}";

    /// <summary>Compose the per-widget-type Reports text-size setting key (plan 47).</summary>
    internal static string ReportsTextSizeKey(string widgetTypeKey) => $"reports_text_size.{widgetTypeKey}";

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, double>?> GetReportsColumnWidthsAsync(string gridKey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gridKey);
        var key = ReportsColumnWidthsKey(gridKey);
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = $k LIMIT 1",
            new Dictionary<string, object?> { ["$k"] = key }, ct);
        if (raw is null) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, double>>(raw);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "{Key} JSON malformed - returning null.", key);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetReportsColumnWidthsAsync(string gridKey, IReadOnlyDictionary<int, double> widths, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gridKey);
        ArgumentNullException.ThrowIfNull(widths);
        var key = ReportsColumnWidthsKey(gridKey);
        var json = JsonSerializer.Serialize(widths);
        await UpsertAsync(key, json, ct);
        RaiseSettingChanged(key);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, StatEmphasis>?> GetReportsTextSizeAsync(string widgetTypeKey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(widgetTypeKey);
        var key = ReportsTextSizeKey(widgetTypeKey);
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT value FROM setting WHERE key = $k LIMIT 1",
            new Dictionary<string, object?> { ["$k"] = key }, ct);
        if (raw is null) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, StatEmphasis>>(raw, ReportsTextSizeJsonOptions);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "{Key} JSON malformed - returning null.", key);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetReportsTextSizeAsync(string widgetTypeKey, IReadOnlyDictionary<string, StatEmphasis> textSize, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(widgetTypeKey);
        ArgumentNullException.ThrowIfNull(textSize);
        var key = ReportsTextSizeKey(widgetTypeKey);
        var json = JsonSerializer.Serialize(textSize, ReportsTextSizeJsonOptions);
        await UpsertAsync(key, json, ct);
        RaiseSettingChanged(key);
    }

    /// <inheritdoc />
    public async Task EnsureSeedDefaultsAsync(CancellationToken ct = default)
    {
        await SeedAsync("theme", "dark", ct);
        await SeedAsync("price_mode", "buy", ct);
        await SeedAsync("refining_yield_percent",
            YieldDefault.ToString("R", CultureInfo.InvariantCulture), ct);
        await SeedAsync("selected_station_id",
            DefaultStationId.ToString(CultureInfo.InvariantCulture), ct);
        // Plan 37 — auto-pause defaults.
        await SeedAsync("auto_pause_idle_minutes",
            IdleMinutesDefault.ToString(CultureInfo.InvariantCulture), ct);
        await SeedAsync("auto_pause_sound_enabled", "true", ct);
        // Plan 43 — refined-output auto-show default on.
        await SeedAsync("auto_show_refined_materials", "true", ct);
        // refined_materials_visibility[.<pilot_id>]  intentionally NOT seeded (null sentinel = no explicit overrides)
        // window_geometry         intentionally NOT seeded (round-2 3.1 = B - null sentinel)
        // widget_layout           intentionally NOT seeded (plan 19 - null sentinel = use default layout)
        // session_stats_config    intentionally NOT seeded (plan 20 - null sentinel = use default order/emphasis)
        // per_pilot_stats_config.<pilot_id>  intentionally NOT seeded (plan 20 - null sentinel; rows added on first user customisation)
        // reports_window_geometry intentionally NOT seeded (plan 25 - null sentinel = use Reports default 1100x700 centered)
        // reports_sidebar_width   intentionally NOT seeded (plan 25 - null sentinel = use Reports default 280 px)
        // reports_column_widths.<gridKey> intentionally NOT seeded (plan 47 - null sentinel = use XAML default column widths)
        // reports_text_size.<widgetTypeKey> intentionally NOT seeded (plan 47 - null sentinel = all elements at StatEmphasis.None)
        // crystal_diagram_window_geometry intentionally NOT seeded (null sentinel = use Crystal default 900x650 centered)
        // crystal_diagram_selected_anomaly intentionally NOT seeded (plan 35 - null sentinel = default to 'all' entry)
        // schema_version          owned by plan 11
        // non_sticky_station_defaults_seeded owned by SqliteMarketStationCatalog
    }

    private Task UpsertAsync(string key, string value, CancellationToken ct) =>
        _db.ExecuteNonQueryAsync(
            "INSERT INTO setting (key, value) VALUES ($k, $v) " +
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            new Dictionary<string, object?> { ["$k"] = key, ["$v"] = value }, ct);

    private Task SeedAsync(string key, string value, CancellationToken ct) =>
        _db.ExecuteNonQueryAsync(
            "INSERT OR IGNORE INTO setting (key, value) VALUES ($k, $v)",
            new Dictionary<string, object?> { ["$k"] = key, ["$v"] = value }, ct);

    private T LogWarnAndReturn<T>(string message, T fallback)
    {
        _log.LogWarning("{Message}", message);
        return fallback;
    }

    private void RaiseSettingChanged(string key)
    {
        var handlers = SettingChanged;
        if (handlers is null) return;
        var args = new SettingChangedEventArgs(key);
        foreach (var h in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<SettingChangedEventArgs>)h).Invoke(this, args);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "SettingChanged handler threw for key={Key}.", key);
            }
        }
    }
}
