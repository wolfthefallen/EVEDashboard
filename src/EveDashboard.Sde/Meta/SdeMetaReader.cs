namespace EveDashboard.Sde.Meta;

using System.Globalization;
using EveDashboard.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Reads the SDE snapshot build time from the live SDE database's
/// <c>meta</c> table. Plan 52: feeds the About window's "SDE data" line.
/// </summary>
/// <remarks>
/// <para>
/// <c>meta.build_time</c> is the raw everef ISO-8601 string written by
/// <see cref="EveDashboard.Sde.Updater.EverefSdeUpdater"/>. It is read as a
/// <see cref="string"/> (the <see cref="ISqliteDatabase.QueryScalarAsync{T}"/>
/// contract supports int/long/string scalars only) then parsed with the same
/// invariant, assume-universal options the updater uses.
/// </para>
/// <para>
/// Bad data is non-fatal: a missing row, an empty value, or an unparseable
/// timestamp all return <c>null</c> (logged at Warning) so a degraded SDE never
/// breaks the About window.
/// </para>
/// </remarks>
public sealed class SdeMetaReader : ISdeMetaReader
{
    private readonly ISqliteDatabase _db;
    private readonly ILogger<SdeMetaReader> _log;

    /// <summary>Construct the reader.</summary>
    /// <param name="db">Open SDE SQLite wrapper (the database holding the <c>meta</c> table).</param>
    /// <param name="log">Logger for malformed/missing-data diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public SdeMetaReader(ISqliteDatabase db, ILogger<SdeMetaReader> log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(log);
        _db = db;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetBuildTimeAsync(CancellationToken ct = default)
    {
        var raw = await _db.QueryScalarAsync<string>(
            "SELECT build_time FROM meta WHERE id = 1", null, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(raw))
        {
            _log.LogWarning("SDE meta.build_time is missing or empty; About window shows 'unknown'.");
            return null;
        }

        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
        {
            return dto;
        }

        _log.LogWarning("SDE meta.build_time '{Raw}' is not a parseable timestamp; About window shows 'unknown'.", raw);
        return null;
    }
}
