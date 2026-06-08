namespace EveDashboard.App.Wpf.Tests.Helpers;

using EveDashboard.App.Wpf.Bootstrap;

/// <summary>
/// Hand-rolled fake of <see cref="IBootstrapErrorReporter"/>. Records every
/// call so tests can assert the second-consecutive-SaveAsync-failure fallback
/// fired with the right title and body, instead of taking down the WPF
/// process via <c>Application.Current.Shutdown(1)</c>.
/// </summary>
internal sealed class FakeBootstrapErrorReporter : IBootstrapErrorReporter
{
    public int CallCount { get; private set; }
    public string? LastTitle { get; private set; }
    public string? LastBody { get; private set; }

    public void ReportAndShutdown(string title, string body)
    {
        CallCount++;
        LastTitle = title;
        LastBody = body;
    }
}
