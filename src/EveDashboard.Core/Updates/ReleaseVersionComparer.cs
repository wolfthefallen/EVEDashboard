namespace EveDashboard.Core.Updates;

/// <summary>
/// Pure comparison between the running assembly version and a release's git tag.
/// No I/O — unit-tested in isolation.
/// </summary>
public static class ReleaseVersionComparer
{
    private static readonly char[] SuffixChars = { '-', '+' };

    /// <summary>
    /// Decide whether <paramref name="latestTag"/> names a release strictly newer
    /// than <paramref name="runningVersion"/>.
    /// </summary>
    /// <param name="runningVersion">The running build's assembly version (e.g. <c>0.1.0.0</c>).</param>
    /// <param name="latestTag">
    /// The release tag, e.g. <c>"v0.2.0"</c>. A leading <c>v</c>/<c>V</c> and any
    /// <c>-</c>/<c>+</c> SemVer suffix are tolerated.
    /// </param>
    /// <returns>
    /// <c>true</c> if the tag parses to a higher Major.Minor.Patch than the running
    /// version; <c>false</c> if it is equal, older, blank, or unparseable.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="runningVersion"/> is null.</exception>
    /// <remarks>
    /// Both sides are normalised to (Major, Minor, Patch) before comparison. This
    /// sidesteps the <see cref="Version"/> trap where <c>"0.1.0"</c> parses with
    /// <c>Revision == -1</c> while the entry-assembly version is <c>0.1.0.0</c>
    /// (<c>Revision == 0</c>), which would otherwise report an identical tag as
    /// older than the running build.
    /// </remarks>
    public static bool IsNewer(Version runningVersion, string latestTag)
    {
        ArgumentNullException.ThrowIfNull(runningVersion);
        if (string.IsNullOrWhiteSpace(latestTag))
        {
            return false;
        }

        var trimmed = latestTag.Trim();
        if (trimmed[0] is 'v' or 'V')
        {
            trimmed = trimmed[1..];
        }

        // Drop any SemVer pre-release / build-metadata suffix (defensive; full
        // releases use clean tags).
        var cut = trimmed.IndexOfAny(SuffixChars);
        if (cut >= 0)
        {
            trimmed = trimmed[..cut];
        }

        if (!Version.TryParse(trimmed, out var tagVersion))
        {
            return false;
        }

        return Normalize(tagVersion) > Normalize(runningVersion);
    }

    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build);
}
