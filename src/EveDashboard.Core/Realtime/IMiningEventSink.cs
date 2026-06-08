namespace EveDashboard.Core.Realtime;

using EveDashboard.Core.Domain;

/// <summary>
/// Single-method consumer interface for parsed <see cref="MiningEvent"/>
/// records. The watcher (plan 12, in <c>EveDashboard.Mining.Realtime</c>) pushes
/// events through this; the writer (plan 12, in <c>EveDashboard.Stats.Writer</c>)
/// implements it. Decoupling via this interface keeps
/// <c>EveDashboard.Mining</c> off any reference to <c>EveDashboard.Stats</c>.
/// </summary>
/// <remarks>
/// Implementations may queue, drop (e.g. when no session is active or the
/// session is paused), or write immediately. The watcher does not inspect
/// the result; it simply <c>await</c>s the call.
/// </remarks>
public interface IMiningEventSink
{
    /// <summary>
    /// Hand a parsed mining event to the sink.
    /// </summary>
    /// <param name="ev">The parsed event. Must not be null.</param>
    /// <param name="ct">Cancellation token. The sink honours it on any
    /// internal await (e.g. while waiting on a bounded channel that is full).</param>
    /// <returns>A task that completes when the sink has accepted the event
    /// (enqueued it, dropped it, or written it).</returns>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="ev"/> is null.</exception>
    /// <exception cref="System.OperationCanceledException">Thrown if <paramref name="ct"/> is cancelled while awaiting an internal queue.</exception>
    Task WriteAsync(MiningEvent ev, CancellationToken ct);
}
