namespace EveDashboard.Core.Updates;

/// <summary>
/// The published release the app compares itself against, reduced to the two
/// fields the launch update check needs.
/// </summary>
/// <param name="TagName">
/// The release's git tag, e.g. <c>"v0.2.0"</c>. Compared against the running
/// assembly version by <see cref="ReleaseVersionComparer"/>.
/// </param>
/// <param name="ReleaseUrl">
/// The release's human-facing page (GitHub <c>html_url</c>), opened in the
/// user's browser when they accept the update prompt.
/// </param>
public sealed record ReleaseInfo(string TagName, string ReleaseUrl);
