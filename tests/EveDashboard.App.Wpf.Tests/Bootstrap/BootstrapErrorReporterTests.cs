namespace EveDashboard.App.Wpf.Tests.Bootstrap;

using EveDashboard.App.Wpf.Bootstrap;
using FluentAssertions;

/// <summary>
/// Per plan 16 alignment 2.2 = B: only null-arg checks are automated; the
/// MessageBox + Application.Shutdown side effects are exercised via the
/// manual test doc (<c>tests/manual/manual-tests.md</c> Section 1, steps 2-7).
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
