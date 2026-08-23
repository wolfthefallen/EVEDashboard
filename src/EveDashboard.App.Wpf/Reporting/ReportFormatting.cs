namespace EveDashboard.App.Wpf.Reporting;

using System.Globalization;

/// <summary>
/// Shared display formatting for the Reports window. Extracted so the
/// compare view and the Lifetime Totals view format active durations
/// identically instead of each carrying its own copy.
/// </summary>
public static class ReportFormatting
{
    /// <summary>
    /// Format an active-duration <see cref="TimeSpan"/> as <c>HH:mm:ss</c> with
    /// hours unbounded (e.g. <c>123:04:05</c> for a 5-day-plus total). Invariant
    /// culture so the rendered digits are stable across locales.
    /// </summary>
    /// <param name="duration">Active (un-paused) duration to render.</param>
    /// <returns>The duration as <c>HH:mm:ss</c>, hours never wrapping at 24.</returns>
    public static string FormatActiveDuration(TimeSpan duration)
    {
        var totalHours = (long)duration.TotalHours;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:D2}:{1:D2}:{2:D2}",
            totalHours, duration.Minutes, duration.Seconds);
    }
}
