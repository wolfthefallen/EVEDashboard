namespace EveDashboard.Core.Settings;

/// <summary>
/// Visual theme applied to the WPF shell. Persisted to <c>setting.theme</c>
/// as the lowercase string <c>"light"</c> or <c>"dark"</c>.
/// </summary>
public enum ThemeMode
{
    /// <summary>Light theme.</summary>
    Light,

    /// <summary>Dark theme. Default for v1.</summary>
    Dark,
}
