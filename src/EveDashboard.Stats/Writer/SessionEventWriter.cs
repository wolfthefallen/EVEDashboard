namespace EveDashboard.Stats.Writer;

using System.Data.Common;
using System.Globalization;
using System.Threading.Channels;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Realtime;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Storage;
using EveDashboard.Stats.Events;
using Microsoft.Extensions.Logging;

/// <summary>
/// Writes parsed <see cref="MiningEvent"/> records to the session
/// <c>log</c> table and raises <see cref="ISessionEventNotifier.SessionEventAdded"/>
/// once each row is INSERTed.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle: <see cref="StartAsync"/> creates a bounded channel and a
/// background consumer task that drains it; <see cref="StopAsync"/> completes
/// the channel writer, awaits the consumer, and unsubscribes from
/// <see cref="ISqliteDatabase.RowChanged"/>. <see cref="WriteAsync"/> is
/// the <see cref="IMiningEventSink"/> entry point used by the watcher.
/// </para>
/// <para>
/// Drop rules:
/// </para>
/// <list type="bullet">
///   <item><description>No active session (<see cref="IActiveSessionContext.ActiveSessionId"/> is null) -&gt; drop.</description></item>
///   <item><description>State == <see cref="SessionRunState.Paused"/> -&gt; drop.</description></item>
///   <item><description><see cref="MiningEventKind.Tick"/> / <see cref="MiningEventKind.Crit"/> with <see cref="MiningEvent.TypeId"/> == null -&gt; drop (parser already logged Warning).</description></item>
///   <item><description><see cref="MiningEventKind.Residue"/> with TypeId == null -&gt; INSERT row with <c>type_id</c>=NULL, <c>volume_m3</c>=NULL.</description></item>
/// </list>
/// </remarks>
public sealed class SessionEventWriter : IMiningEventSink, ISessionEventNotifier, IAsyncDisposable
{
    private readonly ISqliteDatabase _db;
    private readonly ISdeLookup _sde;
    private readonly IActiveSessionContext _activeSession;
    private readonly ILogger<SessionEventWriter> _log;

    /// <summary>Bounded queue between <see cref="WriteAsync"/> and the consumer task. Created by <see cref="StartAsync"/>.</summary>
    private Channel<MiningEvent>? _channel;

    /// <summary>Background consumer task. Created by <see cref="StartAsync"/>.</summary>
    private Task? _consumer;

    /// <summary>Active session id (sha256 hex) captured at <see cref="StartAsync"/>; null when not started.</summary>
    private string? _startedSessionId;

    /// <summary>
    /// Kind of the event currently being INSERTed. Set on the consumer thread
    /// immediately before <see cref="ISqliteDatabase.ExecuteNonQueryAsync"/>
    /// runs the INSERT; read synchronously by <see cref="OnRowChanged"/>
    /// (which fires on the same thread inside the SQLite hook); cleared in
    /// the finally block. The consumer loop is single-reader so concurrent
    /// in-flight INSERTs do not happen.
    /// </summary>
    private MiningEventKind? _currentInsertKind;

    /// <summary>
    /// Auto-resume callback registered by the session controller (UI
    /// layer) at session start. When a <see cref="MiningEventKind.Tick"/> or
    /// <see cref="MiningEventKind.Crit"/> arrives while the session is
    /// <see cref="SessionRunState.Paused"/>, <see cref="WriteAsync"/> awaits
    /// this callback (single-flight via <see cref="_inFlightResume"/>) before
    /// re-checking state. The callback is expected to flip
    /// <see cref="IActiveSessionContext.State"/> to
    /// <see cref="SessionRunState.Running"/> by the time it returns; the
    /// writer then falls through to the normal enqueue path so the triggering
    /// event is preserved (counted in totals).
    /// </summary>
    private Func<CancellationToken, Task>? _onAutoResumeRequested;

    /// <summary>
    /// Single-flight cache for an in-flight auto-resume invocation.
    /// Concurrent paused Tick/Crit callers await the same Task so the resume
    /// SQL runs exactly once per burst. Cleared (allowed to be re-invoked) once
    /// the Task completes; a faulted Task re-invokes on the next paused event.
    /// </summary>
    private Task? _inFlightResume;

    /// <summary>Lock guarding <see cref="_inFlightResume"/> read-modify-write.</summary>
    private readonly object _resumeLock = new();

    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<SessionEventAddedEventArgs>? SessionEventAdded;

    /// <summary>Construct a writer.</summary>
    /// <param name="db">The singleton session SQLite wrapper.</param>
    /// <param name="sde">SDE lookup used to denormalise <c>volume_m3</c> at INSERT time.</param>
    /// <param name="activeSession">Read-only view of the active session id and run state.</param>
    /// <param name="log">Logger for SQL-error and subscriber-isolation diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public SessionEventWriter(
        ISqliteDatabase db,
        ISdeLookup sde,
        IActiveSessionContext activeSession,
        ILogger<SessionEventWriter> log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(sde);
        ArgumentNullException.ThrowIfNull(activeSession);
        ArgumentNullException.ThrowIfNull(log);
        _db = db;
        _sde = sde;
        _activeSession = activeSession;
        _log = log;
    }

    /// <summary>
    /// Set up the bounded channel, subscribe to the SQL update hook, and spawn
    /// the background consumer task.
    /// </summary>
    /// <param name="sessionId">Session id to tag every <c>log</c> row with. Must equal <see cref="IActiveSessionContext.ActiveSessionId"/> when events are written.</param>
    /// <param name="ct">Cancellation token. Currently honoured only for the consumer-spawn step; future awaits inside the consumer use the channel's completion as the stop signal.</param>
    /// <exception cref="InvalidOperationException">Thrown if the writer is already started.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the writer has been disposed.</exception>
    public Task StartAsync(string sessionId, CancellationToken ct)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(sessionId);
        if (_startedSessionId is not null)
        {
            throw new InvalidOperationException("SessionEventWriter is already started; call StopAsync before starting again.");
        }

        _channel = Channel.CreateBounded<MiningEvent>(new BoundedChannelOptions(4096)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        _db.RowChanged += OnRowChanged;
        _startedSessionId = sessionId;
        _consumer = Task.Run(ConsumeLoopAsync, CancellationToken.None);

        _log.LogDebug("SessionEventWriter started for session {SessionId}", sessionId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Complete the channel writer, drain pending events through the consumer,
    /// then unsubscribe from the SQL update hook. Safe to call when not
    /// started; safe to call repeatedly.
    /// </summary>
    /// <param name="ct">Cancellation token. The drain itself is not cancellable - all pending events are written before <see cref="StopAsync"/> returns.</param>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_startedSessionId is null) return;

        _channel?.Writer.TryComplete();
        if (_consumer is not null)
        {
            try
            {
                await _consumer.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "SessionEventWriter consumer faulted during stop; continuing.");
            }
        }

        _db.RowChanged -= OnRowChanged;
        _channel = null;
        _consumer = null;
        _startedSessionId = null;

        // Clear the auto-resume hook so a stale callback from the
        // just-stopped session can never fire on a Tick/Crit that races into
        // the next session's startup window.
        _onAutoResumeRequested = null;
        lock (_resumeLock) { _inFlightResume = null; }

        _log.LogDebug("SessionEventWriter stopped");
    }

    /// <summary>
    /// Register (or clear with <c>null</c>) the callback invoked
    /// inline inside <see cref="WriteAsync"/> when a Tick/Crit arrives while
    /// the session is paused. The callback is expected to perform the
    /// Paused -&gt; Running transition; <see cref="WriteAsync"/> awaits it,
    /// re-reads <see cref="IActiveSessionContext.State"/>, and on Running
    /// falls through to the normal enqueue path so the triggering event lands
    /// in the <c>log</c> table.
    /// </summary>
    /// <param name="callback">Async callback, or <c>null</c> to clear.</param>
    public void SetAutoResumeCallback(Func<CancellationToken, Task>? callback)
        => _onAutoResumeRequested = callback;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Auto-resume: when <see cref="IActiveSessionContext.State"/> is
    /// <see cref="SessionRunState.Paused"/> and the incoming event is a
    /// <see cref="MiningEventKind.Tick"/> or <see cref="MiningEventKind.Crit"/>
    /// AND <see cref="SetAutoResumeCallback"/> has been called with a non-null
    /// delegate, this method awaits the callback (single-flight via the
    /// <see cref="_inFlightResume"/> cache) before re-checking state. If the
    /// callback flips state to <see cref="SessionRunState.Running"/>, the
    /// event proceeds through the normal enqueue path and is INSERTed; if it
    /// fails (throws) or leaves state at <see cref="SessionRunState.Paused"/>,
    /// the event is dropped. Non-trigger paused events (Residue, Depleted, or
    /// any event when no callback is registered) drop silently as before.
    /// </para>
    /// </remarks>
    public async Task WriteAsync(MiningEvent ev, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ThrowIfDisposed();

        // Drop rules - silent (parser / activeSession / session-controls are
        // responsible for telling the user; the writer is a downstream sink).
        if (_activeSession.ActiveSessionId is null) return;

        if (_activeSession.State == SessionRunState.Paused)
        {
            // Auto-resume path: only Tick/Crit (the qualifying events)
            // and only when a callback is registered. Everything else drops
            // silently like before.
            if ((ev.Kind == MiningEventKind.Tick || ev.Kind == MiningEventKind.Crit)
                && _onAutoResumeRequested is { } resume)
            {
                try
                {
                    Task waitOn;
                    lock (_resumeLock)
                    {
                        // Reuse an in-flight (non-completed, non-faulted) task so a
                        // burst of paused Ticks share one resume. Faulted/completed
                        // tasks get replaced so the next caller can retry.
                        if (_inFlightResume is { IsCompleted: false } already)
                        {
                            waitOn = already;
                        }
                        else
                        {
                            // Invoking `resume(ct)` may throw synchronously (e.g. if
                            // the registered delegate is a non-async lambda that
                            // throws). The enclosing try/catch handles both that
                            // case and the awaited-faulted-task case below.
                            _inFlightResume = waitOn = resume(ct);
                        }
                    }
                    await waitOn.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Auto-resume callback threw; dropping triggering {Kind} event.", ev.Kind);
                    return;
                }

                // Callback finished. If state is now Running, fall through to
                // the enqueue path; if still Paused, drop.
                if (_activeSession.State != SessionRunState.Running) return;
            }
            else
            {
                // Non-trigger paused event (Residue / Depleted), or no callback
                // registered yet. Silent drop, same as today.
                return;
            }
        }

        if ((ev.Kind == MiningEventKind.Tick || ev.Kind == MiningEventKind.Crit)
            && ev.TypeId is null)
        {
            return;
        }

        var ch = _channel;
        if (ch is null) return; // not started; drop.

        await ch.Writer.WriteAsync(ev, ct).ConfigureAwait(false);
    }

    /// <summary>Sequentially drains the channel and INSERTs each event.</summary>
    private async Task ConsumeLoopAsync()
    {
        var ch = _channel;
        if (ch is null) return;

        await foreach (var ev in ch.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var sid = _activeSession.ActiveSessionId;
            if (sid is null) continue; // session ended mid-drain; skip.
            try
            {
                await InsertLogRowAsync(ev, sid).ConfigureAwait(false);
            }
            catch (DbException dbex)
            {
                _log.LogError(dbex,
                    "Failed to INSERT log row for {Kind} (pilot {Pilot}, type {TypeId}); continuing.",
                    ev.Kind, ev.CharacterId, ev.TypeId);
            }
            catch (Exception ex)
            {
                // Belt-and-suspenders: a NON-DbException (e.g. a
                // driver-internal ArgumentOutOfRangeException raised by concurrent
                // connection access) must NOT escape this loop. If it did, the
                // background consumer Task would fault and die unobserved, and
                // every subsequent mining event for the session would be enqueued
                // but never written — silent data loss while the app keeps
                // running. Log and keep draining instead.
                _log.LogError(ex,
                    "Unexpected error inserting log row for {Kind} (pilot {Pilot}, type {TypeId}); the consumer continues draining.",
                    ev.Kind, ev.CharacterId, ev.TypeId);
            }
        }
    }

    /// <summary>INSERTs a single row into <c>log</c>. <see cref="_currentInsertKind"/> is set so the synchronous update-hook callback knows what kind to forward.</summary>
    private async Task InsertLogRowAsync(MiningEvent ev, string sessionId)
    {
        // For Tick / Crit / Depleted with a known TypeId, denormalise volume_m3.
        // For Residue (TypeId always null) and any kind without a known type,
        // volume_m3 stays null in the row.
        var volumeM3 = MiningEventRow.VolumeFor(_sde, ev.TypeId, ev.Amount);

        var ts = MiningEventRow.FormatTimestamp(ev.EveTimestamp);

        // Stamp the pilot's CURRENT role on this row. GetRole returns
        // the role from the in-memory map seeded by SessionController.StartAsync
        // (or SetPilotRoleAsync for mid-session changes).
        var role = _activeSession.GetRole(ev.CharacterId);

        var parameters = new Dictionary<string, object?>
        {
            ["$sid"]   = sessionId,
            ["$pid"]   = ev.CharacterId,
            ["$ts"]    = ts,
            ["$kind"]  = ev.Kind.ToString(),
            ["$type"]  = ev.TypeId,
            ["$units"] = ev.Amount,
            ["$vol"]   = volumeM3,
            ["$role"]  = PilotRoleExtensions.ToCanonicalString(role),
        };

        _currentInsertKind = ev.Kind;
        try
        {
            await _db.ExecuteNonQueryAsync(InsertSql, parameters, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _currentInsertKind = null;
        }
    }

    /// <summary>SQL update-hook callback. Filters for INSERTs into <c>log</c> and raises <see cref="SessionEventAdded"/> with subscriber isolation.</summary>
    private void OnRowChanged(object? sender, SqliteRowChangedEventArgs e)
    {
        if (e.ChangeKind != SqliteRowChangeKind.Insert) return;
        if (!string.Equals(e.Table, "log", StringComparison.Ordinal)) return;

        // Read the in-flight kind (set by InsertLogRowAsync just before the
        // INSERT command ran on this same thread). Null means the INSERT did
        // not originate from our writer's pipeline - ignore.
        var kind = _currentInsertKind;
        if (kind is null) return;

        var sid = _activeSession.ActiveSessionId;
        if (sid is null) return;

        var handler = SessionEventAdded;
        if (handler is null) return;

        var args = new SessionEventAddedEventArgs(sid, e.RowId, kind.Value);
        foreach (var d in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<SessionEventAddedEventArgs>)d)(this, args);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Subscriber threw on SessionEventAdded; continuing with remaining handlers.");
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SessionEventWriter));
    }

    private const string InsertSql =
        "INSERT INTO log (session_id, pilot_id, eve_timestamp, event_kind, type_id, units, volume_m3, role) " +
        "VALUES ($sid, $pid, $ts, $kind, $type, $units, $vol, $role)";
}
