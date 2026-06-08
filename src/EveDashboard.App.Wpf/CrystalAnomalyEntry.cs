namespace EveDashboard.App.Wpf;

/// <summary>
/// Plan 35: immutable descriptor for a single entry in the Crystal Diagram
/// window's anomaly dropdown. Pure data — no logic, no I/O.
/// </summary>
/// <param name="Id">Stable lowercase snake_case id (e.g. <c>"all"</c>, <c>"griemeer"</c>).
///     Persisted under the <c>crystal_diagram_selected_anomaly</c> setting key so it survives
///     display-label tweaks.</param>
/// <param name="DisplayName">Short human-readable label shown in the dropdown
///     (e.g. <c>"All / Full Chart"</c>, <c>"Griemeer (Isogen)"</c>).</param>
/// <param name="ImagePackUri">Absolute WPF pack URI to the image asset (e.g.
///     <c>"pack://application:,,,/Assets/anomaly_griemeer.png"</c>). Bound directly to
///     the <c>&lt;Image&gt;</c>'s <c>Source</c> in the window XAML.</param>
public sealed record CrystalAnomalyEntry(string Id, string DisplayName, string ImagePackUri);
