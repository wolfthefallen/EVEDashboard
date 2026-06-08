namespace EveDashboard.Stats.Events;

using EveDashboard.Core.Domain;

/// <summary>
/// Payload for <see cref="ISessionEventNotifier.SessionEventAdded"/>. Identifies
/// the freshly-INSERTed <c>log</c> row by session id and log id, and carries
/// the event kind so subscribers can filter without a re-query.
/// </summary>
public sealed class SessionEventAddedEventArgs : EventArgs
{
    /// <summary>Construct a session-event-added payload.</summary>
    /// <param name="sessionId">EVE Dashboard <c>session.session_id</c>.</param>
    /// <param name="logId">EVE Dashboard <c>log.log_id</c> of the newly-inserted row.</param>
    /// <param name="kind">Mining event kind written into the row.</param>
    public SessionEventAddedEventArgs(string sessionId, long logId, MiningEventKind kind)
    {
        SessionId = sessionId;
        LogId = logId;
        Kind = kind;
    }

    /// <summary>The session (sha256 hex id) this event was tagged with.</summary>
    public string SessionId { get; }

    /// <summary>Newly-inserted <c>log</c> row id.</summary>
    public long LogId { get; }

    /// <summary>Mining event kind (<c>Tick</c> / <c>Crit</c> / <c>Residue</c> / <c>Depleted</c>).</summary>
    public MiningEventKind Kind { get; }
}
