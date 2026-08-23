namespace EveDashboard.App.Wpf.Dialogs;

/// <summary>
/// Identifies which <c>App.OnStartup</c> branch fired the bootstrap-failure
/// re-route to the Settings dialog.
/// </summary>
/// <remarks>
/// The three values map one-to-one to the three call sites in
/// <c>App.RunBootstrapChainAsync</c> that previously called
/// <see cref="EveDashboard.App.Wpf.Bootstrap.BootstrapErrorReporter.ReportAndShutdown"/>
/// directly: a malformed <c>config.yaml</c>, a missing autodetected gamelog
/// folder, and a discovered-but-empty gamelog folder.
/// </remarks>
public enum BootstrapFailureKind
{
    /// <summary><c>config.yaml</c> failed to parse, was missing the <c>gamelog_path</c> key, or the value was empty.</summary>
    ConfigException,

    /// <summary><c>config.yaml</c> was missing AND the autodetected fallback path does not exist on disk.</summary>
    AutodetectFailed,

    /// <summary><c>gamelog_path</c> resolved cleanly but contained zero EVE gamelog files matching the expected pattern.</summary>
    ZeroCharacters,
}

/// <summary>
/// Context passed to <see cref="ISettingsDialog.Show"/> when the bootstrap
/// chain fails. Non-null = bootstrap-failure mode (the dialog hides every
/// section except <c>gamelog_path</c> and shows <see cref="Message"/> in a
/// banner). Null = normal-mode chrome-button entry.
/// </summary>
/// <param name="Kind">Which failure branch fired the re-route.</param>
/// <param name="Message">Human-readable explanation rendered in the dialog's banner row.</param>
public sealed record BootstrapFailureContext(BootstrapFailureKind Kind, string Message);
