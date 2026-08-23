namespace EveDashboard.Core.Sessions;

/// <summary>
/// Read/write surface over the <c>fleet</c> / <c>fleet_pilot</c> tables.
/// Owns only those two tables; the default-fleet pointer and the
/// persisted sort order are <c>setting</c> rows owned by
/// <see cref="Settings.ISettingsRepository"/> and coordinated by the view-model.
/// </summary>
/// <remarks>
/// Fleets are immutable by content id — "editing" a fleet is delete + recreate.
/// The only mutations are create and delete.
/// </remarks>
public interface IFleetRepository
{
    /// <summary>
    /// Load all saved fleets, ordered by name, each with its members
    /// (names + roles joined from the <c>pilot</c> table).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The saved fleets in name order; empty when none exist.</returns>
    Task<IReadOnlyList<Fleet>> GetFleetsAsync(CancellationToken ct = default);

    /// <summary>
    /// Persist a new fleet. The name is trimmed before the
    /// content id is computed and the row is written. A name already in use is
    /// rejected with <see cref="FleetCreateStatus.DuplicateName"/>.
    /// </summary>
    /// <param name="name">User-supplied display name (trimmed internally).</param>
    /// <param name="members">The fleet's pilots and roles (at least one).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The create outcome plus the new fleet id on success.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="name"/> or <paramref name="members"/> is null.</exception>
    Task<FleetCreateResult> CreateFleetAsync(
        string name,
        IReadOnlyList<FleetMember> members,
        CancellationToken ct = default);

    /// <summary>
    /// Delete a fleet and its members. No-op if the id does not exist. Does not
    /// touch the <c>default_fleet_id</c> setting — the caller clears that if the
    /// deleted fleet was the default.
    /// </summary>
    /// <param name="fleetId">Content-addressed id of the fleet to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="fleetId"/> is null.</exception>
    Task DeleteFleetAsync(string fleetId, CancellationToken ct = default);
}

/// <summary>Result of <see cref="IFleetRepository.CreateFleetAsync"/>.</summary>
/// <param name="Status">Whether the fleet was created or rejected.</param>
/// <param name="FleetId">The new fleet id on <see cref="FleetCreateStatus.Created"/>; otherwise <c>null</c>.</param>
public sealed record FleetCreateResult(FleetCreateStatus Status, string? FleetId);

/// <summary>Outcome categories for a fleet-create attempt.</summary>
/// <remarks>
/// There is intentionally no <c>AlreadyExists</c> member: the
/// trimmed name is inside the content hash and is uniquely indexed and checked
/// first, so an identical id always implies an identical name and surfaces as
/// <see cref="DuplicateName"/>.
/// </remarks>
public enum FleetCreateStatus
{
    /// <summary>The fleet was persisted.</summary>
    Created,

    /// <summary>A fleet with the same (trimmed) name already exists; nothing was written.</summary>
    DuplicateName,
}
