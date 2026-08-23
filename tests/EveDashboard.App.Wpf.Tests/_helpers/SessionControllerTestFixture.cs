namespace EveDashboard.App.Wpf.Tests.Helpers;

using System.IO;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Sessions;
using EveDashboard.Mining.Gamelogs;
using EveDashboard.Mining.Realtime;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Queries;
using EveDashboard.Stats.Schema;
using EveDashboard.Stats.Writer;
using EveDashboard.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Builds a real-but-isolated harness for SessionController tests:
/// real in-memory <see cref="SqliteDatabase"/>, real <see cref="SchemaInitializer"/>
/// schema, real <see cref="SessionEventWriter"/> + <see cref="RealtimeLogWatcher"/>
/// (no events flow because no real log lines are produced), real
/// <see cref="ActiveSessionContext"/>, fake <see cref="EveDashboard.App.Wpf.Dialogs.IConfirmDialog"/>,
/// fake <see cref="EveDashboard.Mining.Parsing.IGamelogLineParser"/>, fake
/// <see cref="EveDashboard.Core.Lookup.ISdeLookup"/>.
///
/// Writer + watcher have a per-session lifecycle owned by the VM. Tests
/// exercise the real Start/Stop calls.
/// </summary>
internal sealed class SessionControllerTestFixture : IDisposable
{
    public SqliteDatabase Db { get; }
    public ActiveSessionContext ActiveSession { get; }
    public SessionEventWriter Writer { get; }
    public RealtimeLogWatcher Watcher { get; }
    public ISessionStatsRepository StatsRepo { get; }
    public FakeConfirmDialog ConfirmStop { get; }
    public GamelogDiscoveryService Discovery { get; }
    public IReadOnlyList<Character> Characters { get; }
    public FakeSettingsRepository Settings { get; }
    public CapturingLogger<SessionController> WidgetLog { get; }
    public string TempGamelogDir { get; }

    public SessionControllerTestFixture(bool dialogResult = true, IReadOnlyList<Character>? characters = null)
    {
        // Temp gamelog dir + non-existent files (RealtimeLogWatcher tolerates non-existent paths
        // but requires the parent directory to exist).
        TempGamelogDir = Path.Combine(Path.GetTempPath(), "evemining-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempGamelogDir);

        Characters = characters ?? new[]
        {
            new Character(2_000_000_001, "TestPilotOne", Path.Combine(TempGamelogDir, "20260101_120000_2000000001.txt")),
            new Character(2_000_000_002, "TestPilotTwo", Path.Combine(TempGamelogDir, "20260101_120000_2000000002.txt")),
        };

        // The New Session start path resolves the fleet roster against a
        // FRESH gamelog-discovery snapshot and skips pilots with no current
        // gamelog. So discovery must actually find these
        // characters — write a minimal gamelog file (with the Listener: header
        // the discovery service reads) for each, where the file lives in an
        // existing directory.
        foreach (var c in Characters)
        {
            var parent = Path.GetDirectoryName(c.LogFilePath);
            if (parent is not null && Directory.Exists(parent))
            {
                File.WriteAllText(
                    c.LogFilePath,
                    "------------------------------------------------------------\r\n" +
                    "  Gamelog\r\n" +
                    $"  Listener: {c.Name}\r\n" +
                    "  Session Started: 2026.01.01 12:00:00\r\n" +
                    "------------------------------------------------------------\r\n");
            }
        }

        // Real in-memory SQLite + schema.
        Db = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        SchemaInitializer.EnsureSchemaAsync(Db).GetAwaiter().GetResult();

        // Real ActiveSessionContext + writer + watcher.
        ActiveSession = new ActiveSessionContext();
        var sde = new FakeSdeLookup();
        Writer = new SessionEventWriter(Db, sde, ActiveSession, NullLogger<SessionEventWriter>.Instance);
        var parser = new FakeGamelogLineParser();
        Watcher = new RealtimeLogWatcher(parser, Writer, NullLogger<RealtimeLogWatcher>.Instance);

        // A single shared FakeSettingsRepository so SessionController
        // and SqliteSessionStatsRepository read the same auto-pause / yield /
        // station settings during a test.
        Settings = new FakeSettingsRepository();

        // Real stats repository.
        StatsRepo = new SqliteSessionStatsRepository(
            Db,
            new NoOpIskCalculator(),
            Settings,
            sde,
            NullLogger<SqliteSessionStatsRepository>.Instance);

        ConfirmStop = new FakeConfirmDialog(dialogResult);
        WidgetLog = new CapturingLogger<SessionController>();
        Discovery = new GamelogDiscoveryService(TempGamelogDir, NullLogger<GamelogDiscoveryService>.Instance);
    }

    /// <summary>
    /// Build a fleet roster (all <see cref="PilotRole.Miner"/>) from the first
    /// <paramref name="count"/> discovered characters — the input to
    /// <see cref="SessionController.StartAsync(IReadOnlyList{FleetMember}, CancellationToken)"/>.
    /// </summary>
    public FleetMember[] Roster(int count = 1) =>
        Characters.Take(count)
            .Select(c => new FleetMember(c.CharacterId, c.Name, PilotRole.Miner))
            .ToArray();

    public SessionController CreateViewModel() =>
        new(
            db:             Db,
            statsRepo:      StatsRepo,
            notifier:       Writer,
            writer:         Writer,
            watcher:        Watcher,
            activeSession:  ActiveSession,
            confirmStop:    ConfirmStop,
            discovery:      Discovery,
            allCharacters:  Characters,
            settings:       Settings,
            log:            WidgetLog);

    public void Dispose()
    {
        try { Watcher.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { /* test cleanup */ }
        try { Writer.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { /* test cleanup */ }
        try { Db.Dispose(); } catch { /* test cleanup */ }
        try { if (Directory.Exists(TempGamelogDir)) Directory.Delete(TempGamelogDir, recursive: true); }
        catch { /* test cleanup */ }
    }
}
