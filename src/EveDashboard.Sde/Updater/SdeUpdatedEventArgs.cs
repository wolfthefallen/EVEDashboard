namespace EveDashboard.Sde.Updater;

/// <summary>
/// Payload for <see cref="ISdeUpdater.Updated"/>. Raised exactly once per
/// successful in-place SDE commit; subscribers should rebuild any caches
/// keyed off SDE contents (e.g. plan 09's <c>ISdeLookup</c>).
/// </summary>
public sealed class SdeUpdatedEventArgs : EventArgs
{
    /// <summary>
    /// Construct the event args with the build time of the freshly-committed
    /// snapshot.
    /// </summary>
    /// <param name="buildTime">
    /// The <c>meta.build_time</c> value from the newly-committed payload —
    /// everef's own snapshot timestamp, ISO-8601 UTC.
    /// </param>
    public SdeUpdatedEventArgs(DateTimeOffset buildTime)
    {
        BuildTime = buildTime;
    }

    /// <summary>The newly-committed snapshot's build time.</summary>
    public DateTimeOffset BuildTime { get; }
}
