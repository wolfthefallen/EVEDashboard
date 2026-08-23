namespace EveDashboard.App.Wpf.Tests.Bootstrap;

using EveDashboard.App.Wpf.Bootstrap;
using FluentAssertions;

/// <summary>
/// Only null-arg checks are automated; the MessageBox + Application.Shutdown
/// side effects need a live WPF application, so they are exercised by manual
/// verification instead.
/// </summary>
public class BootstrapErrorReporterTests
{
    [Fact]
    public void ReportAndShutdown_throws_on_null_title()
    {
        Action act = () => BootstrapErrorReporter.ReportAndShutdown(null!, "body");
        act.Should().Throw<ArgumentNullException>().WithParameterName("title");
    }

    [Fact]
    public void ReportAndShutdown_throws_on_null_body()
    {
        Action act = () => BootstrapErrorReporter.ReportAndShutdown("title", null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("body");
    }
}
