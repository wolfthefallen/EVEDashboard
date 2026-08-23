namespace EveDashboard.App.Wpf.Widgets.Pilot;

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using EveDashboard.Core.Sessions;

/// <summary>
/// Maps a <see cref="PilotRole"/> to the widget border thickness.
/// Miner pilots keep the default <c>1</c>-pixel chrome; non-Miner roles get
/// <c>2</c> pixels so the role color reads clearly without dominating the
/// layout.
/// </summary>
public sealed class RoleToThicknessConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not PilotRole role)
        {
            return new Thickness(1);
        }

        return new Thickness(role == PilotRole.Miner ? 1 : 2);
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException($"{nameof(RoleToThicknessConverter)} is one-way only.");
}
