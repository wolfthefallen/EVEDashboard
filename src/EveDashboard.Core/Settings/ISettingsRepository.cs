namespace EveDashboard.Core.Settings;

using EveDashboard.Core.Layout;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings.WidgetStats;

/// <summary>
/// Typed read/write surface over the <c>setting</c> key-value table.
/// Each setting key has its own pair of <c>Get*Async</c> / <c>Set*Async</c>
/// methods; a <see cref="SettingChanged"/> event fires after each
/// <c>Set*Async</c> commits.
/// </summary>
/// <remarks>
/// <para>
/// Implementations hit SQLite on every call (no in-memory cache; settings
/// are touched at startup and on user-config changes only).
/// </para>
/// <para>
/// Assumed single-threaded at startup (<see cref="EnsureSeedDefaultsAsync"/>
/// runs before the UI opens) and UI-thread thereafter (<c>Set*Async</c> from
/// button clicks). Subscribers to <see cref="SettingChanged"/> are isolated
/// per-handler — one failing handler does not prevent others from observing.
/// If a caller awaits <c>Set*Async</c> from a non-UI context (e.g. a unit
/// test), the event fires on the post-await continuation thread; subscribers
/// that need UI thread affinity must marshal themselves.
/// </para>
/// </remarks>
public interface ISettingsRepository
{
    /// <summary>
    /// Read the current theme. Returns <see cref="ThemeMode.Dark"/> when missing
    /// or when the stored value is unrecognised (with Warning log on the latter).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="ThemeMode"/>, or the default.</returns>
    Task<ThemeMode> GetThemeAsync(CancellationToken ct = default);

    /// <summary>Persist a new theme. Raises <see cref="SettingChanged"/> with key <c>"theme"</c>.</summary>
    /// <param name="value">Theme to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetThemeAsync(ThemeMode value, CancellationToken ct = default);

    /// <summary>
    /// Read the current price mode. Returns <see cref="PriceMode.Buy"/> when
    /// missing or when the stored value is unrecognised (with Warning log on
    /// the latter).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="PriceMode"/>, or the default.</returns>
    Task<PriceMode> GetPriceModeAsync(CancellationToken ct = default);

    /// <summary>Persist a new price mode. Raises <see cref="SettingChanged"/> with key <c>"price_mode"</c>.</summary>
    /// <param name="value">Price mode to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetPriceModeAsync(PriceMode value, CancellationToken ct = default);

    /// <summary>Read the configured refining yield percent. Returns <c>90.6</c> when missing.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted refining yield percent, or <c>90.6</c>.</returns>
    Task<double> GetRefiningYieldPercentAsync(CancellationToken ct = default);

    /// <summary>
    /// Persist a new refining yield percent. Values outside <c>[50.0, 90.6]</c>
    /// are clamped (with Warning log). Raises <see cref="SettingChanged"/> with
    /// key <c>"refining_yield_percent"</c>.
    /// </summary>
    /// <param name="value">Refining yield percent. Clamped to <c>[50.0, 90.6]</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetRefiningYieldPercentAsync(double value, CancellationToken ct = default);

    /// <summary>
    /// Plan 37: read the auto-pause idle threshold in minutes. Returns <c>5</c>
    /// when missing. Stored values outside <c>[1, 60]</c> are clamped to that
    /// range (with Warning log). Malformed (non-integer) stored values return
    /// the default <c>5</c> (also with Warning log).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted threshold in minutes, or <c>5</c>.</returns>
    Task<int> GetAutoPauseIdleMinutesAsync(CancellationToken ct = default);

    /// <summary>
    /// Plan 37: persist a new auto-pause idle threshold in minutes. Values
    /// outside <c>[1, 60]</c> are clamped (with Warning log). Raises
    /// <see cref="SettingChanged"/> with key <c>"auto_pause_idle_minutes"</c>.
    /// </summary>
    /// <param name="minutes">Threshold in minutes. Clamped to <c>[1, 60]</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetAutoPauseIdleMinutesAsync(int minutes, CancellationToken ct = default);

    /// <summary>
    /// Plan 37: read whether the auto-pause / auto-resume notification sounds
    /// play. Returns <c>true</c> when missing or when the stored value is not
    /// recognised (with Warning log on unrecognised).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted sound-enabled flag, or <c>true</c>.</returns>
    Task<bool> GetAutoPauseSoundEnabledAsync(CancellationToken ct = default);

    /// <summary>
    /// Plan 37: persist the auto-pause sound on/off toggle. Raises
    /// <see cref="SettingChanged"/> with key <c>"auto_pause_sound_enabled"</c>.
    /// </summary>
    /// <param name="enabled">Whether the notification sounds should play.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetAutoPauseSoundEnabledAsync(bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Read the currently selected station id (drives ISK calc in plan 18).
    /// Returns <c>60003760</c> (Jita) when missing, when the stored value is
    /// not a valid <see cref="long"/>, or when the saved id has no row in
    /// <c>market_stations</c> (with Warning log on the malformed/orphan cases).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The selected station id, or <c>60003760</c> (Jita).</returns>
    Task<long> GetSelectedStationIdAsync(CancellationToken ct = default);

    /// <summary>Persist a new selected station id. Raises <see cref="SettingChanged"/> with key <c>"selected_station_id"</c>.</summary>
    /// <param name="stationId">Station id to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetSelectedStationIdAsync(long stationId, CancellationToken ct = default);

    /// <summary>
    /// Read the persisted main-window geometry. Returns <c>null</c> when missing
    /// or malformed (with Warning log on malformed). Plan 16 interprets <c>null</c>
    /// as "let WPF/OS pick placement."
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted geometry, or <c>null</c>.</returns>
    Task<WindowGeometry?> GetWindowGeometryAsync(CancellationToken ct = default);

    /// <summary>Persist new main-window geometry. Raises <see cref="SettingChanged"/> with key <c>"window_geometry"</c>.</summary>
    /// <param name="value">Geometry to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="value"/> is null.</exception>
    Task SetWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default);

    /// <summary>
    /// Read the persisted Reports child-window geometry (plan 25). Returns
    /// <c>null</c> when missing or malformed (with Warning log on malformed).
    /// Plan 25 interprets <c>null</c> as "use the Reports default size 1100x700
    /// centered, Normal state".
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted Reports window geometry, or <c>null</c>.</returns>
    Task<WindowGeometry?> GetReportsWindowGeometryAsync(CancellationToken ct = default);

    /// <summary>Persist new Reports child-window geometry. Raises <see cref="SettingChanged"/> with key <c>"reports_window_geometry"</c>.</summary>
    /// <param name="value">Geometry to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="value"/> is null.</exception>
    Task SetReportsWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default);

    /// <summary>
    /// Read the persisted Crystal Diagram child-window geometry. Returns
    /// <c>null</c> when missing or malformed (with Warning log on malformed).
    /// A <c>null</c> result is interpreted as "use the Crystal Diagram default
    /// size 900x650 centered, Normal state".
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted Crystal Diagram window geometry, or <c>null</c>.</returns>
    Task<WindowGeometry?> GetCrystalDiagramWindowGeometryAsync(CancellationToken ct = default);

    /// <summary>Persist new Crystal Diagram child-window geometry. Raises <see cref="SettingChanged"/> with key <c>"crystal_diagram_window_geometry"</c>.</summary>
    /// <param name="value">Geometry to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="value"/> is null.</exception>
    Task SetCrystalDiagramWindowGeometryAsync(WindowGeometry value, CancellationToken ct = default);

    /// <summary>
    /// Read the persisted Crystal Diagram selected-anomaly id (plan 35). Returns
    /// <c>null</c> when missing. A <c>null</c> result is interpreted as
    /// "no preference; default to the 'all' entry".
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted stable anomaly id (e.g. <c>"all"</c>, <c>"griemeer"</c>), or <c>null</c>.</returns>
    Task<string?> GetSelectedCrystalAnomalyAsync(CancellationToken ct = default);

    /// <summary>Persist the Crystal Diagram selected-anomaly id (plan 35). Raises <see cref="SettingChanged"/> with key <c>"crystal_diagram_selected_anomaly"</c>.</summary>
    /// <param name="value">Stable anomaly id to persist (e.g. <c>"all"</c>, <c>"griemeer"</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="value"/> is null.</exception>
    Task SetSelectedCrystalAnomalyAsync(string value, CancellationToken ct = default);

    /// <summary>
    /// Read the id of the default fleet (plan 42). Returns <c>null</c> when no
    /// default has been set (the <c>default_fleet_id</c> row is absent).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The default fleet's content id, or <c>null</c>.</returns>
    Task<string?> GetDefaultFleetIdAsync(CancellationToken ct = default);

    /// <summary>
    /// Set or clear the default fleet (plan 42). A non-null id is upserted; a
    /// <c>null</c> clears the default by deleting the <c>default_fleet_id</c> row
    /// (the setting value is NOT NULL, so absence is the "no default" sentinel).
    /// Raises <see cref="SettingChanged"/> with key <c>"default_fleet_id"</c>.
    /// </summary>
    /// <param name="fleetId">Fleet id to set as default, or <c>null</c> to clear.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetDefaultFleetIdAsync(string? fleetId, CancellationToken ct = default);

    /// <summary>
    /// Read the persisted pilot-picker sort order (plan 42). Returns
    /// <see cref="PilotSortOrder.LastActiveNewest"/> when missing or when the
    /// stored value is unrecognised (with Warning log on the latter).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted sort order, or the default.</returns>
    Task<PilotSortOrder> GetPilotSortOrderAsync(CancellationToken ct = default);

    /// <summary>Persist the pilot-picker sort order (plan 42). Raises <see cref="SettingChanged"/> with key <c>"pilot_sort_order"</c>.</summary>
    /// <param name="order">Sort order to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetPilotSortOrderAsync(PilotSortOrder order, CancellationToken ct = default);

    /// <summary>
    /// Read whether newly-produced refined materials auto-show in the live
    /// widgets' "Refined output" section (plan 43). Returns <c>true</c> when
    /// missing or when the stored value is unrecognised (Warning log on the latter).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted auto-show flag, or <c>true</c>.</returns>
    Task<bool> GetAutoShowRefinedMaterialsAsync(CancellationToken ct = default);

    /// <summary>Persist the refined-materials auto-show toggle (plan 43). Raises <see cref="SettingChanged"/> with key <c>"auto_show_refined_materials"</c>.</summary>
    /// <param name="enabled">Whether newly-produced materials auto-show.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetAutoShowRefinedMaterialsAsync(bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Plan 49 (issue #1): whether the live "Refined output" section renders on the
    /// per-pilot widgets. The Session widget is never gated by this. Returns
    /// <c>true</c> when the key is absent (default on).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted toggle, or <c>true</c>.</returns>
    Task<bool> GetShowPilotRefinedOutputAsync(CancellationToken ct = default);

    /// <summary>Persist the per-pilot refined-output toggle (plan 49). Raises <see cref="SettingChanged"/> with key <c>"show_pilot_refined_output"</c>.</summary>
    /// <param name="enabled">Whether the per-pilot widgets show the Refined output section.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetShowPilotRefinedOutputAsync(bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Read the per-widget explicit refined-material visibility overrides (plan 43,
    /// decision 2.2 = A — per-widget). Returns an empty map when none are set;
    /// a material absent from the map follows the auto-show default, a
    /// <c>false</c> entry is a sticky hide, a <c>true</c> entry an explicit show.
    /// </summary>
    /// <param name="scopeKey">Empty string for the Session widget, or the pilot id for a per-pilot widget.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Material type id -> explicit visibility; empty when unset or malformed.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="scopeKey"/> is null.</exception>
    Task<IReadOnlyDictionary<long, bool>> GetRefinedMaterialsVisibilityAsync(string scopeKey, CancellationToken ct = default);

    /// <summary>
    /// Persist the per-widget explicit refined-material visibility overrides
    /// (plan 43). Raises <see cref="SettingChanged"/> with key
    /// <c>"refined_materials_visibility"</c> (Session) or
    /// <c>"refined_materials_visibility.&lt;pilot_id&gt;"</c> (per pilot).
    /// </summary>
    /// <param name="scopeKey">Empty string for the Session widget, or the pilot id for a per-pilot widget.</param>
    /// <param name="visibility">Material type id -> explicit visibility map.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="scopeKey"/> or <paramref name="visibility"/> is null.</exception>
    Task SetRefinedMaterialsVisibilityAsync(string scopeKey, IReadOnlyDictionary<long, bool> visibility, CancellationToken ct = default);

    /// <summary>
    /// Read the persisted Reports-window sidebar expanded width in pixels
    /// (plan 25). Returns <c>null</c> when missing (interpret as default 280 px)
    /// or malformed (with Warning log on malformed).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted expanded width in pixels, or <c>null</c>.</returns>
    Task<double?> GetReportsSidebarWidthAsync(CancellationToken ct = default);

    /// <summary>Persist the Reports-window sidebar expanded width. Raises <see cref="SettingChanged"/> with key <c>"reports_sidebar_width"</c>.</summary>
    /// <param name="value">Expanded width in device-independent pixels.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetReportsSidebarWidthAsync(double value, CancellationToken ct = default);

    /// <summary>
    /// Read the persisted dashboard widget layout (plan 19). Returns
    /// <c>null</c> when missing, when the stored JSON is malformed, or when
    /// the stored JSON has an unknown <c>kind</c> discriminator (with Warning
    /// log on the malformed/unknown cases). Plan 19 interprets <c>null</c>
    /// as "use the default layout: Session at index 0, then per-pilots
    /// alphabetical, none hidden."
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted layout, or <c>null</c>.</returns>
    Task<WidgetLayout?> GetWidgetLayoutAsync(CancellationToken ct = default);

    /// <summary>Persist a new widget layout. Raises <see cref="SettingChanged"/> with key <c>"widget_layout"</c>.</summary>
    /// <param name="value">Layout to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="value"/> is null.</exception>
    Task SetWidgetLayoutAsync(WidgetLayout value, CancellationToken ct = default);

    /// <summary>
    /// Read the persisted SessionStats widget stat customisation (plan 20).
    /// Returns <c>null</c> when missing, when the stored JSON is malformed, or
    /// when the stored JSON is otherwise unreadable (with Warning log on the
    /// malformed case). The renderer interprets <c>null</c> as "use the default
    /// stat order with no rows hidden, default emphasis from <c>[StatRow]</c>."
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted stats config, or <c>null</c>.</returns>
    Task<WidgetStatsConfig?> GetSessionStatsConfigAsync(CancellationToken ct = default);

    /// <summary>Persist a new SessionStats widget stat customisation. Raises <see cref="SettingChanged"/> with key <c>"session_stats_config"</c>.</summary>
    /// <param name="value">Config to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="value"/> is null.</exception>
    Task SetSessionStatsConfigAsync(WidgetStatsConfig value, CancellationToken ct = default);

    /// <summary>
    /// Read the persisted per-pilot widget stat customisation for a single
    /// pilot (plan 20). Returns <c>null</c> when missing or malformed (with
    /// Warning log on malformed). Each pilot has an independent config keyed
    /// <c>per_pilot_stats_config.&lt;pilot_id&gt;</c> (decision 3.1 = B).
    /// </summary>
    /// <param name="pilotId">EVE character id of the pilot whose config to read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted stats config for the pilot, or <c>null</c>.</returns>
    Task<WidgetStatsConfig?> GetPerPilotStatsConfigAsync(long pilotId, CancellationToken ct = default);

    /// <summary>Persist a new per-pilot widget stat customisation. Raises <see cref="SettingChanged"/> with key <c>"per_pilot_stats_config.&lt;pilot_id&gt;"</c>.</summary>
    /// <param name="pilotId">EVE character id of the pilot whose config to write.</param>
    /// <param name="value">Config to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="value"/> is null.</exception>
    Task SetPerPilotStatsConfigAsync(long pilotId, WidgetStatsConfig value, CancellationToken ct = default);

    /// <summary>
    /// Plan 47: read the persisted Reports DataGrid column widths for one grid
    /// type. Returns <c>null</c> when missing or malformed (with Warning log on
    /// malformed). A <c>null</c> result means "no user-resized columns; keep the
    /// XAML default widths." The map is column-index -> pixel width, and contains
    /// only the columns the user actually resized.
    /// </summary>
    /// <param name="gridKey">Grid-type key (e.g. <c>"sessions"</c>, <c>"ore"</c>, <c>"compare.rates"</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Column-index -> pixel width, or <c>null</c> when unset/malformed.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="gridKey"/> is null.</exception>
    Task<IReadOnlyDictionary<int, double>?> GetReportsColumnWidthsAsync(string gridKey, CancellationToken ct = default);

    /// <summary>
    /// Plan 47: persist the Reports DataGrid column widths for one grid type.
    /// Raises <see cref="SettingChanged"/> with key
    /// <c>"reports_column_widths.&lt;gridKey&gt;"</c>.
    /// </summary>
    /// <param name="gridKey">Grid-type key (e.g. <c>"sessions"</c>, <c>"ore"</c>, <c>"compare.rates"</c>).</param>
    /// <param name="widths">Column-index -> pixel width map (only the resized columns).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="gridKey"/> or <paramref name="widths"/> is null.</exception>
    Task SetReportsColumnWidthsAsync(string gridKey, IReadOnlyDictionary<int, double> widths, CancellationToken ct = default);

    /// <summary>
    /// Plan 47: read the per-element text-size (emphasis) overrides for one
    /// Reports table widget type. Returns <c>null</c> when missing or malformed
    /// (with Warning log on malformed). A <c>null</c> result means "every element
    /// at the default <see cref="WidgetStats.StatEmphasis.None"/> look." The map
    /// is element-key -> emphasis, and contains only the elements the user raised
    /// above <see cref="WidgetStats.StatEmphasis.None"/>.
    /// </summary>
    /// <param name="widgetTypeKey">Widget-type key (one of <c>"ore"</c>, <c>"refined"</c>, <c>"compare"</c>, <c>"lifetime"</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Element-key -> emphasis, or <c>null</c> when unset/malformed.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="widgetTypeKey"/> is null.</exception>
    Task<IReadOnlyDictionary<string, WidgetStats.StatEmphasis>?> GetReportsTextSizeAsync(string widgetTypeKey, CancellationToken ct = default);

    /// <summary>
    /// Plan 47: persist the per-element text-size (emphasis) overrides for one
    /// Reports table widget type. Raises <see cref="SettingChanged"/> with key
    /// <c>"reports_text_size.&lt;widgetTypeKey&gt;"</c>.
    /// </summary>
    /// <param name="widgetTypeKey">Widget-type key (one of <c>"ore"</c>, <c>"refined"</c>, <c>"compare"</c>, <c>"lifetime"</c>).</param>
    /// <param name="textSize">Element-key -> emphasis map (only the raised elements).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="widgetTypeKey"/> or <paramref name="textSize"/> is null.</exception>
    Task SetReportsTextSizeAsync(string widgetTypeKey, IReadOnlyDictionary<string, WidgetStats.StatEmphasis> textSize, CancellationToken ct = default);

    /// <summary>
    /// Seed default values for any setting key not yet present in the
    /// <c>setting</c> table. Idempotent — re-running is a no-op once defaults
    /// are present. Does NOT seed <c>schema_version</c> (plan 11 owns that),
    /// <c>window_geometry</c> (intentionally null sentinel on first launch),
    /// <c>widget_layout</c> (intentionally null sentinel on first launch — plan 19),
    /// <c>session_stats_config</c> (intentionally null sentinel on first launch — plan 20),
    /// the family of <c>per_pilot_stats_config.&lt;pilot_id&gt;</c> keys (intentionally null sentinel — plan 20),
    /// or <c>non_sticky_station_defaults_seeded</c> (catalog's sentinel).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task EnsureSeedDefaultsAsync(CancellationToken ct = default);

    /// <summary>
    /// Raised after <c>Set*Async</c> commits. Args carry the setting key only;
    /// subscribers re-call the matching <c>Get*Async</c> for the new value.
    /// Subscribers are isolated per-handler — a failing handler does not
    /// prevent others from observing.
    /// </summary>
    event EventHandler<SettingChangedEventArgs>? SettingChanged;
}
