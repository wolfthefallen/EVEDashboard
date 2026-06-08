namespace EveDashboard.App.Wpf.Converters;

using System.Globalization;
using System.Windows;
using System.Windows.Data;

/// <summary>
/// Inverse of WPF's BCL <see cref="System.Windows.Controls.BooleanToVisibilityConverter"/>:
/// maps <c>true</c> to <see cref="Visibility.Collapsed"/> and <c>false</c> to
/// <see cref="Visibility.Visible"/>. Used by plan 22.2's Settings dialog to
/// hide the Display / Pricing / Markets sections when the dialog is opened in
/// bootstrap-failure mode (driven by
/// <c>SettingsDialogViewModel.IsBootstrapFailureMode</c>).
/// </summary>
/// <remarks>
/// <c>Visibility.Collapsed</c> (rather than <c>Hidden</c>) so the hidden
/// sections do not consume layout space — the dialog auto-sizes down to the
/// smaller "fix your gamelog path" form. <c>ConvertBack</c> is not supported;
/// the binding is one-way.
/// </remarks>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    /// <summary>Convert a boolean to inverse visibility.</summary>
    /// <param name="value">Boolean. Non-boolean values are treated as <c>false</c>.</param>
    /// <param name="targetType">Unused; binding target is <see cref="Visibility"/>.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="culture">Unused.</param>
    /// <returns><see cref="Visibility.Collapsed"/> if <paramref name="value"/> is <c>true</c>; <see cref="Visibility.Visible"/> otherwise.</returns>
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Not supported - the binding is one-way.</summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
