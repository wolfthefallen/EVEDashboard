namespace EveDashboard.Sde.Meta;

/// <summary>Reads version/build metadata from the live SDE database.</summary>
/// <remarks>
/// Plan 52: the About window surfaces the SDE snapshot's build time as a single
/// "SDE data: yyyy-MM-dd" line. The reader is deliberately null-tolerant — a
/// missing or unparseable value yields <c>null</c> rather than an exception so a
/// degraded SDE never breaks the About window.
/// </remarks>
public interface ISdeMetaReader
{
    /// <summary>
    /// Read the SDE snapshot build time from <c>meta.build_time</c>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed build time, or <c>null</c> if the row is missing,
    /// empty, or unparseable.</returns>
    Task<DateTimeOffset?> GetBuildTimeAsync(CancellationToken ct = default);
}
