namespace EveDashboard.App.Wpf.Widgets.Session;

using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.App.Wpf.Widgets.StatConfig;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using EveDashboard.Stats.Queries;
using Microsoft.Extensions.Logging;

/// <summary>
/// Saved-data sibling of <see cref="SessionStatsViewModel"/> for the Reports
/// window. Re-declares the same <c>[StatRow]</c>-decorated stat properties as
/// the live VM (irreducible per the renderer's runtime-type
/// <see cref="System.Reflection.PropertyInfo"/> reads) but populates them
/// directly from a <see cref="SessionStats"/> snapshot via
/// <see cref="ApplyStats"/>. No <see cref="SessionController"/> dependency.
/// </summary>
/// <remarks>
/// <para>
/// Setting key passed to the base is <c>session_stats_config</c> — same as the
/// live VM. Show/hide preferences are deliberately shared between live and
/// Reports so the user controls them in one place. The popup VM's
/// <c>vmType</c> is <c>typeof(SavedSessionStatsViewModel)</c> so the
/// <c>StatRowsBuilder.Resolve</c> reflection target matches the runtime
/// instance the renderer reads via PropertyInfo.
/// </para>
/// </remarks>
public sealed partial class SavedSessionStatsViewModel : StatConfigurableWidgetViewModel
{
    /// <summary>Saved session id (sha256 hex) this VM is bound to. Immutable for the VM's lifetime.</summary>
    public string SavedSessionId { get; }

    /// <summary>Display session name (the user-typed, template-expanded name from <c>session.name</c>). Set externally via <see cref="SetSessionName"/>.</summary>
    [ObservableProperty]
    [property: StatRow("Session", Reorderable = true, DefaultOrder = -2, DefaultEmphasis = StatEmphasis.Header1)]
    private string _displaySessionName = string.Empty;

    /// <summary>Active duration formatted as HH:mm:ss. Set by <see cref="ApplyStats"/>.</summary>
    [ObservableProperty]
    [property: StatRow("Total time", Reorderable = true, DefaultOrder = -1, DefaultEmphasis = StatEmphasis.Hero)]
    private string _totalTimeMined = "00:00:00";

    /// <summary>m^3 per second over active duration.</summary>
    [ObservableProperty]
    [property: StatRow("m³/sec", Format = "N2", Reorderable = true, DefaultOrder = 1)]
    private double _m3PerSecond;

    /// <summary>Estimated ISK per hour, sourced from the saved snapshot's <c>IskPerHour</c>.</summary>
    [ObservableProperty]
    [property: StatRow("ISK/h", Format = "N0", Reorderable = true, DefaultOrder = 2)]
    private double _iskPerHour;

    /// <summary>Estimated total ISK mined this session, sourced from the saved snapshot's <c>TotalIskMined</c>.</summary>
    [ObservableProperty]
    [property: StatRow("Total ISK", Format = "N0", Reorderable = true, DefaultOrder = 3)]
    private double _totalIskMined;

    /// <summary>Total m^3 mined this session.</summary>
    [ObservableProperty]
    [property: StatRow("Total m³", Format = "N1", Reorderable = true, DefaultOrder = 4)]
    private double _totalM3Mined;

    /// <summary>Total m^3 from crit events.</summary>
    [ObservableProperty]
    [property: StatRow("Crit m³", Format = "N1", Reorderable = true, DefaultOrder = 5)]
    private double _totalM3FromCrits;

    /// <summary>Number of crit events.</summary>
    [ObservableProperty]
    [property: StatRow("Crit count", Reorderable = true, DefaultOrder = 6)]
    private long _critCount;

    /// <summary>Crit chance fraction (crits / (crits + ticks)).</summary>
    [ObservableProperty]
    [property: StatRow("Crit chance", Format = "P1", Reorderable = true, DefaultOrder = 7)]
    private double _critChance;

    /// <summary>Percentage of <c>TotalIskMined</c> attributable to crit events; 0 when total ISK is 0.</summary>
    [ObservableProperty]
    [property: StatRow("Crit ISK %", Format = "P1", Reorderable = true, DefaultOrder = 8)]
    private double _critIskPercent;

    /// <summary>Construct the saved-data SessionStats VM.</summary>
    /// <param name="savedSessionId">Saved session id; identity only — data is supplied by <see cref="ApplyStats"/>.</param>
    /// <param name="settings">Settings repository for stat-config persistence + change events.</param>
    /// <param name="log">Logger for diagnostics on Apply-failure paths.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="settings"/> or <paramref name="log"/> is null.</exception>
    public SavedSessionStatsViewModel(
        string savedSessionId,
        ISettingsRepository settings,
        ILogger<SavedSessionStatsViewModel> log)
        : base(
            vmType:     typeof(SavedSessionStatsViewModel),
            settings:   settings,
            settingKey: "session_stats_config",
            load:       ct => settings.GetSessionStatsConfigAsync(ct),
            save:       (cfg, ct) => settings.SetSessionStatsConfigAsync(cfg, ct),
            log:        log)
    {
        ArgumentNullException.ThrowIfNull(log);
        SavedSessionId = savedSessionId;

        StartInitialRebuild();
    }

    /// <inheritdoc />
    public override Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Set the display session name (sourced from <c>session.name</c>).</summary>
    /// <param name="name">Session name string to display.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="name"/> is null.</exception>
    public void SetSessionName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        DisplaySessionName = name;
    }

    /// <summary>
    /// Copy a saved <see cref="SessionStats"/> snapshot into the bound stat
    /// properties. Stamps <see cref="WidgetBaseViewModel.LastUpdated"/> with
    /// the snapshot's <c>LastCalculated</c> instant.
    /// </summary>
    /// <param name="snapshot">Full session-stats snapshot for the saved session id.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="snapshot"/> is null.</exception>
    public void ApplyStats(SessionStats snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        TotalTimeMined   = FormatDuration(snapshot.TotalTimeMined);
        M3PerSecond      = snapshot.M3PerSecond;
        IskPerHour       = snapshot.IskPerHour;
        TotalIskMined    = snapshot.TotalIskMined;
        TotalM3Mined     = snapshot.TotalM3Mined;
        TotalM3FromCrits = snapshot.TotalM3FromCrits;
        CritCount        = snapshot.CritCount;
        CritChance       = snapshot.CritChance;
        CritIskPercent   = snapshot.CritIskPercent;
        LastUpdated      = snapshot.LastCalculated;
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.Days > 0
            ? $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
}
