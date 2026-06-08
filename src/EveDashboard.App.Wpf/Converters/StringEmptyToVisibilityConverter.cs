namespace EveDashboard.App.Wpf.Converters;

using System.Globalization;
using System.Windows;
using System.Windows.Data;

/// <summary>
/// Maps a string to <see cref="Visibility"/>: empty / null -> <see cref="Visibility.Collapsed"/>,
/// non-empty -> <see cref="Visibility.Visible"/>. Used by the Reports window's
/// status row so the row collapses (and does not reserve layout space) when
/// no status message is currently set.
/// </summary>
public sealed class StringEmptyToVisibilityConverter : IValueConverter
{
    /// <summary>Convert a string to visibility.</summary>
    /// <param name="value">String value to inspect. Non-string values are treated as empty.</param>
    /// <param name="targetType">Unused; binding target is <see cref="Visibility"/>.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="culture">Unused.</param>
    /// <returns><see cref="Visibility.Collapsed"/> if the value is null or empty; <see cref="Visibility.Visible"/> otherwise.</returns>
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Not supported — the binding is one-way.</summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
