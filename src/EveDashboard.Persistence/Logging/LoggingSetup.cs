using EveDashboard.Core.Logging;
using EveDashboard.Core.Storage;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace EveDashboard.Persistence.Logging;

/// <summary>
/// Bootstraps the application logging pipeline using Serilog as the underlying
/// provider for <see cref="Microsoft.Extensions.Logging"/>. Configures a console
/// sink and a rotating file sink, then assigns the resulting
/// <see cref="ILoggerFactory"/> to <see cref="LogProvider.Factory"/>.
/// </summary>
/// <remarks>
/// The no-arg <see cref="Configure(LogLevel)"/> overload resolves the
/// conventional per-user log directory <c>%APPDATA%\EveDashboard\logs</c>
/// (via <see cref="AppDataPaths.GetAppDataRoot"/>) and is the production
/// entry point. The explicit-directory overloads remain as test seams.
/// </remarks>
public static class LoggingSetup
{
    private const string DefaultOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {SourceContext,-50} {Level:u8} {Message:lj}{NewLine}{Exception}";

    private const long DefaultFileSizeLimitBytes = 10L * 1024 * 1024; // 10 MB
    private const int DefaultRetainedFileCount = 2;                   // current + 1 backup
    private const string LogFileName = "EveDashboard.log";

    /// <summary>
    /// Configure logging using the conventional per-user log directory
    /// <c>%APPDATA%\EveDashboard\logs</c>. Folder is created if missing.
    /// Production entry point — call this from <c>App.OnStartup</c>.
    /// </summary>
    /// <param name="minimumLevel">Minimum log level for both sinks. Default is Information.</param>
    /// <exception cref="System.IO.IOException">Thrown if the log directory cannot be created.</exception>
    public static void Configure(LogLevel minimumLevel = LogLevel.Information)
        => Configure(DefaultLogDirectory(), minimumLevel);

    /// <summary>
    /// Configure logging with the production-default rotation parameters
    /// (10 MB max file size, 1 backup retained). Test seam — production code
    /// uses the no-arg <see cref="Configure(LogLevel)"/> overload.
    /// </summary>
    /// <param name="logDirectory">Directory where the log file lives. Created if missing.</param>
    /// <param name="minimumLevel">Minimum log level for both sinks. Default is Information.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="logDirectory"/> is null.</exception>
    /// <exception cref="System.IO.IOException">Thrown if the log directory cannot be created.</exception>
    public static void Configure(string logDirectory, LogLevel minimumLevel = LogLevel.Information)
        => Configure(logDirectory, minimumLevel, DefaultFileSizeLimitBytes, DefaultRetainedFileCount);

    /// <summary>
    /// Configure logging with custom rotation parameters. Used by tests so file
    /// rotation can be exercised against a small <paramref name="fileSizeLimitBytes"/>
    /// rather than waiting for 10 MB to accumulate.
    /// </summary>
    /// <param name="logDirectory">Directory where the log file lives. Created if missing.</param>
    /// <param name="minimumLevel">Minimum log level for both sinks.</param>
    /// <param name="fileSizeLimitBytes">Max bytes per log file before rotation kicks in.</param>
    /// <param name="retainedFileCount">Total log files to retain (current + N backups).</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="logDirectory"/> is null.</exception>
    /// <exception cref="System.IO.IOException">Thrown if the log directory cannot be created.</exception>
    public static void Configure(
        string logDirectory,
        LogLevel minimumLevel,
        long fileSizeLimitBytes,
        int retainedFileCount)
    {
        ArgumentNullException.ThrowIfNull(logDirectory);
        Directory.CreateDirectory(logDirectory);
        var logFilePath = Path.Combine(logDirectory, LogFileName);

        var serilogLevel = ToSerilogLevel(minimumLevel);

        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Is(serilogLevel)
            .WriteTo.Console(outputTemplate: DefaultOutputTemplate)
            .WriteTo.File(
                path: logFilePath,
                outputTemplate: DefaultOutputTemplate,
                fileSizeLimitBytes: fileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: retainedFileCount,
                shared: false)
            .CreateLogger();

        // Bridge Serilog -> Microsoft.Extensions.Logging
        var msFactory = new SerilogLoggerFactory(serilogLogger, dispose: true);

        LogProvider.Factory = msFactory;
    }

    private static LogEventLevel ToSerilogLevel(LogLevel level) => level switch
    {
        LogLevel.Trace       => LogEventLevel.Verbose,
        LogLevel.Debug       => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning     => LogEventLevel.Warning,
        LogLevel.Error       => LogEventLevel.Error,
        LogLevel.Critical    => LogEventLevel.Fatal,
        LogLevel.None        => LogEventLevel.Fatal,
        _                    => LogEventLevel.Information,
    };

    /// <summary>
    /// Resolve the conventional default logs directory:
    /// <c>%APPDATA%\EveDashboard\logs</c>. <c>internal</c> so the test
    /// project can assert on the resolved path without invoking
    /// <see cref="Configure(LogLevel)"/> against the real <c>%APPDATA%</c>.
    /// </summary>
    /// <returns>Absolute path string.</returns>
    internal static string DefaultLogDirectory()
        => Path.Combine(AppDataPaths.GetAppDataRoot(), "logs");
}
