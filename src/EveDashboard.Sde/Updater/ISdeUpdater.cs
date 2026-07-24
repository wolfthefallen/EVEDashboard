namespace EveDashboard.Sde.Updater;

/// <summary>
/// Runtime updater for the EVE Static Data Export. Probes
/// <see href="https://data.everef.net/reference-data/reference-data-latest.tar.xz"/>
/// for a newer snapshot than the locally-stored one and, if found, downloads
/// it, decompresses (xz → tar), and imports it into the live SDE database
/// in-place. WAL mode (enabled at SDE creation by plan 07's
/// <c>SchemaInitializer</c>) lets concurrent readers keep working throughout.
/// </summary>
/// <remarks>
/// Plan 08's deliverable. Call site (plan 14, <c>App.xaml.cs OnStartup</c>)
/// is expected to invoke <see cref="CheckAndUpdateAsync"/> exactly once at
/// application startup, on a background thread, without blocking UI.
/// Subscribers to <see cref="Updated"/> (plan 09's <c>ISdeLookup</c>) rebuild
/// in-memory caches when a new payload commits.
/// </remarks>
public interface ISdeUpdater
{
    /// <summary>
    /// Probe everef.net for a newer reference-data snapshot. If newer,
    /// download, decompress, import into the live SDE database, validate,
    /// and commit atomically. Raises <see cref="Updated"/> exactly once on
    /// successful commit.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="SdeUpdateResult.NoUpdateNeeded"/> if upstream is at or
    /// behind the local snapshot; <see cref="SdeUpdateResult.Updated"/> on
    /// a successful in-place import; <see cref="SdeUpdateResult.Unreachable"/>
    /// on any network or upstream failure (timeout, DNS, 5xx, malformed
    /// payload, validation-threshold tripped).
    /// </returns>
    /// <remarks>
    /// Single attempt — no retry. Per plan 08 decision 8.6: callers wanting
    /// to retry can reinvoke. Throws only on programmer errors. All transient
    /// failures are reported via <see cref="SdeUpdateResult.Unreachable"/>.
    /// </remarks>
    Task<SdeUpdateResult> CheckAndUpdateAsync(CancellationToken ct = default);

    /// <summary>
    /// Raised exactly once per successful in-place commit. Subscribers
    /// should rebuild any in-memory caches keyed off SDE contents.
    /// </summary>
    /// <remarks>
    /// Subscriber invocation is isolated per-handler — if one subscriber
    /// throws, the exception is logged at <c>Warning</c> and remaining
    /// subscribers still observe the event. Per plan 08 decision 8.20.
    /// </remarks>
    event EventHandler<SdeUpdatedEventArgs>? Updated;
}
