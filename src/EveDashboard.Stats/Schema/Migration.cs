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
/// v1 ships zero migrations; the framework is plumbing for v2+. When the first
/// real migration lands, the plan that introduces it will revisit transaction
/// wrapping (deferred per plan 11 Q4.2 = C; expected to land on transaction-per-migration).
/// </remarks>
public sealed record Migration(
    int TargetVersion,
    string Description,
    Func<ISqliteDatabase, CancellationToken, Task> Up);
