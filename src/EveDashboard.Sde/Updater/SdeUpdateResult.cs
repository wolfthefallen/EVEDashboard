namespace EveDashboard.Sde.Updater;

/// <summary>
/// Outcome of a single <see cref="ISdeUpdater.CheckAndUpdateAsync"/> call.
/// Closed discriminated-union — only the three nested case types defined
/// below are valid concrete subtypes.
/// </summary>
/// <remarks>
/// The private parameterless constructor is what makes the hierarchy closed:
/// only nested types within <see cref="SdeUpdateResult"/> can call it, so
/// external assemblies cannot introduce a fourth case. Per plan 08 decision 8.12.
/// </remarks>
public abstract record SdeUpdateResult
{
    private SdeUpdateResult() { }

    /// <summary>
    /// Upstream snapshot is at or behind the locally-stored snapshot —
    /// nothing to import. Either the conditional HEAD's headers matched
    /// <c>meta.last_modified</c> / <c>meta.etag</c>, or the conditional
    /// GET returned HTTP 304 Not Modified.
    /// </summary>
    public sealed record NoUpdateNeeded : SdeUpdateResult
    {
        /// <summary>Construct the no-update-needed case.</summary>
        public NoUpdateNeeded() { }
    }

    /// <summary>
    /// A newer upstream snapshot was downloaded, decompressed, validated,
    /// and committed to the live SDE database. Subscribers to
    /// <see cref="ISdeUpdater.Updated"/> have been notified.
    /// </summary>
    /// <param name="BuildTime">
    /// The <c>meta.build_time</c> value from the newly-committed payload —
    /// everef's own snapshot timestamp.
    /// </param>
    public sealed record Updated(DateTimeOffset BuildTime) : SdeUpdateResult;

    /// <summary>
    /// The update could not be applied because of a network, upstream, or
    /// import-time failure. The local SDE is untouched. The caller may
    /// retry on the next launch (per plan 08 decision 8.6 — single attempt,
    /// no in-process retry).
    /// </summary>
    /// <param name="Reason">
    /// Human-readable explanation. Suitable for logs and a future status
    /// indicator. The underlying exception (if any) has already been logged
    /// at <c>Warning</c> by the updater.
    /// </param>
    public sealed record Unreachable(string Reason) : SdeUpdateResult;
}
