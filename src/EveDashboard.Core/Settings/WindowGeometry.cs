namespace EveDashboard.Core.Settings;

/// <summary>
/// Persisted size, position, and maximised state of the main window.
/// Round-trips via <see cref="System.Text.Json.JsonSerializer"/> in
/// <c>setting.window_geometry</c>.
/// </summary>
/// <param name="Width">Window width in device-independent pixels (restored bounds).</param>
/// <param name="Height">Window height in device-independent pixels (restored bounds).</param>
/// <param name="Left">Window left in device-independent pixels.</param>
/// <param name="Top">Window top in device-independent pixels.</param>
/// <param name="IsMaximized">If true, the window was maximised at save time.</param>
public sealed record WindowGeometry(
    double Width,
    double Height,
    double Left,
    double Top,
    bool IsMaximized);
