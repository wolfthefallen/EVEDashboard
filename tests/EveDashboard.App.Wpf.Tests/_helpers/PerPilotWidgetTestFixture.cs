namespace EveDashboard.App.Wpf.Tests.Helpers;

using System.Globalization;
using EveDashboard.App.Wpf.Widgets.Pilot;
using EveDashboard.Core.Sessions;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Queries;
using EveDashboard.Stats.Schema;
using EveDashboard.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Real-but-isolated harness for <see cref="PerPilotWidgetCoordinator"/> tests.
/// Mirrors <see cref="SessionWidgetTestFixture"/>: real in-memory
/// <see cref="SqliteDatabase"/>, real <see cref="SchemaInitializer"/>, real
/// <see cref="ActiveSessionContext"/>, real <see cref="SqliteSessionStatsRepository"/>,
/// fake <see cref="FakeSessionEventNotifier"/> (because tests need to raise
/// <c>SessionEventAdded</c> on demand without doing a full INSERT through
/// <see cref="EveDashboard.Stats.Writer.SessionEventWriter"/>).
///
/// xUnit has no message loop, so the coordinator's
/// <see cref="System.Windows.Threading.DispatcherTimer"/> never auto-fires;
/// tests drive lifecycle via the internal <c>HandleActiveSessionChangedAsync</c>
/// and <c>OnTickAsync</c> methods (visible via <c>InternalsVisibleTo</c>).
/// </summary>
internal sealed class PerPilotWidgetTestFixture : IDisposable
{
    public SqliteDatabase Db { get; }
    public ActiveSessionContext ActiveSession { get; }
    public FakeSessionEventNotifier Notifier { get; }
    public CountingSessionStatsRepository StatsRepo { get; }
    public FakeSettingsRepository Settings { get; }
    public ILoggerFactory LoggerFactory { get; }
    public CapturingLogger<PerPilotWidgetCoordinator> CoordinatorLog { get; }

    public PerPilotWidgetTestFixture()
    {
        Db = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        SchemaInitializer.EnsureSchemaAsync(Db).GetAwaiter().GetResult();

        ActiveSession = new ActiveSessionContext();
        Notifier = new FakeSessionEventNotifier();
        Settings = new FakeSettingsRepository();

        var inner = new SqliteSessionStatsRepository(
            Db,
            new NoOpIskCalculator(),
            new FakeSettingsRepository(),
            NullLogger<SqliteSessionStatsRepository>.Instance);
        StatsRepo = new CountingSessionStatsRepository(inner);

        CoordinatorLog = new CapturingLogger<PerPilotWidgetCoordinator>();
        LoggerFactory = new TestLoggerFactory(CoordinatorLog);
    }

    /// <summary>
    /// Plan 35: tracks SetPilotRoleAsync calls forwarded by per-pilot widgets.
    /// Tests inspect this list to verify role-change commands fire correctly.
    /// Default behavior is no-op (does not update DB or context — tests do that
    /// directly when needed).
    /// </summary>
    public List<(long PilotId, PilotRole Role)> SetRoleCallbackInvocations { get; } = new();

    public PerPilotWidgetCoordinator CreateCoordinator() =>
        new(
            activeSession:        ActiveSession,
            db:                   Db,
            statsRepo:            StatsRepo,
            notifier:             Notifier,
            loggerFactory:        LoggerFactory,
            settings:             Settings,
            setPilotRoleCallback: (pilotId, role, ct) =>
            {
                SetRoleCallbackInvocations.Add((pilotId, role));
                return Task.CompletedTask;
            });

    /// <summary>Insert a session row and return its <c>session_id</c>.</summary>
    public async Task<string> InsertSessionAsync(string name = "Test session")
    {
        // Plan 38: session ids are sha256 hex strings. Mix in a per-call GUID so
        // repeated inserts (even with the same name) get distinct, well-formed ids
        // and never collide on the session_id PRIMARY KEY.
        var id = SessionId.Compute(name, Guid.NewGuid().ToString("N"));
        await Db.ExecuteNonQueryAsync(
            "INSERT INTO session (session_id, name, notes) VALUES ($sid, $name, NULL)",
            new Dictionary<string, object?> { ["$sid"] = id, ["$name"] = name });
        return id;
    }

    /// <summary>Insert a pilot and link it to the session via <c>session_pilot</c>. Defaults role to Miner (plan 35).</summary>
    public async Task InsertSessionPilotAsync(string sessionId, long pilotId, string pilotName, string role = "Miner")
    {
        await Db.ExecuteNonQueryAsync(
            "INSERT OR IGNORE INTO pilot (pilot_id, name) VALUES ($pid, $name)",
            new Dictionary<string, object?> { ["$pid"] = pilotId, ["$name"] = pilotName });
        await Db.ExecuteNonQueryAsync(
            "INSERT INTO session_pilot (session_id, pilot_id, role) VALUES ($sid, $pid, $role)",
            new Dictionary<string, object?>
            {
                ["$sid"]  = sessionId,
                ["$pid"]  = pilotId,
                ["$role"] = role,
            });
    }

    /// <summary>
    /// Insert an open <c>session_time</c> row so the stats repo's active-duration
    /// query has a non-zero denominator. Without this, M3PerSecond is always 0.
    /// </summary>
    public async Task OpenSessionTimeAsync(string sessionId, DateTimeOffset start)
    {
        await Db.ExecuteNonQueryAsync(
            "INSERT INTO session_time (session_id, start_time, stop_time, is_pause) " +
            "VALUES ($sid, $start, NULL, 0)",
            new Dictionary<string, object?>
            {
                ["$sid"] = sessionId,
                ["$start"] = start.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            });
    }

    /// <summary>Insert a synthetic Tick log row for a pilot. Used by stats-refresh tests.</summary>
    public async Task InsertTickAsync(
        string sessionId,
        long pilotId,
        DateTimeOffset eveTimestamp,
        long typeId,
        long units,
        double volumeM3)
    {
        await Db.ExecuteNonQueryAsync(
            "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3) " +
            "VALUES ($sid, $pid, $ts, 'Tick', $tid, $u, $v)",
            new Dictionary<string, object?>
            {
                ["$sid"] = sessionId,
                ["$pid"] = pilotId,
                ["$ts"] = eveTimestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ["$tid"] = typeId,
                ["$u"] = units,
                ["$v"] = volumeM3,
            });
    }

    public void Dispose()
    {
        try { Db.Dispose(); } catch { /* test cleanup */ }
    }
}

/// <summary>Manually-triggerable <see cref="EveDashboard.Stats.Events.ISessionEventNotifier"/>.</summary>
internal sealed class FakeSessionEventNotifier : EveDashboard.Stats.Events.ISessionEventNotifier
{
    public event EventHandler<EveDashboard.Stats.Events.SessionEventAddedEventArgs>? SessionEventAdded;

    public void Raise(string sessionId, long logId, EveDashboard.Core.Domain.MiningEventKind kind) =>
        SessionEventAdded?.Invoke(this,
            new EveDashboard.Stats.Events.SessionEventAddedEventArgs(sessionId, logId, kind));
}

/// <summary>Decorator that tracks <see cref="ISessionStatsRepository.GetSessionStatsAsync"/> call count.</summary>
internal sealed class CountingSessionStatsRepository : ISessionStatsRepository
{
    private readonly ISessionStatsRepository _inner;
    public int GetCallCount;
    public Func<string, DateTimeOffset, CancellationToken, Task<SessionStats>>? Override { get; set; }

    public CountingSessionStatsRepository(ISessionStatsRepository inner) { _inner = inner; }

    public Task<SessionStats> GetSessionStatsAsync(string sessionId, DateTimeOffset asOf, bool allowLazyFetch, CancellationToken ct = default)
    {
        Interlocked.Increment(ref GetCallCount);
        if (Override is not null) return Override(sessionId, asOf, ct);
        return _inner.GetSessionStatsAsync(sessionId, asOf, allowLazyFetch, ct);
    }
}

/// <summary>Minimal <see cref="ILoggerFactory"/> that returns a single capturing logger and NullLogger for everything else.</summary>
internal sealed class TestLoggerFactory : ILoggerFactory
{
    private readonly ILogger _coordinatorLogger;

    public TestLoggerFactory(ILogger coordinatorLogger) { _coordinatorLogger = coordinatorLogger; }

    public void AddProvider(ILoggerProvider provider) { /* no-op */ }
    public ILogger CreateLogger(string categoryName)
    {
        if (categoryName == typeof(PerPilotWidgetCoordinator).FullName)
            return _coordinatorLogger;
        return NullLogger.Instance;
    }
    public void Dispose() { /* no-op */ }
}
