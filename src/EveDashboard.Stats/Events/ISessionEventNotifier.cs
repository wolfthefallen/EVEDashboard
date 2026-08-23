namespace EveDashboard.Stats.Events;

/// <summary>
/// Fan-out of session-event INSERT notifications. Widget ViewModels
/// subscribe here; the writer (<c>SessionEventWriter</c>) is the producer.
/// </summary>
/// <remarks>
/// <para>
/// The architecture intent:
/// the SQL update hook on the writer's connection is the truth signal; this
/// event is the in-process fan-out mechanism. Subscribers handle the event
/// idempotently - the standard pattern is to re-query the stats layer.
/// </para>
/// <para>
/// Subscriber failures are isolated: an exception thrown by one handler does
/// not prevent the other handlers from observing the event (the same
/// per-handler isolation the SDE updater's <c>Updated</c> event uses).
/// Every failure is logged at <c>Warning</c>.
/// </para>
/// </remarks>
public interface ISessionEventNotifier
{
    /// <summary>
    /// Raised after every successful row INSERT into the <c>log</c> table by
    /// the session-event writer.
    /// </summary>
    event EventHandler<SessionEventAddedEventArgs>? SessionEventAdded;
}
