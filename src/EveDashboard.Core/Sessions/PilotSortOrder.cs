namespace EveDashboard.Core.Sessions;

/// <summary>
/// Order in which the fleet-builder's pilot picker lists discovered pilots
/// (plan 42, decision 1.6 = A). Persisted across restarts as a <c>setting</c>
/// row (decision 1.7 = A).
/// </summary>
public enum PilotSortOrder
{
    /// <summary>Most-recently-active gamelog first. The default and the discovery baseline order.</summary>
    LastActiveNewest,

    /// <summary>Least-recently-active gamelog first (the baseline reversed).</summary>
    LastActiveOldest,

    /// <summary>By character name, A to Z.</summary>
    NameAsc,

    /// <summary>By character name, Z to A.</summary>
    NameDesc,
}

/// <summary>
/// String round-trip + display helpers for <see cref="PilotSortOrder"/>.
/// </summary>
/// <remarks>
/// The canonical strings are the enum names verbatim (<c>"LastActiveNewest"</c>,
/// <c>"LastActiveOldest"</c>, <c>"NameAsc"</c>, <c>"NameDesc"</c>); they are the
/// values stored in the <c>pilot_sort_order</c> setting. This class is pure —
/// the Warning-on-unrecognised log lives in the settings repository, which uses
/// <see cref="TryParse"/> to detect a bad stored value.
/// </remarks>
public static class PilotSortOrderExtensions
{
    /// <summary>Convert a <see cref="PilotSortOrder"/> to its canonical stored string (the enum name).</summary>
    /// <param name="order">Enum value to format.</param>
    /// <returns>The canonical string.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="order"/> is not a defined enum value.</exception>
    public static string ToCanonicalString(PilotSortOrder order) => order switch
    {
        PilotSortOrder.LastActiveNewest => "LastActiveNewest",
        PilotSortOrder.LastActiveOldest => "LastActiveOldest",
        PilotSortOrder.NameAsc          => "NameAsc",
        PilotSortOrder.NameDesc         => "NameDesc",
        _ => throw new ArgumentOutOfRangeException(nameof(order), order, "Unknown PilotSortOrder enum value."),
    };

    /// <summary>
    /// Try to parse a canonical stored string into a <see cref="PilotSortOrder"/>.
    /// </summary>
    /// <param name="value">Stored value (one of the canonical strings), or null.</param>
    /// <param name="order">The matched order, or <see cref="PilotSortOrder.LastActiveNewest"/> (the default) when unmatched.</param>
    /// <returns><c>true</c> if <paramref name="value"/> matched a canonical string; otherwise <c>false</c>.</returns>
    public static bool TryParse(string? value, out PilotSortOrder order)
    {
        switch (value)
        {
            case "LastActiveNewest": order = PilotSortOrder.LastActiveNewest; return true;
            case "LastActiveOldest": order = PilotSortOrder.LastActiveOldest; return true;
            case "NameAsc":          order = PilotSortOrder.NameAsc;          return true;
            case "NameDesc":         order = PilotSortOrder.NameDesc;         return true;
            default:                 order = PilotSortOrder.LastActiveNewest; return false;
        }
    }

    /// <summary>
    /// Parse a canonical stored string, returning the default
    /// <see cref="PilotSortOrder.LastActiveNewest"/> for an unrecognised value
    /// (no log — callers that need the Warning use <see cref="TryParse"/>).
    /// </summary>
    /// <param name="value">Stored value, or null.</param>
    /// <returns>The matched order, or the default.</returns>
    public static PilotSortOrder Parse(string? value)
    {
        TryParse(value, out var order);
        return order;
    }

    /// <summary>Human-readable label for the sort dropdown (decision 1.6 = A).</summary>
    /// <param name="order">Enum value to label.</param>
    /// <returns>One of <c>"Last active (newest)"</c>, <c>"Last active (oldest)"</c>, <c>"Name (A-Z)"</c>, <c>"Name (Z-A)"</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="order"/> is not a defined enum value.</exception>
    public static string DisplayLabel(PilotSortOrder order) => order switch
    {
        PilotSortOrder.LastActiveNewest => "Last active (newest)",
        PilotSortOrder.LastActiveOldest => "Last active (oldest)",
        PilotSortOrder.NameAsc          => "Name (A-Z)",
        PilotSortOrder.NameDesc         => "Name (Z-A)",
        _ => throw new ArgumentOutOfRangeException(nameof(order), order, "Unknown PilotSortOrder enum value."),
    };
}
