namespace EveDashboard.App.Wpf.Widgets.Session;

using System.ComponentModel;
using EveDashboard.App.Wpf.Widgets.StatConfig;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Settings.WidgetStats;
using Microsoft.Extensions.Logging;

/// <summary>
/// Thin ViewModel behind the SessionStats widget (plan 24 split). Holds a
/// reference to the singleton <see cref="SessionController"/> and forwards
/// every bound stat field the view renders:
/// <see cref="DisplaySessionName"/>, <see cref="TotalTimeMined"/>,
/// <see cref="M3PerSecond"/>, <see cref="IskPerHour"/>,
/// <see cref="TotalIskMined"/>, <see cref="TotalM3Mined"/>,
/// <see cref="TotalM3FromCrits"/>, <see cref="CritCount"/>, and
/// <see cref="CritChance"/>. The inherited
/// <see cref="WidgetBaseViewModel.LastUpdated"/> is forwarded by listening
/// to the controller's PropertyChanged for <c>LastUpdated</c>.
/// </summary>
/// <remarks>
/// Plan 20: <see cref="StatRowAttribute"/> attributes attach to THIS VM's
/// forwarding properties (NOT to <see cref="SessionController"/>'s backing
/// fields) so reflection over <c>typeof(SessionStatsViewModel)</c> finds
/// them uniformly with the per-pilot widget. Plan 25 retrofit: extends
/// <see cref="StatConfigurableWidgetViewModel"/> which owns the duplicated
/// stat-config plumbing flagged in <c>CLEANUP.md</c> 2.2.
/// </remarks>
public sealed class SessionStatsViewModel : StatConfigurableWidgetViewModel
{
    private readonly SessionController _controller;

    /// <summary>Resolved (template-expanded) session name shown as the widget header.</summary>
    [StatRow("Session", Reorderable = true, DefaultOrder = -2, DefaultEmphasis = StatEmphasis.Header1)]
    public string DisplaySessionName => _controller.DisplaySessionName;

    /// <summary>Live elapsed-time string (HH:mm:ss). Updated each tick during Running mode.</summary>
    [StatRow("Total time", Reorderable = true, DefaultOrder = -1, DefaultEmphasis = StatEmphasis.Hero)]
    public string TotalTimeMined => _controller.TotalTimeMined;

    /// <summary>m^3 per second over active duration.</summary>
    [StatRow("m³/sec", Format = "N2", Reorderable = true, DefaultOrder = 1)]
    public double M3PerSecond => _controller.M3PerSecond;

    /// <summary>Estimated ISK per hour, sourced from the plan-18 ISK pipeline via the session stats snapshot.</summary>
    [StatRow("ISK/h", Format = "N0", Reorderable = true, DefaultOrder = 2)]
    public double IskPerHour => _controller.IskPerHour;

    /// <summary>Estimated ISK mined this session, sourced from the plan-18 ISK pipeline via the session stats snapshot.</summary>
    [StatRow("Total ISK", Format = "N0", Reorderable = true, DefaultOrder = 3)]
    public double TotalIskMined => _controller.TotalIskMined;

    /// <summary>Total m^3 mined this session.</summary>
    [StatRow("Total m³", Format = "N1", Reorderable = true, DefaultOrder = 4)]
    public double TotalM3Mined => _controller.TotalM3Mined;

    /// <summary>Total m^3 from crit events.</summary>
    [StatRow("Crit m³", Format = "N1", Reorderable = true, DefaultOrder = 5)]
    public double TotalM3FromCrits => _controller.TotalM3FromCrits;

    /// <summary>Number of crit events.</summary>
    [StatRow("Crit count", Reorderable = true, DefaultOrder = 6)]
    public long CritCount => _controller.CritCount;

    /// <summary>Crit chance fraction (crits / (crits + ticks)).</summary>
    [StatRow("Crit chance", Format = "P1", Reorderable = true, DefaultOrder = 7)]
    public double CritChance => _controller.CritChance;

    /// <summary>Plan 36: percentage of <c>TotalIskMined</c> attributable to crit events; 0 when total ISK is 0.</summary>
    [StatRow("Crit ISK %", Format = "P1", Reorderable = true, DefaultOrder = 8)]
    public double CritIskPercent => _controller.CritIskPercent;

    /// <summary>Construct the SessionStats VM around the singleton session controller.</summary>
    /// <param name="controller">The session controller — must be non-null.</param>
    /// <param name="settings">Settings repository for stat-config persistence + change events.</param>
    /// <param name="log">Logger; reserved for diagnostics on Apply failure paths.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="controller"/> or <paramref name="settings"/> is null.</exception>
    public SessionStatsViewModel(
        SessionController controller,
        ISettingsRepository settings,
        ILogger<SessionStatsViewModel>? log = null)
        : base(
            vmType:     typeof(SessionStatsViewModel),
            settings:   settings,
            settingKey: "session_stats_config",
            load:       ct => settings.GetSessionStatsConfigAsync(ct),
            save:       (cfg, ct) => settings.SetSessionStatsConfigAsync(cfg, ct),
            log:        log)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        _controller.PropertyChanged += OnControllerPropertyChanged;

        // Subclass fields are now set; trigger the initial render.
        StartInitialRebuild();
    }

    /// <inheritdoc />
    public override Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SessionController.DisplaySessionName):
                OnPropertyChanged(nameof(DisplaySessionName));
                break;
            case nameof(SessionController.TotalTimeMined):
                OnPropertyChanged(nameof(TotalTimeMined));
                break;
            case nameof(SessionController.TotalM3Mined):
                OnPropertyChanged(nameof(TotalM3Mined));
                break;
            case nameof(SessionController.M3PerSecond):
                OnPropertyChanged(nameof(M3PerSecond));
                break;
            case nameof(SessionController.TotalIskMined):
                OnPropertyChanged(nameof(TotalIskMined));
                break;
            case nameof(SessionController.IskPerHour):
                OnPropertyChanged(nameof(IskPerHour));
                break;
            case nameof(SessionController.TotalM3FromCrits):
                OnPropertyChanged(nameof(TotalM3FromCrits));
                break;
            case nameof(SessionController.CritCount):
                OnPropertyChanged(nameof(CritCount));
                break;
            case nameof(SessionController.CritChance):
                OnPropertyChanged(nameof(CritChance));
                break;
            case nameof(SessionController.CritIskPercent):
                OnPropertyChanged(nameof(CritIskPercent));
                break;
            case nameof(SessionController.LastUpdated):
                LastUpdated = _controller.LastUpdated;
                break;
        }
    }

    /// <inheritdoc />
    protected override void DisposeCore()
    {
        try { _controller.PropertyChanged -= OnControllerPropertyChanged; } catch { /* defensive */ }
    }
}
