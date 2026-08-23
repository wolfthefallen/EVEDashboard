namespace EveDashboard.Core.Storage;

/// <summary>
/// Common-table-expression body that filters <c>log</c> rows by event-time
/// role per the filter rules listed in the remarks. Consumer queries
/// interpolate this constant in front of a SELECT against <c>eligible_log</c>.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from
/// <c>SqliteSessionStatsRepository</c> and <c>SqliteReportingQueries</c> so
/// the role-filter SQL lives in exactly one place. Both consumers use the
/// canonical <c>$sid</c> parameter name for the session id; callers must bind
/// <c>$sid</c> in their parameter dictionary.
/// </para>
/// <para>
/// Filter rules (event-time role on <c>log.role</c>):
/// </para>
/// <list type="number">
///   <item><description><c>Cyno</c> rows are always excluded.</description></item>
///   <item><description><c>Combat</c> rows are always excluded.</description></item>
///   <item><description><c>Booster</c> rows are excluded unless the pilot has at least one Booster-tagged row with <c>volume_m3 &gt; 0</c> in this session (the m^3 gate).</description></item>
///   <item><description><c>Miner</c> rows are always included.</description></item>
/// </list>
/// <para>
/// Note: Residue rows for a Booster pilot have <c>volume_m3 = NULL</c> and
/// therefore do NOT trip the gate; only a row with positive m^3 flips a
/// Booster pilot from excluded to included. This is intentional — boost
/// support that produces no mined ore stays excluded.
/// </para>
/// </remarks>
public static class RoleEligibilityCte
{
    /// <summary>
    /// The CTE clause. Append a SELECT against <c>eligible_log</c> after this
    /// string. Example:
    /// <code>
    /// const string MyQuery = RoleEligibilityCte.Sql + """
    ///     SELECT SUM(volume_m3) FROM eligible_log
    /// """;
    /// </code>
    /// The caller must bind <c>$sid</c>.
    /// </summary>
    public const string Sql = """
        WITH eligible_log AS (
            SELECT * FROM log
            WHERE session_id = $sid
              AND role NOT IN ('Cyno','Combat')
              AND (role <> 'Booster'
                   OR pilot_id IN (
                       SELECT pilot_id FROM log
                       WHERE session_id = $sid AND role = 'Booster' AND volume_m3 > 0
                   ))
        )
        """;
}
