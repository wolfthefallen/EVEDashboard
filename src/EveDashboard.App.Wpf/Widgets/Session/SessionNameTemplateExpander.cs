namespace EveDashboard.App.Wpf.Widgets.Session;

using System.Globalization;
using EveDashboard.Core.Domain;

/// <summary>
/// Expands template variables in a session-name string into concrete values.
/// Pure: no clock dependency, no I/O, no logger. Exact-string-match
/// replacement of seven tokens; no nesting; unknown tokens left as literal
/// text for forward-compat. <c>{{$STARTDATE}}</c> / <c>{{$ENDDATE}}</c> render as
/// day + month abbreviation (<c>16Aug</c>) and <c>{{$YYYY}}</c> as the four-digit
/// year, so a window reads naturally as <c>16Aug - 19Aug 2026</c>.
/// </summary>
public static class SessionNameTemplateExpander
{
    /// <summary>
    /// Expand template variables into a concrete session name.
    /// </summary>
    /// <param name="template">Raw user input. May contain zero or more variables.</param>
    /// <param name="selectedPilots">Pilots toggled on at Start time. Used by <c>{{$CHARS}}</c>.</param>
    /// <param name="asOf">Time at which <c>{{$DATE}}</c> / <c>{{$TIME}}</c> / <c>{{$DATETIME}}</c> resolve.</param>
    /// <param name="windowStart">Start of the session's window, for <c>{{$STARTDATE}}</c> and <c>{{$YYYY}}</c>. Defaults to <paramref name="asOf"/> (a live session starts now).</param>
    /// <param name="windowStop">End of the session's window, for <c>{{$ENDDATE}}</c>. Defaults to <paramref name="asOf"/> (a live session has no known end yet).</param>
    /// <returns>Expanded string. Null/empty inputs round-trip unchanged.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="selectedPilots"/> is null.</exception>
    public static string Expand(
        string template,
        IReadOnlyList<Character> selectedPilots,
        DateTimeOffset asOf,
        DateTimeOffset? windowStart = null,
        DateTimeOffset? windowStop = null)
    {
        ArgumentNullException.ThrowIfNull(selectedPilots);
        if (string.IsNullOrEmpty(template)) return template ?? string.Empty;

        var start = windowStart ?? asOf;
        var stop = windowStop ?? asOf;
        return template
            .Replace("{{$DATETIME}}",  asOf.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{{$DATE}}",      asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),       StringComparison.Ordinal)
            .Replace("{{$TIME}}",      asOf.ToString("HH:mm", CultureInfo.InvariantCulture),            StringComparison.Ordinal)
            .Replace("{{$STARTDATE}}", start.ToString("dMMM", CultureInfo.InvariantCulture),            StringComparison.Ordinal)
            .Replace("{{$ENDDATE}}",   stop.ToString("dMMM", CultureInfo.InvariantCulture),             StringComparison.Ordinal)
            .Replace("{{$YYYY}}",      start.ToString("yyyy", CultureInfo.InvariantCulture),            StringComparison.Ordinal)
            .Replace("{{$CHARS}}",     string.Join(", ", selectedPilots.Select(p => p.Name)),            StringComparison.Ordinal);
    }
}
