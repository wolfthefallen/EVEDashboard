namespace EveDashboard.Stats.Fleets;

using System.Globalization;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="IFleetRepository"/> implementation backed by an injected
/// <see cref="ISqliteDatabase"/>. Owns the <c>fleet</c> / <c>fleet_pilot</c>
/// tables; pilot names are reused from the existing <c>pilot</c> table
/// (plan 42). Holds no mutable state — safe to register as a singleton alongside
/// the singleton session <see cref="ISqliteDatabase"/>.
/// </summary>
public sealed class SqliteFleetRepository : IFleetRepository
{
    /// <summary>ISO-8601 UTC millisecond format, matching <c>session_time.start_time</c> (no shared helper yet — CLEANUP 2.1).</summary>
    private const string IsoUtcFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    private readonly ISqliteDatabase _db;
    private readonly ILogger<SqliteFleetRepository> _log;

    /// <summary>Construct a fleet repository over the given SQLite database.</summary>
    /// <param name="db">SQLite database wrapper — typically the singleton session DB.</param>
    /// <param name="log">Logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="db"/> or <paramref name="log"/> is null.</exception>
    public SqliteFleetRepository(ISqliteDatabase db, ILogger<SqliteFleetRepository> log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(log);
        _db = db;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Fleet>> GetFleetsAsync(CancellationToken ct = default)
    {
        var fleetRows = await _db.QueryAsync(
            "SELECT fleet_id, name, created_at FROM fleet ORDER BY name",
            static r => (Id: r.GetString(0), Name: r.GetString(1), CreatedAt: r.GetString(2)),
            ct: ct);

        var result = new List<Fleet>(fleetRows.Count);
        foreach (var f in fleetRows)
        {
            var members = await _db.QueryAsync(
                """
                SELECT fp.pilot_id, p.name, fp.role
                FROM fleet_pilot fp
                JOIN pilot p ON p.pilot_id = fp.pilot_id
                WHERE fp.fleet_id = $id
                ORDER BY p.name
                """,
                static r => new FleetMember(
                    r.GetInt64(0), r.GetString(1), PilotRoleExtensions.Parse(r.GetString(2))),
                new Dictionary<string, object?> { ["$id"] = f.Id }, ct);
            result.Add(new Fleet(f.Id, f.Name, f.CreatedAt, members));
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<FleetCreateResult> CreateFleetAsync(
        string name,
        IReadOnlyList<FleetMember> members,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(members);

        // Decision 3.4 = A: trim once; the trimmed name feeds the hash, the
        // uniqueness check, and the stored row so all three agree.
        var trimmed = name.Trim();
        var fleetId = FleetId.Compute(trimmed, members.Select(m => (m.PilotId, m.Role)).ToList());
        var createdAt = DateTimeOffset.UtcNow.ToString(IsoUtcFormat, CultureInfo.InvariantCulture);

        var result = await _db.WithTransactionAsync<FleetCreateResult>(async (tx, _) =>
        {
            // Decision 2.4 = B: reject a duplicate display name. No separate
            // id-existence check (decision 3.1 = A) — an identical id implies an
            // identical name, already caught here. On a duplicate the body simply
            // returns without writing — WithTransactionAsync COMMITs an empty
            // transaction (no rows changed), equivalent to the old explicit ROLLBACK.
            var dup = await tx.QueryScalarAsync<long>(
                "SELECT 1 FROM fleet WHERE name = $n LIMIT 1",
                new Dictionary<string, object?> { ["$n"] = trimmed }, ct);
            if (dup != 0)
            {
                _log.LogInformation("Fleet create rejected: name '{Name}' already in use.", trimmed);
                return new FleetCreateResult(FleetCreateStatus.DuplicateName, null);
            }

            // FK target: ensure each member exists in pilot (they are discovered
            // characters at create time, so the name is known).
            foreach (var m in members)
            {
                await tx.ExecuteNonQueryAsync(
                    "INSERT OR IGNORE INTO pilot (pilot_id, name) VALUES ($pid, $name)",
                    new Dictionary<string, object?> { ["$pid"] = m.PilotId, ["$name"] = m.Name }, ct);
            }

            await tx.ExecuteNonQueryAsync(
                "INSERT INTO fleet (fleet_id, name, created_at) VALUES ($id, $n, $ts)",
                new Dictionary<string, object?> { ["$id"] = fleetId, ["$n"] = trimmed, ["$ts"] = createdAt }, ct);

            foreach (var m in members)
            {
                await tx.ExecuteNonQueryAsync(
                    "INSERT INTO fleet_pilot (fleet_id, pilot_id, role, created_at) VALUES ($id, $pid, $role, $ts)",
                    new Dictionary<string, object?>
                    {
                        ["$id"]   = fleetId,
                        ["$pid"]  = m.PilotId,
                        ["$role"] = PilotRoleExtensions.ToCanonicalString(m.Role),
                        ["$ts"]   = createdAt,
                    }, ct);
            }

            return new FleetCreateResult(FleetCreateStatus.Created, fleetId);
        }, immediate: false, ct).ConfigureAwait(false);

        if (result.Status == FleetCreateStatus.Created)
        {
            _log.LogInformation(
                "Created fleet '{Name}' ({FleetId}) with {Count} member(s).", trimmed, fleetId, members.Count);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task DeleteFleetAsync(string fleetId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fleetId);

        await _db.WithTransactionAsync(async (tx, _) =>
        {
            await tx.ExecuteNonQueryAsync(
                "DELETE FROM fleet_pilot WHERE fleet_id = $id",
                new Dictionary<string, object?> { ["$id"] = fleetId }, ct);
            await tx.ExecuteNonQueryAsync(
                "DELETE FROM fleet WHERE fleet_id = $id",
                new Dictionary<string, object?> { ["$id"] = fleetId }, ct);
        }, immediate: false, ct).ConfigureAwait(false);

        _log.LogInformation("Deleted fleet {FleetId}.", fleetId);
    }
}
