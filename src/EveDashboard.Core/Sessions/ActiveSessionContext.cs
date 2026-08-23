namespace EveDashboard.Core.Sessions;

/// <summary>
/// Default <see cref="IActiveSessionContext"/> implementation. Thread-safe via
/// a private lock around the field reads/writes; the
/// <see cref="IActiveSessionContext.ActiveSessionChanged"/> event is raised
/// outside the lock to avoid re-entrancy if a subscriber calls back into the
/// context while handling.
/// </summary>
public sealed class ActiveSessionContext : IActiveSessionContext
{
    private readonly object _lock = new();
    private string? _id;
    private SessionRunState _state = SessionRunState.None;

    /// <summary>
    /// Per-pilot role map. Mirrors <c>session_pilot.role</c> for the
    /// active session. Populated by <see cref="SeedRoles"/> at session start,
    /// mutated by <see cref="SetRole"/> on user role-change, cleared by
    /// <see cref="ClearRoles"/> at session stop. Guarded by <c>_lock</c>.
    /// </summary>
    private readonly Dictionary<long, PilotRole> _roles = new();

    /// <inheritdoc />
    public string? ActiveSessionId
    {
        get { lock (_lock) { return _id; } }
    }

    /// <inheritdoc />
    public SessionRunState State
    {
        get { lock (_lock) { return _state; } }
    }

    /// <inheritdoc />
    public event EventHandler? ActiveSessionChanged;

    /// <inheritdoc />
    public event EventHandler<PilotRoleChangedEventArgs>? RolesChanged;

    /// <summary>
    /// Atomically update the active session id and run state. If either field
    /// changes, raises <see cref="ActiveSessionChanged"/> exactly once.
    /// </summary>
    /// <param name="sessionId">New active session id (sha256 hex), or <c>null</c> to clear.</param>
    /// <param name="state">New run state.</param>
    public void Set(string? sessionId, SessionRunState state)
    {
        bool changed;
        lock (_lock)
        {
            changed = _id != sessionId || _state != state;
            _id = sessionId;
            _state = state;
        }

        if (changed)
        {
            ActiveSessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public PilotRole GetRole(long pilotId)
    {
        lock (_lock)
        {
            return _roles.TryGetValue(pilotId, out var role) ? role : PilotRole.Miner;
        }
    }

    /// <summary>
    /// Mutate a pilot's role in the in-memory map. Raises
    /// <see cref="RolesChanged"/> once if the role actually changed (no event
    /// fires on a no-op). Called by <c>SessionController.SetPilotRoleAsync</c>
    /// after the SQL UPDATE commits.
    /// </summary>
    /// <param name="pilotId">EVE character id whose role is changing.</param>
    /// <param name="role">New role.</param>
    public void SetRole(long pilotId, PilotRole role)
    {
        PilotRole oldRole;
        bool changed;
        lock (_lock)
        {
            var hadOld = _roles.TryGetValue(pilotId, out oldRole);
            if (!hadOld) oldRole = PilotRole.Miner;
            changed = !hadOld || oldRole != role;
            _roles[pilotId] = role;
        }

        if (changed)
        {
            RolesChanged?.Invoke(this, new PilotRoleChangedEventArgs(pilotId, oldRole, role));
        }
    }

    /// <summary>
    /// Bulk-populate the role map at session start. Clears any
    /// pre-existing entries before copying in. Does NOT raise
    /// <see cref="RolesChanged"/> — seeding is initial setup, not a
    /// user-facing role change.
    /// </summary>
    /// <param name="roles">Pilot-id -> role map captured from the just-INSERTed <c>session_pilot</c> rows.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="roles"/> is null.</exception>
    public void SeedRoles(IReadOnlyDictionary<long, PilotRole> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        lock (_lock)
        {
            _roles.Clear();
            foreach (var kv in roles)
            {
                _roles[kv.Key] = kv.Value;
            }
        }
    }

    /// <summary>
    /// Empty the role map. Called by <c>SessionController.StopInternalAsync</c>
    /// after the session ends so the map doesn't leak roles into the next
    /// session's seed.
    /// </summary>
    public void ClearRoles()
    {
        lock (_lock)
        {
            _roles.Clear();
        }
    }
}
