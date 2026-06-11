namespace EveDashboard.App.Wpf.Services;

using EveDashboard.App.Wpf.Dialogs;
using EveDashboard.Core.Updates;
using Microsoft.Extensions.Logging;

/// <summary>
/// Orchestrates the launch-time update check: asks the <see cref="IReleaseClient"/>
/// for the latest release, compares it to the running version, and — if newer —
/// prompts the user (Yes/No) to open the download page in their browser.
/// </summary>
/// <remarks>
/// WPF-free (depends only on seams) so it is unit-testable. Invoked once at
/// startup as a fire-and-forget task AFTER the main window is shown; it swallows
/// and logs all failures so it can never block or crash startup.
/// </remarks>
public sealed class UpdateNotifier
{
    private readonly IReleaseClient _client;
    private readonly IConfirmDialog _confirm;
    private readonly IBrowserLauncher _browser;
    private readonly ILogger<UpdateNotifier> _log;

    /// <summary>Construct the notifier.</summary>
    /// <param name="client">Release-info source.</param>
    /// <param name="confirm">Modal confirm-dialog seam.</param>
    /// <param name="browser">Default-browser launcher seam.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public UpdateNotifier(
        IReleaseClient client,
        IConfirmDialog confirm,
        IBrowserLauncher browser,
        ILogger<UpdateNotifier> log)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(log);
        _client = client;
        _confirm = confirm;
        _browser = browser;
        _log = log;
    }

    /// <summary>
    /// If a newer release exists, prompt the user and open the download page on
    /// confirmation. Never throws — all failures are logged and swallowed.
    /// </summary>
    /// <param name="runningVersion">The running build's assembly version.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// The release fetch is awaited on the caller's synchronization context (do
    /// NOT invoke from a non-UI thread): the confirm dialog must be shown on the
    /// UI thread.
    /// </remarks>
    public async Task NotifyIfUpdateAvailableAsync(Version runningVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runningVersion);
        try
        {
            var release = await _client.GetLatestReleaseAsync(ct);
            if (release is null)
            {
                return;
            }

            if (!ReleaseVersionComparer.IsNewer(runningVersion, release.TagName))
            {
                _log.LogInformation(
                    "Update check: running {Running} is current with latest release {Latest}.",
                    runningVersion.ToString(3), release.TagName);
                return;
            }

            _log.LogInformation(
                "Update check: newer release {Latest} available (running {Running}); prompting user.",
                release.TagName, runningVersion.ToString(3));

            var openDownload = _confirm.Show(
                title:        "Update available",
                body:         $"A new version ({release.TagName}) is available. " +
                              $"You are running {runningVersion.ToString(3)}. Open the download page?",
                confirmLabel: "Yes",
                cancelLabel:  "No");

            if (openDownload)
            {
                _browser.Open(release.ReleaseUrl);
            }
        }
        catch (Exception ex)
        {
            // Fire-and-forget safety: the update check must never disrupt the app.
            _log.LogWarning(ex, "Update check failed unexpectedly; skipping the update prompt.");
        }
    }
}
