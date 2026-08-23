namespace EveDashboard.App.Wpf.Widgets.Session;

using System.Globalization;
using System.Windows;
using System.Windows.Data;

/// <summary>
/// Maps <see cref="SessionWidgetMode"/> to <see cref="Visibility"/> by
/// membership in a <c>|</c>-separated mode list supplied as the converter
/// parameter.
/// </summary>
/// <remarks>
/// Single-mode parameters keep working: <c>ConverterParameter=Config</c>
/// matches only Config; <c>ConverterParameter=Running|Paused</c> matches
/// Running OR Paused. The <c>|</c>-separated form exists so a stat
/// block can be visible in multiple modes from a single XAML declaration
/// without duplicating the layout. The chrome-bar's <c>Stop</c>
/// button uses <c>ConverterParameter=Running|Paused</c>.
/// </remarks>
public sealed class SessionModeToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SessionWidgetMode mode || parameter is not string targetParam)
        {
            return Visibility.Collapsed;
        }
        var modeName = mode.ToString();
        foreach (var target in targetParam.Split('|'))
        {
            if (modeName == target) return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
