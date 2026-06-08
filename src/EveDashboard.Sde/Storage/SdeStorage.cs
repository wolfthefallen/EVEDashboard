namespace EveDashboard.Sde.Storage;

using System.Reflection;
using EveDashboard.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="ISdeStorage"/> implementation: places the SDE under
/// <c>%APPDATA%\EveDashboard\sde\eve.sqlite</c> on Windows and
/// bootstraps from an embedded fallback resource on first launch.
/// </summary>
/// <remarks>
/// Path resolution composes against <see cref="AppDataPaths.GetAppDataRoot"/>
/// (single source of truth for the <c>%APPDATA%\EveDashboard</c> root).
/// Windows is the supported platform. <see cref="Environment.SpecialFolder.ApplicationData"/>
/// resolves to a reasonable per-user directory on Linux/macOS too, so the
/// type technically loads there, but cross-platform behaviour is best-effort
/// and not tested.
/// </remarks>
public sealed class SdeStorage : ISdeStorage
{
    /// <summary>
    /// Logical name of the embedded fallback resource. Must match the
    /// <c>&lt;LogicalName&gt;</c> in <c>EveDashboard.Sde.csproj</c>.
    /// </summary>
    private const string FallbackResourceName = "EveDashboard.Sde.Data.sde-fallback.sqlite";

    private readonly ILogger<SdeStorage> _log;

    /// <summary>
    /// Construct an <see cref="SdeStorage"/> using the conventional Windows
    /// path: <c>%APPDATA%\EveDashboard\sde\eve.sqlite</c>.
    /// </summary>
    /// <param name="log">Logger for bootstrap diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="log"/> is <c>null</c>.</exception>
    public SdeStorage(ILogger<SdeStorage> log) : this(log, DefaultDatabasePath()) { }

    /// <summary>
    /// Test-seam constructor — accepts an explicit database path. Used by
    /// the unit-test project (made visible via <c>InternalsVisibleTo</c>).
    /// </summary>
    /// <param name="log">Logger for bootstrap diagnostics.</param>
    /// <param name="databasePath">Absolute path of the runtime SDE database.</param>
    /// <exception cref="ArgumentNullException">Thrown if either argument is <c>null</c>.</exception>
    internal SdeStorage(ILogger<SdeStorage> log, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(databasePath);
        _log = log;
        DatabasePath = databasePath;
    }

    /// <inheritdoc />
    public string DatabasePath { get; }

    /// <inheritdoc />
    public async Task<bool> EnsureDatabaseExistsAsync(CancellationToken ct = default)
    {
        if (File.Exists(DatabasePath))
        {
            _log.LogDebug("SDE database already present at {Path}; no bootstrap needed", DatabasePath);
            return false;
        }

        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var asm = typeof(SdeStorage).Assembly;
        await using var src = asm.GetManifestResourceStream(FallbackResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded SDE fallback resource not found: '{FallbackResourceName}'. " +
                $"Verify <EmbeddedResource> in EveDashboard.Sde.csproj.");
        await using (var dst = File.Create(DatabasePath))
        {
            await src.CopyToAsync(dst, ct).ConfigureAwait(false);
        }

        _log.LogInformation(
            "Bootstrapped SDE database from embedded fallback ({SizeKb} KB) to {Path}",
            new FileInfo(DatabasePath).Length / 1024, DatabasePath);
        return true;
    }

    /// <summary>
    /// Resolve the conventional default path:
    /// <c>%APPDATA%\EveDashboard\sde\eve.sqlite</c>.
    /// </summary>
    /// <returns>Absolute path string.</returns>
    private static string DefaultDatabasePath()
        => Path.Combine(AppDataPaths.GetAppDataRoot(), "sde", "eve.sqlite");
}
