namespace EveDashboard.App.Wpf.Converters;

using System.Globalization;
using System.Windows.Data;

/// <summary>
/// Two-way <see cref="IValueConverter"/> that flips a boolean. Used by plan
/// 20's stat-config popup so the row's "Show" checkbox can two-way-bind to
/// the underlying <c>StatConfigRowViewModel.Hidden</c> flag (checkbox
/// checked = NOT hidden = visible).
/// </summary>
/// <remarks>
/// <c>Convert</c> and <c>ConvertBack</c> are symmetric; binding mode TwoWay
/// is the expected usage.
/// </remarks>
public sealed class InverseBooleanConverter : IValueConverter
{
    /// <summary>Flip a boolean. Non-boolean values are treated as <c>false</c>.</summary>
    /// <param name="value">Boolean to flip.</param>
    /// <param name="targetType">Unused.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="culture">Unused.</param>
    /// <returns>The negation of <paramref name="value"/>.</returns>
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    /// <summary>Inverse of <see cref="Convert"/> — same negation in the reverse direction.</summary>
    /// <param name="value">Boolean to flip.</param>
    /// <param name="targetType">Unused.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="culture">Unused.</param>
    /// <returns>The negation of <paramref name="value"/>.</returns>
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
