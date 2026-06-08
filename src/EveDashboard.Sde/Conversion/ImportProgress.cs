namespace EveDashboard.Sde.Conversion;

/// <summary>
/// Progress signal raised periodically during an SDE import.
/// </summary>
/// <param name="CurrentTable">
/// Logical table currently being imported. One of:
/// <c>"meta"</c>, <c>"categories"</c>, <c>"groups"</c>, <c>"types"</c>,
/// <c>"type_materials"</c>.
/// </param>
/// <param name="RowsThisTable">
/// Rows imported into <paramref name="CurrentTable"/> since work on
/// that table started.
/// </param>
/// <param name="RowsTotal">
/// Rows imported across all tables since the import started.
/// Strictly increases for the duration of an import.
/// </param>
public sealed record ImportProgress(string CurrentTable, long RowsThisTable, long RowsTotal);
