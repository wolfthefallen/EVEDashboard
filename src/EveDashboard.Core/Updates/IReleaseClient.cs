namespace EveDashboard.Core.Updates;

/// <summary>
/// Fetches the latest published release of the application from its hosting
/// service (GitHub). Read-only network access; implementations live in
/// <c>EveDashboard.Updates</c>.
/// </summary>
/// <remarks>
/// The launch update check must never disrupt startup, so implementations are
/// fail-soft: any failure (no network, non-success status, malformed payload)
/// is logged and surfaced as <c>null</c> rather than thrown.
/// </remarks>
public interface IReleaseClient
{
    /// <summary>Fetch the latest published release.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The latest <see cref="ReleaseInfo"/>, or <c>null</c> when there is no
    /// published release or the request/parse failed (the failure is logged).
    /// </returns>
    Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default);
}
