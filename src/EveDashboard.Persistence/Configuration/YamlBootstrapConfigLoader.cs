using EveDashboard.Core.Configuration;
using EveDashboard.Core.Storage;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EveDashboard.Persistence.Configuration;

/// <summary>
/// YAML-backed implementation of <see cref="IBootstrapConfigLoader"/>.
/// Reads <c>%APPDATA%\EveDashboard\config.yaml</c> at startup and
/// writes it via temp-file + atomic rename when the user updates settings.
/// </summary>
/// <remarks>
/// <para>
/// The loader's job is intentionally narrow. It reads the YAML, expands
/// environment variables in the <c>gamelog_path</c> value, and returns a
/// <see cref="BootstrapConfigResult"/>. It does NOT validate that the
/// returned path exists on disk; downstream consumers
/// (<c>EveDashboard.Mining.Gamelogs.GamelogDiscoveryService</c> or the WPF
/// app's startup sequence) handle that and surface a popup if the path is missing.
/// </para>
/// <para>
/// Single-threaded usage assumed: <see cref="LoadAsync"/> runs once at
/// app startup; <see cref="SaveAsync"/> runs on the UI thread.
/// </para>
/// </remarks>
public sealed class YamlBootstrapConfigLoader : IBootstrapConfigLoader
{
    private readonly string _configFilePath;
    private readonly string _autodetectGamelogPath;
    private readonly ILogger<YamlBootstrapConfigLoader> _logger;

    /// <summary>
    /// Construct the production loader. The config file path is resolved
    /// to <c>%APPDATA%\EveDashboard\config.yaml</c> via
    /// <see cref="AppDataPaths.GetAppDataRoot"/> (the single source of truth for
    /// the app-data root), and the autodetect gamelog path is resolved to
    /// <c>&lt;Documents&gt;\EVE\logs\Gamelogs</c> (where <c>&lt;Documents&gt;</c>
    /// follows any redirected-Documents configuration).
    /// </summary>
    /// <param name="logger">Logger for diagnostic output. Required.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="logger"/> is null.</exception>
    public YamlBootstrapConfigLoader(ILogger<YamlBootstrapConfigLoader> logger)
        : this(
            configFilePath: Path.Combine(
                AppDataPaths.GetAppDataRoot(),
                "config.yaml"),
            autodetectGamelogPath: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "EVE", "logs", "Gamelogs"),
            logger: logger)
    { }

    /// <summary>
    /// Test-seam constructor. Allows unit tests to point the loader at
    /// temp directories instead of <c>%APPDATA%</c> / <c>Documents</c>.
    /// </summary>
    /// <param name="configFilePath">Absolute path to the bootstrap YAML file (need not exist).</param>
    /// <param name="autodetectGamelogPath">Absolute path used for autodetect when <paramref name="configFilePath"/> is missing.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    internal YamlBootstrapConfigLoader(
        string configFilePath,
        string autodetectGamelogPath,
        ILogger<YamlBootstrapConfigLoader> logger)
    {
        _configFilePath = configFilePath ?? throw new ArgumentNullException(nameof(configFilePath));
        _autodetectGamelogPath = autodetectGamelogPath ?? throw new ArgumentNullException(nameof(autodetectGamelogPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<BootstrapConfigResult> LoadAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(_configFilePath))
        {
            if (Directory.Exists(_autodetectGamelogPath))
            {
                _logger.LogInformation(
                    "Bootstrap config not found at {ConfigPath}; using autodetected gamelog path {GamelogPath}.",
                    _configFilePath, _autodetectGamelogPath);
                return new BootstrapConfigResult.AutodetectUsed(_autodetectGamelogPath);
            }

            _logger.LogError(
                "Bootstrap config not found at {ConfigPath} AND autodetect path does not exist: {AttemptedPath}. " +
                "User must supply a gamelog path via the application's first-launch prompt.",
                _configFilePath, _autodetectGamelogPath);
            return new BootstrapConfigResult.AutodetectFailed(_autodetectGamelogPath);
        }

        var yamlText = await File.ReadAllTextAsync(_configFilePath, ct);

        BootstrapConfigDto dto;
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            dto = deserializer.Deserialize<BootstrapConfigDto>(yamlText)
                  ?? new BootstrapConfigDto();
        }
        catch (YamlException ex)
        {
            throw new BootstrapConfigException(
                $"config.yaml at {_configFilePath} is not valid YAML: {ex.Message}", ex);
        }

        var raw = dto.GamelogPath?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            throw new BootstrapConfigException(
                $"config.yaml at {_configFilePath} is missing the required 'gamelog_path' key " +
                "(or the value is empty). Please edit the file and supply your EVE Gamelogs directory path.");
        }

        var expanded = Environment.ExpandEnvironmentVariables(raw);
        return new BootstrapConfigResult.Loaded(expanded);
    }

    /// <inheritdoc />
    public async Task SaveAsync(string gamelogPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gamelogPath);
        ct.ThrowIfCancellationRequested();

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        var dto = new BootstrapConfigDto { GamelogPath = gamelogPath };
        var yaml = serializer.Serialize(dto);

        var parentDir = Path.GetDirectoryName(_configFilePath)!;
        Directory.CreateDirectory(parentDir);

        var tempPath = _configFilePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, yaml, ct);
        File.Move(tempPath, _configFilePath, overwrite: true);

        _logger.LogInformation(
            "Bootstrap config saved to {ConfigPath} with gamelog_path={GamelogPath}.",
            _configFilePath, gamelogPath);
    }
}
