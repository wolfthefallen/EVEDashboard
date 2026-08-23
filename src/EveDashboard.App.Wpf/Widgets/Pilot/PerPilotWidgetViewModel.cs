namespace EveDashboard.App.Wpf.Widgets.Pilot;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EveDashboard.App.Wpf.Widgets.StatConfig;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using EveDashboard.Stats.Queries;
using Microsoft.Extensions.Logging;

/// <summary>
/// ViewModel for one per-pilot widget. One instance per pilot in
/// the active session; the bound stats are repopulated by
/// <c>PerPilotWidgetCoordinator</c> on every dirty tick via
/// <see cref="ApplyStats"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PilotId"/> and <see cref="PilotName"/> are immutable for the
/// widget's lifetime - the coordinator drops and recreates widgets when the
/// active session changes.
/// </para>
/// <para>
/// The inherited abstract <see cref="WidgetBaseViewModel.RefreshAsync"/> is a
/// no-op here; the coordinator drives refresh by invoking
/// <see cref="ApplyStats"/> with the latest <see cref="CharacterStats"/>
/// snapshot. ISK fields are populated by the ISK pricing pipeline that runs
/// inside <c>SqliteSessionStatsRepository.GetSessionStatsAsync</c>.
/// </para>
/// <para>
/// Extends <see cref="StatConfigurableWidgetViewModel"/>,
/// which owns the stat-config plumbing shared by every stat-list widget.
/// The per-pilot-only <see cref="IsLastEventAtVisible"/>
/// flag is wired via the <see cref="OnAdditionalHideOnlyResolved"/> hook.
/// </para>
/// <para>
/// Per-pilot fleet role (Miner / Booster / Cyno / Combat): the VM
/// subscribes to <see cref="IActiveSessionContext.RolesChanged"/> for live
/// updates from <c>SessionController.SetPilotRoleAsync</c>, and to
/// <see cref="ISettingsRepository.SettingChanged"/> filtered by <c>"theme"</c>
/// so the role-driven border brush re-resolves on a theme swap. Both
/// subscriptions are released in <see cref="DisposeCore"/>.
/// </para>
/// </remarks>
public sealed partial class PerPilotWidgetViewModel : StatConfigurableWidgetViewModel
{
    private readonly IActiveSessionContext _activeSession;
    private readonly Func<long, PilotRole, CancellationToken, Task> _setRoleCallback;

    /// <summary>EVE character id for this widget. Matches <c>session_pilot.pilot_id</c>. NOT rendered to the user.</summary>
    public long PilotId { get; }

    /// <summary>EVE character name (without role tag). Immutable for the widget's lifetime.</summary>
    public string PilotName { get; }

    /// <summary>
    /// Human-readable header text. <c>"PilotName"</c> for Miner;
    /// <c>"PilotName [Booster]"</c> etc for non-Miner. Recomputed when
    /// <see cref="Role"/> changes.
    /// </summary>
    [StatRow("Pilot", Reorderable = true, DefaultOrder = 0, DefaultEmphasis = StatEmphasis.Header0)]
    public string DisplayName =>
        Role == PilotRole.Miner ? PilotName : $"{PilotName} [{Role}]";

    /// <summary>This pilot's current fleet role. Drives the role border, the DisplayName tag, and the context-menu checkmark.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private PilotRole _role;

    /// <summary>Wall-clock instant of the most recent log row for this pilot, or null when none yet.</summary>
    [ObservableProperty]
    [property: StatRow("Last event", Reorderable = false, DefaultOrder = 9998)]
    private DateTimeOffset? _lastEventAt;

    /// <summary>m^3 mined per second over the session's active duration. <c>0</c> when no active duration yet.</summary>
    [ObservableProperty]
    [property: StatRow("m³/sec", Format = "N2", Reorderable = true, DefaultOrder = 1)]
    private double _m3PerSecond;

    /// <summary>Estimated ISK earned per hour, sourced from the ISK pricing pipeline via the per-pilot stats snapshot.</summary>
    [ObservableProperty]
    [property: StatRow("ISK/h", Format = "N0", Reorderable = true, DefaultOrder = 2)]
    private double _iskPerHour;

    /// <summary>Estimated total ISK earned by this pilot, sourced from the ISK pricing pipeline via the per-pilot stats snapshot.</summary>
    [ObservableProperty]
    [property: StatRow("Total ISK", Format = "N0", Reorderable = true, DefaultOrder = 3)]
    private double _totalIskMined;

    /// <summary>Total m^3 mined by this pilot in the session.</summary>
    [ObservableProperty]
    [property: StatRow("Total m³", Format = "N1", Reorderable = true, DefaultOrder = 4)]
    private double _totalM3Mined;

    /// <summary>Total m^3 attributable to crit (mining-laser bonus) events.</summary>
    [ObservableProperty]
    [property: StatRow("Crit m³", Format = "N1", Reorderable = true, DefaultOrder = 5)]
    private double _totalM3FromCrits;

    /// <summary>Number of crit events seen for this pilot.</summary>
    [ObservableProperty]
    [property: StatRow("Crit count", Reorderable = true, DefaultOrder = 6)]
    private long _critCount;

    /// <summary>Crit-events / (crit-events + tick-events). <c>0</c> when no crit-eligible events yet.</summary>
    [ObservableProperty]
    [property: StatRow("Crit chance", Format = "P1", Reorderable = true, DefaultOrder = 7)]
    private double _critChance;

    /// <summary>Drives the footer <c>Last event</c> TextBlock visibility. True (default) = visible.</summary>
    [ObservableProperty]
    private bool _isLastEventAtVisible = true;

    /// <summary>
    /// Invokes the constructor-injected role-change callback. Bound
    /// to each MenuItem in the right-click context menu via
    /// <c>CommandParameter="{x:Static sessions:PilotRole.X}"</c>.
    /// </summary>
    public IAsyncRelayCommand<PilotRole> SetRoleCommand { get; }

    /// <summary>Construct a per-pilot widget VM.</summary>
    /// <param name="pilotId">EVE character id (from <c>session_pilot.pilot_id</c>).</param>
    /// <param name="pilotName">EVE character name; rendered as the widget header.</param>
    /// <param name="initialRole">Pilot's role at widget-create time (read from <c>session_pilot.role</c> by the coordinator).</param>
    /// <param name="settings">Settings repository for stat-config persistence + change events.</param>
    /// <param name="activeSession">Read view of the active session; subscribed to for <see cref="IActiveSessionContext.RolesChanged"/>.</param>
    /// <param name="setRoleCallback">Forwarded to <c>SessionController.SetPilotRoleAsync</c> by DI factory.</param>
    /// <param name="log">Logger; reserved for future per-widget diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if any required argument is null.</exception>
    public PerPilotWidgetViewModel(
        long pilotId,
        string pilotName,
        PilotRole initialRole,
        ISettingsRepository settings,
        IActiveSessionContext activeSession,
        Func<long, PilotRole, CancellationToken, Task> setRoleCallback,
        ILogger<PerPilotWidgetViewModel> log)
        : base(
            vmType:     typeof(PerPilotWidgetViewModel),
            settings:   settings,
            settingKey: $"per_pilot_stats_config.{pilotId}",
            load:       ct => settings.GetPerPilotStatsConfigAsync(pilotId, ct),
            save:       (cfg, ct) => settings.SetPerPilotStatsConfigAsync(pilotId, cfg, ct),
            log:        log)
    {
        ArgumentNullException.ThrowIfNull(pilotName);
        ArgumentNullException.ThrowIfNull(activeSession);
        ArgumentNullException.ThrowIfNull(setRoleCallback);
        ArgumentNullException.ThrowIfNull(log);

        PilotId          = pilotId;
        PilotName        = pilotName;
        _role            = initialRole;
        _activeSession   = activeSession;
        _setRoleCallback = setRoleCallback;

        SetRoleCommand = new AsyncRelayCommand<PilotRole>(SetRoleAsync);

        _activeSession.RolesChanged += OnRolesChanged;
        Settings.SettingChanged += OnSettingChanged;

        // This pilot's per-widget "Refined output" section (scope key =
        // the pilot id, matching the per_pilot_stats_config keying).
        EnableRefinedOutput(pilotId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Initial render with default config (pre-load); will rebuild on first SettingChanged
        // OR on the first tick — whichever fires first. Calling here is optional safety:
        // gives the widget non-empty Stats immediately on construction.
        StartInitialRebuild();
    }

    /// <summary>
    /// No-op refresh hook - the coordinator drives refresh via
    /// <see cref="ApplyStats"/>. Returns <see cref="Task.CompletedTask"/>
    /// to satisfy the abstract base.
    /// </summary>
    /// <param name="ct">Cancellation token (unused).</param>
    public override Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Copy a <see cref="CharacterStats"/> snapshot into the bound
    /// <c>[ObservableProperty]</c> fields and stamp <see cref="WidgetBaseViewModel.LastUpdated"/>.
    /// Called by <c>PerPilotWidgetCoordinator</c> on every dirty tick.
    /// </summary>
    /// <param name="stats">Latest stats snapshot for this pilot.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="stats"/> is null.</exception>
    public void ApplyStats(CharacterStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        LastEventAt      = stats.LastEventAt;
        M3PerSecond      = stats.M3PerSecond;
        IskPerHour       = stats.IskPerHour;
        TotalIskMined    = stats.TotalIskMined;
        TotalM3Mined     = stats.TotalM3Mined;
        TotalM3FromCrits = stats.TotalM3FromCrits;
        CritCount        = stats.CritCount;
        CritChance       = stats.CritChance;
        // Also propagate role from the snapshot (RolesChanged is the
        // immediate signal; this is the redundant catch-all).
        Role             = stats.Role;
        // Merge this pilot's refined output into the section.
        MergeRefinedOutput(stats.RefinedOutput);
        LastUpdated      = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    protected override void OnAdditionalHideOnlyResolved(IReadOnlyList<StatRowDescriptor> hideOnly)
    {
        IsLastEventAtVisible = !FindHidden(hideOnly, nameof(LastEventAt));
    }

    /// <summary>
    /// Forward the chosen role to the controller via the injected
    /// callback. The controller does the SQL UPDATE + in-memory map update +
    /// <c>RolesChanged</c> raise; <see cref="OnRolesChanged"/> then sees the
    /// event and updates this VM's <see cref="Role"/> property.
    /// </summary>
    private Task SetRoleAsync(PilotRole role) =>
        _setRoleCallback(PilotId, role, CancellationToken.None);

    /// <summary>
    /// Handle a mid-session role change from
    /// <c>ActiveSessionContext.SetRole</c>. Only acts when the event pertains
    /// to this widget's pilot.
    /// </summary>
    private void OnRolesChanged(object? sender, PilotRoleChangedEventArgs e)
    {
        if (e.PilotId != PilotId) return;
        Role = e.NewRole;
    }

    /// <summary>
    /// Handle a theme swap. The role border brush is resolved via
    /// <c>RoleToBorderBrushConverter</c> against
    /// <c>Application.Current.Resources</c>; when the merged theme dictionary
    /// changes, the binding does NOT re-run automatically. Re-raise
    /// <see cref="Role"/> PropertyChanged so the converter re-resolves.
    /// </summary>
    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (!string.Equals(e.Key, "theme", StringComparison.Ordinal)) return;
        OnPropertyChanged(nameof(Role));
    }

    /// <inheritdoc />
    protected override void DisposeCore()
    {
        try { _activeSession.RolesChanged -= OnRolesChanged; } catch { /* defensive */ }
        try { Settings.SettingChanged    -= OnSettingChanged; } catch { /* defensive */ }
    }
}
