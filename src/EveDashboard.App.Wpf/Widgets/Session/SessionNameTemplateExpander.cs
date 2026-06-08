namespace EveDashboard.App.Wpf.Widgets.Session;

using System.Globalization;
using EveDashboard.Core.Domain;

/// <summary>
/// Expands template variables in a session-name string into concrete values.
/// Pure: no clock dependency, no I/O, no logger. Per plan 16 decision 16.R
/// and <c>MEMORY.md -&gt; project_session_model</c>: exact-string-match
/// replacement of four tokens; no nesting; unknown tokens left as literal
/// text for forward-compat.
/// </summary>
public static class SessionNameTemplateExpander
{
    /// <summary>
    /// Expand template variables into a concrete session name.
    /// </summary>
    /// <param name="template">Raw user input. May contain zero or more variables.</param>
    /// <param name="selectedPilots">Pilots toggled on at Start time. Used by <c>{{$CHARS}}</c>.</param>
    /// <param name="asOf">Time at which <c>{{$DATE}}</c> / <c>{{$TIME}}</c> / <c>{{$DATETIME}}</c> resolve.</param>
    /// <returns>Expanded string. Null/empty inputs round-trip unchanged.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="selectedPilots"/> is null.</exception>
    public static string Expand(
        string template,
        IReadOnlyList<Character> selectedPilots,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(selectedPilots);
        if (string.IsNullOrEmpty(template)) return template ?? string.Empty;

        return template
            .Replace("{{$DATETIME}}", asOf.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{{$DATE}}",     asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),       StringComparison.Ordinal)
            .Replace("{{$TIME}}",     asOf.ToString("HH:mm", CultureInfo.InvariantCulture),            StringComparison.Ordinal)
            .Replace("{{$CHARS}}",    string.Join(", ", selectedPilots.Select(p => p.Name)),            StringComparison.Ordinal);
    }
}
