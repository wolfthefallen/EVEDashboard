namespace EveDashboard.App.Wpf.Converters;

using System.Globalization;
using System.Windows.Data;
using EveDashboard.Core.Sessions;

/// <summary>
/// Maps a <see cref="PilotSortOrder"/> to its human-readable dropdown label
/// via <see cref="PilotSortOrderExtensions.DisplayLabel"/>.
/// Used by the New Session dialog's sort ComboBox so the items read
/// "Last active (newest)" rather than the enum name.
/// </summary>
public sealed class PilotSortOrderLabelConverter : IValueConverter
{
    /// <summary>Convert a <see cref="PilotSortOrder"/> to its display label.</summary>
    /// <param name="value">The <see cref="PilotSortOrder"/> value.</param>
    /// <param name="targetType">Unused; binding target is a string.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="culture">Unused.</param>
    /// <returns>The dropdown label, or the value's <c>ToString()</c> if it is not a known order.</returns>
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is PilotSortOrder order
            ? PilotSortOrderExtensions.DisplayLabel(order)
            : value?.ToString() ?? string.Empty;

    /// <summary>Not supported — the binding is one-way (SelectedItem carries the enum directly).</summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
