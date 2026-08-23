namespace EveDashboard.Stats.Schema;

using EveDashboard.Core.Storage;

/// <summary>
/// One step in the session-DB schema-migration timeline. Applied by
/// <see cref="SchemaInitializer.EnsureSchemaAsync(ISqliteDatabase, CancellationToken)"/>
/// when the current <c>schema_version</c> in the <c>setting</c> table is less than
/// <see cref="TargetVersion"/>.
/// </summary>
/// <param name="TargetVersion">Schema version this migration produces. Bumped sequentially.</param>
/// <param name="Description">Short human-readable description; included in log lines.</param>
/// <param name="Up">
/// Forward migration. Receives the open SQLite wrapper and a cancellation token.
/// The delegate should depend only on its parameters — capturing surrounding
/// state risks subtle bugs when migrations are applied at app startup.
/// </param>
/// <remarks>
/// Migrations currently run without explicit transaction wrapping — every
/// registered migration so far is additive and idempotent, so a mid-migration
/// failure is safely re-runnable. If a destructive multi-statement migration
/// ever lands, transaction-per-migration is the expected shape.
/// </remarks>
public sealed record Migration(
    int TargetVersion,
    string Description,
    Func<ISqliteDatabase, CancellationToken, Task> Up);
