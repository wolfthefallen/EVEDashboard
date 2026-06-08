namespace EveDashboard.Stats.Schema;

/// <summary>
/// Registered session-DB migrations, ordered by <see cref="Migration.TargetVersion"/>.
/// v1 ships an empty list — the fresh schema is version 1 by definition.
/// </summary>
internal static class Migrations
{
    /// <summary>Empty in v1; future schema-change plans append entries here.</summary>
    public static IReadOnlyList<Migration> All { get; } = Array.Empty<Migration>();
}
