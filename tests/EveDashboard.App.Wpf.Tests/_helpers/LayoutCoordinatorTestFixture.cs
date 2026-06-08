namespace EveDashboard.App.Wpf.Tests.Helpers;

using EveDashboard.App.Wpf.Layout;
using EveDashboard.App.Wpf.Widgets.Pilot;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Stats.Settings;
using EveDashboard.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Real-but-isolated harness for <see cref="LayoutCoordinator"/> tests
/// (plan 19; plan 24 added the SessionStats VM; the post-plan-25 follow-up
/// removed the SessionConfig widget — it lives in a modal dialog now).
/// Composes:
/// <list type="bullet">
/// <item><see cref="PerPilotWidgetTestFixture"/> — owns the per-pilot DB,
///   <c>ActiveSessionContext</c>, <c>FakeSessionEventNotifier</c>, and
///   <see cref="PerPilotWidgetCoordinator"/>.</item>
/// <item><see cref="SessionControllerTestFixture"/> — owns its own in-memory
///   DB so it can construct a real <see cref="SessionController"/>; the
///   coordinator only holds a reference to that controller and never
///   queries its underlying DB, so the dual-DB split is harmless.</item>
/// <item>Real <see cref="SessionStatsViewModel"/> wrapping the controller.</item>
/// <item>A real <see cref="SqliteSettingsRepository"/> over the per-pilot
///   fixture's DB, so <c>widget_layout</c> reads/writes hit a real schema.</item>
/// </list>
/// xUnit has no dispatcher loop, so the coordinator's debounced save is
/// configured for a near-zero interval and tests call
/// <c>FlushPendingSaveAsync</c> to await it deterministically.
/// </summary>
internal sealed class LayoutCoordinatorTestFixture : IDisposable
{
    public PerPilotWidgetTestFixture PerPilotFixture { get; }
    public SessionControllerTestFixture SessionFixture { get; }
    public SqliteSettingsRepository SettingsRepo { get; }
    public PerPilotWidgetCoordinator PerPilotCoord { get; }
    public SessionController SessionVm { get; }
    public SessionStatsViewModel SessionStatsVm { get; }
    public CapturingLogger<LayoutCoordinator> CoordLog { get; }

    public LayoutCoordinatorTestFixture()
    {
        PerPilotFixture = new PerPilotWidgetTestFixture();
        SessionFixture = new SessionControllerTestFixture();
        SettingsRepo = new SqliteSettingsRepository(
            PerPilotFixture.Db, NullLogger<SqliteSettingsRepository>.Instance);
        PerPilotCoord = PerPilotFixture.CreateCoordinator();
        SessionVm = SessionFixture.CreateViewModel();
        SessionStatsVm = new SessionStatsViewModel(SessionVm, PerPilotFixture.Settings);
        CoordLog = new CapturingLogger<LayoutCoordinator>();
    }

    /// <summary>Construct a coordinator with a near-zero save debounce interval so tests can await saves deterministically.</summary>
    public LayoutCoordinator CreateCoordinator()
    {
        var coord = new LayoutCoordinator(
            PerPilotFixture.ActiveSession,
            PerPilotCoord,
            SessionVm,
            SessionStatsVm,
            SettingsRepo,
            CoordLog);
        coord.SetSaveDebounceIntervalForTests(TimeSpan.FromMilliseconds(1));
        return coord;
    }

    /// <summary>Insert a session row and link the given pilots; returns the session id.</summary>
    public async Task<string> InsertSessionWithPilotsAsync(params (long PilotId, string Name)[] pilots)
    {
        var sid = await PerPilotFixture.InsertSessionAsync();
        foreach (var (pid, name) in pilots)
        {
            await PerPilotFixture.InsertSessionPilotAsync(sid, pid, name);
        }
        return sid;
    }

    public void Dispose()
    {
        try { PerPilotCoord.Dispose(); } catch { /* test cleanup */ }
        try { SessionStatsVm.Dispose(); } catch { /* test cleanup */ }
        try { SessionVm.Dispose(); } catch { /* test cleanup */ }
        try { PerPilotFixture.Dispose(); } catch { /* test cleanup */ }
        try { SessionFixture.Dispose(); } catch { /* test cleanup */ }
    }
}
