namespace EveDashboard.App.Wpf.About;

using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.Sde.Meta;
using Microsoft.Extensions.Logging;

/// <summary>
/// Plan 52 VM for <see cref="AboutWindow"/>. Exposes the app version line, the
/// GitHub repository URL (for the clickable hyperlink), and the SDE snapshot
/// date. The thank-you lines and CCP legal paragraph are static text in the
/// XAML, so the VM stays minimal.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="VersionText"/> is read from the entry assembly's version (the
/// <c>Directory.Build.props</c> <c>&lt;Version&gt;</c> applied at build time),
/// matching how the launch update check reads the running version. In a unit-test
/// host the entry assembly is the test runner, so <see cref="VersionText"/> is
/// covered by the manual test, not a unit test.
/// </para>
/// <para>
/// <see cref="InitializeAsync"/> is called once from
/// <see cref="AboutWindow.OnLoaded"/>; a missing/unparseable SDE build time
/// leaves <see cref="SdeDataText"/> at its "unknown" default rather than throwing.
/// </para>
/// </remarks>
public sealed partial class AboutWindowViewModel : ObservableObject
{
    private const string UnknownSdeText = "SDE data: unknown";

    private readonly ISdeMetaReader _sdeMeta;
    private readonly ILogger<AboutWindowViewModel> _log;

    /// <summary>
    /// "EVE Dashboard v&lt;version&gt;" — the running app version to three parts
    /// (e.g. <c>0.1.3</c>), or <c>"unknown"</c> when the entry assembly version
    /// cannot be read.
    /// </summary>
    public string VersionText { get; }

    /// <summary>The public GitHub repository URL, shown as a clickable hyperlink.</summary>
    public string RepositoryUrl => "https://github.com/wolfthefallen/EVEDashboard";

    /// <summary>
    /// "SDE data: yyyy-MM-dd" from the SDE snapshot build time, or
    /// "SDE data: unknown" before <see cref="InitializeAsync"/> runs or when the
    /// build time is missing/unparseable.
    /// </summary>
    [ObservableProperty]
    private string _sdeDataText = UnknownSdeText;

    /// <summary>Construct the VM.</summary>
    /// <param name="sdeMeta">Reader for the SDE snapshot build time.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public AboutWindowViewModel(
        ISdeMetaReader sdeMeta,
        ILogger<AboutWindowViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(sdeMeta);
        ArgumentNullException.ThrowIfNull(log);
        _sdeMeta = sdeMeta;
        _log = log;

        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3);
        VersionText = "EVE Dashboard v" + (version ?? "unknown");
    }

    /// <summary>
    /// Read the SDE snapshot build time and format <see cref="SdeDataText"/>.
    /// Called once from <see cref="AboutWindow.OnLoaded"/>. A missing/unparseable
    /// build time (reader returns <c>null</c>) or a read failure leaves the
    /// "unknown" default in place — the About window never fails over SDE state.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            var buildTime = await _sdeMeta.GetBuildTimeAsync(ct).ConfigureAwait(true);
            SdeDataText = buildTime is null
                ? UnknownSdeText
                : "SDE data: " + buildTime.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "About VM: failed to read SDE build time; showing 'unknown'.");
        }
    }
}
