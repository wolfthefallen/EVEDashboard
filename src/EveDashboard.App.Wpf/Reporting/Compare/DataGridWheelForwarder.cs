namespace EveDashboard.App.Wpf.Reporting.Compare;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

/// <summary>
/// Static helper that forwards mouse-wheel events from a DataGrid's internal
/// ScrollViewer to the parent ScrollViewer when the inner viewer is at its
/// scroll boundary in the wheel direction or has nothing scrollable. Used by
/// every Reports widget that hosts a DataGrid (single-mode PerOreBreakdownView
/// and all three compare views) so wheel-scrolling the Reports content stays
/// smooth when the cursor passes over a DataGrid.
/// </summary>
public static class DataGridWheelForwarder
{
    /// <summary>
    /// Inspect the wheel event; if the inner DataGrid's ScrollViewer cannot
    /// consume it (at boundary in wheel direction, or no scrollable content),
    /// mark the event handled and bubble a fresh MouseWheelEvent to the
    /// DataGrid's parent for the outer ScrollViewer to pick up.
    /// </summary>
    /// <param name="sender">The DataGrid that raised PreviewMouseWheel.</param>
    /// <param name="e">The wheel event args.</param>
    public static void Forward(object sender, MouseWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Handled) return;
        if (sender is not DataGrid dg) return;

        var sv = FindVisualChild<ScrollViewer>(dg);
        if (sv is null) return;

        var atTop = sv.VerticalOffset <= 0;
        var atBottom = sv.VerticalOffset >= sv.ScrollableHeight;
        var nothingToScroll = sv.ScrollableHeight <= 0;

        if (nothingToScroll || (e.Delta > 0 && atTop) || (e.Delta < 0 && atBottom))
        {
            e.Handled = true;
            var bubbled = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source      = dg,
            };
            (VisualTreeHelper.GetParent(dg) as UIElement)?.RaiseEvent(bubbled);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var nested = FindVisualChild<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }
}
