using EveDashboard.Core.Logging;
using EveDashboard.Persistence.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EveDashboard.Persistence.Tests.Logging;

public class LoggingSetupTests : IDisposable
{
    private readonly string _testLogDir;

    public LoggingSetupTests()
    {
        _testLogDir = Path.Combine(
            Path.GetTempPath(),
            $"eve-mining-dashboard-test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        // Reset the static factory so other tests start clean.
        LogProvider.Factory = NullLoggerFactory.Instance;

        // Clean up the test log directory.
        if (Directory.Exists(_testLogDir))
        {
            Directory.Delete(_testLogDir, recursive: true);
        }
    }

    [Fact]
    public void Configure_AssignsRealFactoryToLogProvider()
    {
        LogProvider.Factory.Should().BeOfType<NullLoggerFactory>(
            "before Configure runs, the default NullLoggerFactory should be in place");

        LoggingSetup.Configure(_testLogDir);

        LogProvider.Factory.Should().NotBeOfType<NullLoggerFactory>(
            "after Configure runs, a real factory should be installed");
    }

    [Fact]
    public void DefaultLogDirectory_resolves_under_appdata_eve_mining_dashboard_logs()
    {
        // Assertion only — do NOT call Configure(), which would write to the
        // real %APPDATA%. The internal resolver is exposed via InternalsVisibleTo.
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EveDashboard",
            "logs");

        LoggingSetup.DefaultLogDirectory().Should().Be(expected);
    }

    [Fact]
    public void Configure_LoggedMessageReachesFile()
    {
        LoggingSetup.Configure(_testLogDir, LogLevel.Information);

        var logger = LogProvider.Factory.CreateLogger<LoggingSetupTests>();
        const string marker = "smoke-test-marker-message-12345";
        logger.LogInformation(marker);

        // Serilog flushes async by default. Force a clean dispose so the file
        // is fully written before we read it.
        if (LogProvider.Factory is IDisposable disposable)
        {
            disposable.Dispose();
        }
        LogProvider.Factory = NullLoggerFactory.Instance;

        var logFile = Path.Combine(_testLogDir, "EveDashboard.log");
        File.Exists(logFile).Should().BeTrue("the log file should be created on first write");

        var contents = File.ReadAllText(logFile);
        contents.Should().Contain(marker, "the marker message should appear in the log file");
        contents.Should().Contain("EveDashboard.Persistence.Tests.Logging.LoggingSetupTests",
            "the SourceContext (logger category) should appear in the formatted line");
    }
}
