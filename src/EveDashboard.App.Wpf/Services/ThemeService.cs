namespace EveDashboard.App.Wpf.Services;

using System.Windows;
using EveDashboard.Core.Settings;
using Microsoft.Extensions.Logging;

/// <summary>
/// Loads the right WPF <see cref="ResourceDictionary"/> for the current theme
/// at app startup, and (for plan 17's Settings UI) re-applies the dictionary
/// when the persisted theme value changes.
/// </summary>
/// <remarks>
/// <para>
/// Per plan 16 decisions 2.2 = A and 16.U: the WPF view tree references all
/// theme-derived brushes via <c>{DynamicResource ...}</c>, which means
/// re-merging a different <c>ResourceDictionary</c> at runtime is enough to
/// retheme every visible control with no restart. Plan 16 ships with no UI
/// to fire the swap; the subscription is dormant until plan 17's Settings UI
/// calls <see cref="ISettingsRepository.SetThemeAsync"/>.
/// </para>
/// <para>
/// The service is idempotent — applying the same theme twice in a row is a
/// no-op for the merged dictionary collection.
/// </para>
/// </remarks>
public sealed class ThemeService
{
    private readonly ISettingsRepository _settings;
    private readonly ILogger<ThemeService> _log;

    /// <summary>Construct the service.</summary>
    /// <param name="settings">Settings repository — read for the current theme + subscribed to for live swap.</param>
    /// <param name="log">Logger for theme-load diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public ThemeService(ISettingsRepository settings, ILogger<ThemeService> log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);
        _settings = settings;
        _log = log;
    }

    /// <summary>
    /// Read the persisted theme and merge the matching <c>ResourceDictionary</c>
    /// into <see cref="Application.Resources"/>. Subscribes to
    /// <see cref="ISettingsRepository.SettingChanged"/> so future
    /// <c>SetThemeAsync</c> calls reapply the dictionary on the UI thread.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadInitialAsync(CancellationToken ct = default)
    {
        var theme = await _settings.GetThemeAsync(ct).ConfigureAwait(false);
        ApplyResourceDictionary(theme);
        _settings.SettingChanged += OnSettingChanged;
        _log.LogInformation("ThemeService loaded initial theme: {Theme}", theme);
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (!string.Equals(e.Key, "theme", StringComparison.Ordinal)) return;

        var app = Application.Current;
        if (app is null) return;

        // Marshal to UI thread; SetThemeAsync may have completed off the UI
        // thread (tests, background flows) and ResourceDictionary mutation
        // requires the dispatcher.
        _ = app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var theme = await _settings.GetThemeAsync().ConfigureAwait(true);
                ApplyResourceDictionary(theme);
                _log.LogInformation("ThemeService re-applied theme on SettingChanged: {Theme}", theme);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "ThemeService failed to re-apply theme on SettingChanged.");
            }
        });
    }

    private static void ApplyResourceDictionary(ThemeMode theme)
    {
        var app = Application.Current;
        if (app is null) return;

        var uri = theme switch
        {
            ThemeMode.Light => new Uri("/Themes/Light.xaml", UriKind.Relative),
            ThemeMode.Dark  => new Uri("/Themes/Dark.xaml",  UriKind.Relative),
            _               => throw new ArgumentOutOfRangeException(nameof(theme), theme, "Unknown theme."),
        };

        var dict = new ResourceDictionary { Source = uri };
        var merged = app.Resources.MergedDictionaries;

        // Replace any prior theme dictionary by Source. Idempotent — re-applying
        // the same theme removes the old entry and adds the new one.
        // Bug 2 fix (2026-05-03): match Light.xaml / Dark.xaml LITERALLY, not
        // any path under /Themes/. Plan 22.1 introduced Themes/Controls.xaml
        // (the global Button + TextBox style sheet) which the previous
        // /Themes/ substring predicate accidentally removed on every Apply,
        // taking the global Button style out of scope and reverting all
        // buttons to OS Aero defaults.
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            var src = merged[i].Source;
            if (src is null) continue;
            var path = src.OriginalString;
            if (path.EndsWith("/Themes/Light.xaml", StringComparison.Ordinal) ||
                path.EndsWith("/Themes/Dark.xaml", StringComparison.Ordinal))
            {
                merged.RemoveAt(i);
            }
        }
        merged.Add(dict);
    }
}
