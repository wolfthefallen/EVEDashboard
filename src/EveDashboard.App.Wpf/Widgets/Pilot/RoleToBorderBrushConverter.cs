namespace EveDashboard.App.Wpf.Widgets.Pilot;

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using EveDashboard.Core.Sessions;

/// <summary>
/// Maps a <see cref="PilotRole"/> to its widget border brush. Looks
/// up the brush by key in <see cref="Application.Current"/>'s merged resource
/// dictionaries (the active theme).
/// </summary>
/// <remarks>
/// <para>
/// Theme switch reactivity: the converter returns the brush instance that's
/// live at the moment the binding evaluates. When the theme dictionary swaps
/// (via <c>ThemeService.OnSettingChanged</c>), the binding's converter-output
/// is stale until the binding re-runs. <see cref="PerPilotWidgetViewModel"/>
/// (and the saved sibling) subscribe to
/// <see cref="EveDashboard.Core.Settings.ISettingsRepository.SettingChanged"/>
/// filtered by <c>"theme"</c> and re-raise <c>Role</c> PropertyChanged so the
/// converter re-resolves.
/// </para>
/// <para>
/// Brush key mapping (all keys verified present in
/// both <c>Themes/Light.xaml</c> and <c>Themes/Dark.xaml</c>):
/// </para>
/// <list type="bullet">
///   <item><description>Miner   -> <c>WidgetBorderBrush</c> (unchanged default chrome)</description></item>
///   <item><description>Booster -> <c>MintGreenBrush</c></description></item>
///   <item><description>Cyno    -> <c>LightBlueBrush</c></description></item>
///   <item><description>Combat  -> <c>CoralPinkBrush</c></description></item>
/// </list>
/// </remarks>
public sealed class RoleToBorderBrushConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not PilotRole role)
        {
            return DependencyProperty.UnsetValue;
        }

        var key = role switch
        {
            PilotRole.Miner   => "WidgetBorderBrush",
            PilotRole.Booster => "MintGreenBrush",
            PilotRole.Cyno    => "LightBlueBrush",
            PilotRole.Combat  => "CoralPinkBrush",
            _                 => "WidgetBorderBrush",
        };

        var app = Application.Current;
        if (app is null)
        {
            return DependencyProperty.UnsetValue;
        }

        return app.TryFindResource(key) as Brush ?? (object)DependencyProperty.UnsetValue;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException($"{nameof(RoleToBorderBrushConverter)} is one-way only.");
}
