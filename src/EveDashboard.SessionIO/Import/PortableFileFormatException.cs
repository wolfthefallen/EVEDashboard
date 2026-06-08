namespace EveDashboard.SessionIO.Import;

/// <summary>
/// Thrown when an import file's <c>_manifest.format</c> field is missing or is
/// not the expected literal <c>"EveDashboardSessionExport"</c>. Surfaces "this is
/// not one of our exports" before any SQL runs.
/// </summary>
public sealed class PortableFileFormatException : Exception
{
    /// <summary>Construct with a description of what was wrong.</summary>
    /// <param name="message">Human-readable explanation surfaced to the user.</param>
    public PortableFileFormatException(string message) : base(message) { }
}
