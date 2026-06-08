namespace EveDashboard.App.Wpf.Bootstrap;

/// <summary>
/// Production <see cref="IBootstrapErrorReporter"/>. Delegates to the existing
/// static <see cref="BootstrapErrorReporter.ReportAndShutdown"/> helper.
/// </summary>
public sealed class BootstrapErrorReporterAdapter : IBootstrapErrorReporter
{
    /// <inheritdoc />
    public void ReportAndShutdown(string title, string body)
        => BootstrapErrorReporter.ReportAndShutdown(title, body);
}
