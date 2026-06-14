namespace EveDashboard.App.Wpf.Converters;

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using EveDashboard.Core.Settings.WidgetStats;

/// <summary>
/// Plan 47: maps a <see cref="StatEmphasis"/> to a font size (points). Drives the
/// Reports table widgets' label sizing (via the shared <c>ReportsEmphasisLabel</c>
/// style) and each table <c>DataGrid.FontSize</c> (size only, per decision 2.2 = B).
/// Sizes match the active-session emphasis levels (canonical values in
/// <c>Themes/StatRowTemplates.xaml</c>): Header0-&gt;14, Header1-&gt;16,
/// Header2-&gt;20, Hero-&gt;22. <see cref="StatEmphasis.None"/> and any unknown value
/// return <see cref="DependencyProperty.UnsetValue"/> so the target keeps its
/// inherited size (the plain row look). One-way.
/// </summary>
public sealed class StatEmphasisToFontSizeConverter : IValueConverter
{
    /// <summary>Convert an emphasis level to a font size, or <see cref="DependencyProperty.UnsetValue"/> to inherit.</summary>
    /// <param name="value"><see cref="StatEmphasis"/>; any other value inherits (returns <see cref="DependencyProperty.UnsetValue"/>).</param>
    /// <param name="targetType">Unused; binding target is a font size (<see cref="double"/>).</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="culture">Unused.</param>
    /// <returns>The mapped font size for Header0/1/2/Hero; <see cref="DependencyProperty.UnsetValue"/> for None / non-emphasis values.</returns>
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            StatEmphasis.Header0 => 14.0,
            StatEmphasis.Header1 => 16.0,
            StatEmphasis.Header2 => 20.0,
            StatEmphasis.Hero => 22.0,
            _ => DependencyProperty.UnsetValue,
        };

    /// <summary>Not supported - the binding is one-way.</summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Plan 47: maps a <see cref="StatEmphasis"/> to a <see cref="FontWeight"/> for
/// Reports table-widget labels (via the shared <c>ReportsEmphasisLabel</c> style),
/// matching the active-session emphasis rows: Header0/1/2 are Bold; None and Hero
/// are Normal (Hero leans on size + accent colour, not weight). One-way.
/// </summary>
public sealed class StatEmphasisToFontWeightConverter : IValueConverter
{
    /// <summary>Convert an emphasis level to a font weight.</summary>
    /// <param name="value"><see cref="StatEmphasis"/>; any other value is treated as Normal.</param>
    /// <param name="targetType">Unused; binding target is <see cref="FontWeight"/>.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="culture">Unused.</param>
    /// <returns><see cref="FontWeights.Bold"/> for Header0/1/2; <see cref="FontWeights.Normal"/> otherwise.</returns>
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            StatEmphasis.Header0 or StatEmphasis.Header1 or StatEmphasis.Header2 => FontWeights.Bold,
            _ => FontWeights.Normal,
        };

    /// <summary>Not supported - the binding is one-way.</summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
