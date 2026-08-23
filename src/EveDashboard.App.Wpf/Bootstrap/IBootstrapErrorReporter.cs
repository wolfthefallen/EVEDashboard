namespace EveDashboard.App.Wpf.Bootstrap;

/// <summary>
/// Test seam over <see cref="BootstrapErrorReporter.ReportAndShutdown"/>.
/// Production wires <see cref="BootstrapErrorReporterAdapter"/>; unit tests
/// substitute a fake that records the call so assertions can fire without
/// taking the WPF process down.
/// </summary>
/// <remarks>
/// The static helper stays for production use; the interface exists so the
/// second-consecutive <c>SaveAsync</c> failure path in
/// <c>SettingsDialogViewModel</c> is unit-testable. Mirrors the
/// <c>IConfirmDialog</c> / <c>ISettingsDialog</c> /
/// <c>IAddStationDialog</c> precedent for UI-boundary test seams.
/// </remarks>
public interface IBootstrapErrorReporter
{
    /// <summary>
    /// Show <paramref name="body"/> in an OK-button error dialog titled
    /// <paramref name="title"/>, then shut the application down with exit
    /// code <c>1</c>.
    /// </summary>
    /// <param name="title">Short dialog title.</param>
    /// <param name="body">Multi-line dialog body.</param>
    void ReportAndShutdown(string title, string body);
}
