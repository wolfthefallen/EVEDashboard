namespace EveDashboard.Core.Settings;

/// <summary>
/// Fired by <see cref="ISettingsRepository.SettingChanged"/> after a
/// <c>Set*Async</c> call commits. Subscribers re-call the matching
/// <c>Get*Async</c> if they need the new value.
/// </summary>
public sealed class SettingChangedEventArgs : EventArgs
{
    /// <summary>The setting key that changed (e.g. <c>"theme"</c>).</summary>
    public string Key { get; }

    /// <summary>Construct event args for a setting key change.</summary>
    /// <param name="key">Non-null setting key.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="key"/> is null.</exception>
    public SettingChangedEventArgs(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Key = key;
    }
}
