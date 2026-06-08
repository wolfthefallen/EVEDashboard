namespace EveDashboard.Stats.Storage;

using EveDashboard.Core.Storage;
using EveDashboard.Stats.Schema;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="ISessionStore"/> implementation: places the session DB
/// under <c>%APPDATA%\EveDashboard\session.sqlite</c> on Windows.
/// </summary>
/// <remarks>
/// Path resolution composes against <see cref="AppDataPaths.GetAppDataRoot"/>
/// (single source of truth for the <c>%APPDATA%\EveDashboard</c> root).
/// Windows is the target platform. Cross-platform behaviour
/// (<c>Environment.GetFolderPath(SpecialFolder.ApplicationData)</c> returns
/// <c>~/.config</c>-style paths on Linux/macOS) is best-effort, untested.
/// </remarks>
public sealed class SessionStore : ISessionStore
{
    private readonly ILogger<SessionStore> _log;

    /// <summary>Default constructor — uses the conventional Windows path.</summary>
    /// <param name="log">Logger for lifecycle diagnostics.</param>
    public SessionStore(ILogger<SessionStore> log) : this(log, DefaultDatabasePath()) { }

    /// <summary>Test-seam constructor — pass an explicit path.</summary>
    /// <param name="log">Logger for lifecycle diagnostics.</param>
    /// <param name="databasePath">Explicit filesystem path for the session DB.</param>
    internal SessionStore(ILogger<SessionStore> log, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(databasePath);
        _log = log;
        DatabasePath = databasePath;
    }

    /// <inheritdoc />
    public string DatabasePath { get; }

    /// <inheritdoc />
    public async Task EnsureSchemaAsync(ISqliteDatabase target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        await SchemaInitializer.EnsureSchemaAsync(target, ct);

        _log.LogInformation("Session SQLite schema ensured at {Path}", DatabasePath);
    }

    private static string DefaultDatabasePath()
        => Path.Combine(AppDataPaths.GetAppDataRoot(), "session.sqlite");
}
