namespace EveDashboard.App.Wpf.Converters;

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using EveDashboard.App.Wpf.Reporting;

/// <summary>
/// Maps a <see cref="BreakdownContentMode"/> to <see cref="Visibility"/> by
/// comparing against a converter-parameter string (<c>"Single"</c> or
/// <c>"Compare"</c>). Used by the Reports window to show one content subtree
/// per mode while keeping both in the visual tree (avoids re-templating on
/// every selection change).
/// </summary>
public sealed class BreakdownContentModeToVisibilityConverter : IValueConverter
{
    /// <summary>Convert a mode + target-mode-name to <see cref="Visibility"/>.</summary>
    /// <param name="value">The bound <see cref="BreakdownContentMode"/>.</param>
    /// <param name="targetType">Unused; binding target is <see cref="Visibility"/>.</param>
    /// <param name="parameter">Target-mode name as a string (<c>"Single"</c> or <c>"Compare"</c>). Comparison is case-insensitive.</param>
    /// <param name="culture">Unused.</param>
    /// <returns><see cref="Visibility.Visible"/> when the bound mode matches the parameter; <see cref="Visibility.Collapsed"/> otherwise.</returns>
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not BreakdownContentMode mode) return Visibility.Collapsed;
        if (parameter is not string target) return Visibility.Collapsed;
        var matches = string.Equals(mode.ToString(), target, StringComparison.OrdinalIgnoreCase);
        return matches ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Not supported — the binding is one-way.</summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
