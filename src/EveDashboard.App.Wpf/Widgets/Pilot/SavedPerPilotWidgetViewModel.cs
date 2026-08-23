namespace EveDashboard.App.Wpf.Widgets.Pilot;

using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.App.Wpf.Widgets.StatConfig;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using EveDashboard.Stats.Queries;
using Microsoft.Extensions.Logging;

/// <summary>
/// Saved-data sibling of <see cref="PerPilotWidgetViewModel"/> for the Reports
/// window. Re-declares the same <c>[StatRow]</c>-decorated stat properties as
/// the live VM but populates them from a <see cref="CharacterStats"/> snapshot
/// via <see cref="ApplyStats"/>. No coordinator subscription.
/// </summary>
/// <remarks>
/// Setting key passed to the base is <c>per_pilot_stats_config.&lt;pilotId&gt;</c>
/// — same as the live VM for the same pilot. Show/hide preferences are
/// deliberately shared.
/// </remarks>
public sealed partial class SavedPerPilotWidgetViewModel : StatConfigurableWidgetViewModel
{
    /// <summary>Saved session id (sha256 hex) this VM is bound to. Immutable.</summary>
    public string SavedSessionId { get; }

    /// <summary>EVE character id for this widget. Immutable.</summary>
    public long PilotId { get; }

    /// <summary>EVE character name (without role tag). Immutable.</summary>
    public string PilotName { get; }

    /// <summary>
    /// Human-readable header text. <c>"PilotName"</c> for Miner;
    /// <c>"PilotName [Booster]"</c> etc for non-Miner. Recomputed when
    /// <see cref="Role"/> changes.
    /// </summary>
    [StatRow("Pilot", Reorderable = true, DefaultOrder = 0, DefaultEmphasis = StatEmphasis.Header0)]
    public string DisplayName =>
        Role == PilotRole.Miner ? PilotName : $"{PilotName} [{Role}]";

    /// <summary>Pilot's role in the saved session (flows in via <see cref="ApplyStats"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private PilotRole _role = PilotRole.Miner;

    /// <summary>Wall-clock instant of the most recent log row for this pilot, or null when none.</summary>
    [ObservableProperty]
    [property: StatRow("Last event", Reorderable = false, DefaultOrder = 9998)]
    private DateTimeOffset? _lastEventAt;

    /// <summary>m^3 mined per second over the saved session's active duration.</summary>
    [ObservableProperty]
    [property: StatRow("m³/sec", Format = "N2", Reorderable = true, DefaultOrder = 1)]
    private double _m3PerSecond;

    /// <summary>Estimated ISK earned per hour for this pilot.</summary>
    [ObservableProperty]
    [property: StatRow("ISK/h", Format = "N0", Reorderable = true, DefaultOrder = 2)]
    private double _iskPerHour;

    /// <summary>Estimated total ISK earned by this pilot.</summary>
    [ObservableProperty]
    [property: StatRow("Total ISK", Format = "N0", Reorderable = true, DefaultOrder = 3)]
    private double _totalIskMined;

    /// <summary>Total m^3 mined by this pilot.</summary>
    [ObservableProperty]
    [property: StatRow("Total m³", Format = "N1", Reorderable = true, DefaultOrder = 4)]
    private double _totalM3Mined;

    /// <summary>Total m^3 attributable to crit events for this pilot.</summary>
    [ObservableProperty]
    [property: StatRow("Crit m³", Format = "N1", Reorderable = true, DefaultOrder = 5)]
    private double _totalM3FromCrits;

    /// <summary>Number of crit events seen for this pilot.</summary>
    [ObservableProperty]
    [property: StatRow("Crit count", Reorderable = true, DefaultOrder = 6)]
    private long _critCount;

    /// <summary>Crit chance fraction (crits / (crits + ticks)) for this pilot.</summary>
    [ObservableProperty]
    [property: StatRow("Crit chance", Format = "P1", Reorderable = true, DefaultOrder = 7)]
    private double _critChance;

    /// <summary>Drives the footer <c>Last event</c> TextBlock visibility (mirrors the live widget).</summary>
    [ObservableProperty]
    private bool _isLastEventAtVisible = true;

    /// <summary>Construct the saved-data per-pilot VM.</summary>
    /// <param name="savedSessionId">Saved session id; identity only — data is supplied by <see cref="ApplyStats"/>.</param>
    /// <param name="pilotId">EVE character id for this widget.</param>
    /// <param name="pilotName">EVE character name; rendered as the widget header.</param>
    /// <param name="settings">Settings repository for stat-config persistence + change events (shared with the live per-pilot widget for the same pilot).</param>
    /// <param name="log">Logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="pilotName"/>, <paramref name="settings"/>, or <paramref name="log"/> is null.</exception>
    public SavedPerPilotWidgetViewModel(
        string savedSessionId,
        long pilotId,
        string pilotName,
        ISettingsRepository settings,
        ILogger<SavedPerPilotWidgetViewModel> log)
        : base(
            vmType:     typeof(SavedPerPilotWidgetViewModel),
            settings:   settings,
            settingKey: $"per_pilot_stats_config.{pilotId}",
            load:       ct => settings.GetPerPilotStatsConfigAsync(pilotId, ct),
            save:       (cfg, ct) => settings.SetPerPilotStatsConfigAsync(pilotId, cfg, ct),
            log:        log)
    {
        ArgumentNullException.ThrowIfNull(pilotName);
        ArgumentNullException.ThrowIfNull(log);
        SavedSessionId = savedSessionId;
        PilotId = pilotId;
        PilotName = pilotName;

        // Re-theme the role border when the theme changes (the same
        // pattern as the live PerPilotWidgetViewModel). Saved widgets are
        // read-only — no RolesChanged subscription, no SetRoleCommand.
        Settings.SettingChanged += OnSettingChanged;

        StartInitialRebuild();
    }

    /// <inheritdoc />
    public override Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Copy a saved <see cref="CharacterStats"/> snapshot into the bound
    /// observable properties.
    /// </summary>
    /// <param name="stats">Per-pilot snapshot.</param>
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
        Role             = stats.Role;
        LastUpdated      = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    protected override void OnAdditionalHideOnlyResolved(IReadOnlyList<StatRowDescriptor> hideOnly)
    {
        IsLastEventAtVisible = !FindHidden(hideOnly, nameof(LastEventAt));
    }

    /// <summary>
    /// Re-raise <see cref="Role"/> PropertyChanged on a theme swap so
    /// the role border brush re-resolves against the new theme dictionary.
    /// </summary>
    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (!string.Equals(e.Key, "theme", StringComparison.Ordinal)) return;
        OnPropertyChanged(nameof(Role));
    }

    /// <inheritdoc />
    protected override void DisposeCore()
    {
        try { Settings.SettingChanged -= OnSettingChanged; } catch { /* defensive */ }
    }
}
