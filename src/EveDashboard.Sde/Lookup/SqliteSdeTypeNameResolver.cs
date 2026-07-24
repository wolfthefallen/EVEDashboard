namespace EveDashboard.Sde.Lookup;

using EveDashboard.Core.Lookup;
using EveDashboard.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="ISdeTypeNameResolver"/> backed by a direct query against the SDE
/// <c>types</c> table. Unlike <see cref="SqliteSdeLookup"/> (which caches only
/// ore + gas), this resolves ANY published type by name, so the Haul Value paste
/// box can price non-mining items too. One indexed lookup per call
/// (<c>idx_types_name</c> exists), which is fine for the handful of lines in a
/// pasted haul.
/// </summary>
public sealed class SqliteSdeTypeNameResolver : ISdeTypeNameResolver
{
    private readonly ISqliteDatabase _sde;
    private readonly ILogger<SqliteSdeTypeNameResolver> _log;

    /// <summary>Construct the resolver over the SDE SQLite database.</summary>
    /// <param name="sde">The SDE SQLite wrapper (the <c>eve.sqlite</c> store).</param>
    /// <param name="log">Logger for diagnostics.</param>
    /// <exception cref="System.ArgumentNullException">Thrown if either argument is null.</exception>
    public SqliteSdeTypeNameResolver(ISqliteDatabase sde, ILogger<SqliteSdeTypeNameResolver> log)
    {
        ArgumentNullException.ThrowIfNull(sde);
        ArgumentNullException.ThrowIfNull(log);
        _sde = sde;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<long?> ResolveTypeIdAsync(string name, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return null;

        var rows = await _sde.QueryAsync(
            "SELECT type_id FROM types WHERE name = $n COLLATE NOCASE LIMIT 1",
            r => r.GetInt64(0),
            new Dictionary<string, object?> { ["$n"] = trimmed }, ct).ConfigureAwait(false);

        if (rows.Count != 0) return rows[0];
        _log.LogDebug("SDE type-name resolve found no type named '{Name}'.", trimmed);
        return null;
    }
}
