namespace EveDashboard.App.Wpf.Widgets.Pilot;

using System.Globalization;
using System.Windows.Data;
using EveDashboard.Core.Sessions;

/// <summary>
/// Plan 35: returns <c>true</c> iff the bound <see cref="PilotRole"/> matches
/// the string <c>ConverterParameter</c>. Used for the <c>IsChecked</c> binding
/// on each MenuItem in the per-pilot widget's right-click context menu.
/// </summary>
/// <remarks>
/// <para>
/// The XAML binding uses <c>Mode=OneWay</c> so user clicks on the MenuItem do
/// NOT try to two-way-write a boolean back through this converter — the
/// <c>SetRoleCommand</c> on the VM handles the actual state change.
/// <see cref="ConvertBack"/> therefore throws.
/// </para>
/// <para>
/// String comparison is case-sensitive against the enum's default formatter
/// (e.g. <c>PilotRole.Booster.ToString()</c> = <c>"Booster"</c>) to match the
/// canonical role strings used in <c>ConverterParameter</c>.
/// </para>
/// </remarks>
public sealed class RoleEqualsConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not PilotRole role || parameter is not string expected)
        {
            return false;
        }

        return string.Equals(role.ToString(), expected, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException(
            $"{nameof(RoleEqualsConverter)} is one-way only; bind IsChecked with Mode=OneWay and use SetRoleCommand for state changes.");
}
