using EveDashboard.Core.Configuration;
using EveDashboard.Persistence.Configuration;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EveDashboard.Persistence.Tests.Configuration;

public class YamlBootstrapConfigLoaderTests : IDisposable
{
    private readonly string _scratchRoot;

    public YamlBootstrapConfigLoaderTests()
    {
        _scratchRoot = Path.Combine(
            Path.GetTempPath(),
            $"eve-mining-bootstrap-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratchRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratchRoot))
        {
            Directory.Delete(_scratchRoot, recursive: true);
        }
    }

    private string CreateConfigPath(string fileName = "config.yaml")
        => Path.Combine(_scratchRoot, fileName);

    private string CreateAutodetectPath(bool exists)
    {
        var path = Path.Combine(_scratchRoot, $"autodetect-{Guid.NewGuid():N}");
        if (exists) Directory.CreateDirectory(path);
        return path;
    }

    private static YamlBootstrapConfigLoader CreateLoader(
        string configPath,
        string autodetectPath,
        out CapturingLogger<YamlBootstrapConfigLoader> capturing)
    {
        capturing = new CapturingLogger<YamlBootstrapConfigLoader>();
        return new YamlBootstrapConfigLoader(configPath, autodetectPath, capturing);
    }

    // 1
    [Fact]
    public void Constructor_throws_on_null_logger()
    {
        Action act = () => new YamlBootstrapConfigLoader(logger: null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // 2
    [Fact]
    public void Constructor_throws_on_null_configFilePath()
    {
        var capturing = new CapturingLogger<YamlBootstrapConfigLoader>();
        Action act = () => new YamlBootstrapConfigLoader(
            configFilePath: null!,
            autodetectGamelogPath: _scratchRoot,
            logger: capturing);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configFilePath");
    }

    // 3
    [Fact]
    public void Constructor_throws_on_null_autodetectGamelogPath()
    {
        var capturing = new CapturingLogger<YamlBootstrapConfigLoader>();
        Action act = () => new YamlBootstrapConfigLoader(
            configFilePath: CreateConfigPath(),
            autodetectGamelogPath: null!,
            logger: capturing);
        act.Should().Throw<ArgumentNullException>().WithParameterName("autodetectGamelogPath");
    }

    // 4
    [Fact]
    public async Task LoadAsync_returns_Loaded_when_file_present_and_valid()
    {
        var configPath = CreateConfigPath();
        await File.WriteAllTextAsync(configPath, "gamelog_path: D:\\test\\path\n");
        var loader = CreateLoader(configPath, CreateAutodetectPath(exists: false), out _);

        var result = await loader.LoadAsync();

        result.Should().BeOfType<BootstrapConfigResult.Loaded>()
            .Which.GamelogPath.Should().Be(@"D:\test\path");
    }

    // 5
    [Fact]
    public async Task LoadAsync_expands_environment_variables_in_loaded_path()
    {
        var configPath = CreateConfigPath();
        // %TEMP% always resolves on Windows. Single-quoted because '%' is a YAML
        // reserved indicator at the start of an unquoted scalar; quoting is
        // standard YAML hygiene and parallels what a real config file would do.
        await File.WriteAllTextAsync(configPath, "gamelog_path: '%TEMP%\\foo'\n");
        var loader = CreateLoader(configPath, CreateAutodetectPath(exists: false), out _);

        var result = await loader.LoadAsync();

        var loaded = result.Should().BeOfType<BootstrapConfigResult.Loaded>().Subject;
        loaded.GamelogPath.Should().NotContain("%TEMP%",
            "the env-var token must be expanded by Environment.ExpandEnvironmentVariables");
        loaded.GamelogPath.Should().EndWith(@"\foo");
    }

    // 6
    [Fact]
    public async Task LoadAsync_returns_AutodetectUsed_when_file_missing_and_autodetect_dir_exists()
    {
        var configPath = CreateConfigPath(); // path constructed but file not created
        var autodetect = CreateAutodetectPath(exists: true);
        var loader = CreateLoader(configPath, autodetect, out _);

        var result = await loader.LoadAsync();

        result.Should().BeOfType<BootstrapConfigResult.AutodetectUsed>()
            .Which.GamelogPath.Should().Be(autodetect);
    }

    // 7
    [Fact]
    public async Task LoadAsync_returns_AutodetectFailed_when_file_missing_and_autodetect_dir_missing()
    {
        var configPath = CreateConfigPath();
        var autodetect = CreateAutodetectPath(exists: false);
        var loader = CreateLoader(configPath, autodetect, out var log);

        var result = await loader.LoadAsync();

        result.Should().BeOfType<BootstrapConfigResult.AutodetectFailed>()
            .Which.AttemptedPath.Should().Be(autodetect);
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error,
            "the loader must log at Error level when autodetect fails");
    }

    // 8
    [Fact]
    public async Task LoadAsync_returns_Loaded_even_when_explicit_path_does_not_exist()
    {
        var configPath = CreateConfigPath();
        await File.WriteAllTextAsync(configPath, "gamelog_path: D:\\does\\not\\exist\\anywhere\n");
        var loader = CreateLoader(configPath, CreateAutodetectPath(exists: true), out _);

        var result = await loader.LoadAsync();

        // The loader's job is deliberately narrow; downstream validates
        // disk presence and surfaces the popup.
        result.Should().BeOfType<BootstrapConfigResult.Loaded>()
            .Which.GamelogPath.Should().Be(@"D:\does\not\exist\anywhere");
    }

    // 9
    [Fact]
    public async Task LoadAsync_throws_BootstrapConfigException_on_malformed_yaml()
    {
        var configPath = CreateConfigPath();
        // Unterminated double-quoted scalar is a hard YamlDotNet parse failure.
        await File.WriteAllTextAsync(configPath, "gamelog_path: \"unterminated\n");
        var loader = CreateLoader(configPath, CreateAutodetectPath(exists: true), out _);

        Func<Task> act = () => loader.LoadAsync();

        var ex = (await act.Should().ThrowAsync<BootstrapConfigException>()).Subject.Single();
        ex.InnerException.Should().NotBeNull("malformed YAML should preserve the YamlException as InnerException");
    }

    // 10
    [Fact]
    public async Task LoadAsync_throws_BootstrapConfigException_when_gamelog_path_key_missing()
    {
        var configPath = CreateConfigPath();
        await File.WriteAllTextAsync(configPath, "some_other_key: some_value\n");
        var loader = CreateLoader(configPath, CreateAutodetectPath(exists: true), out _);

        Func<Task> act = () => loader.LoadAsync();

        await act.Should().ThrowAsync<BootstrapConfigException>()
            .WithMessage("*gamelog_path*");
    }

    // 11
    [Fact]
    public async Task LoadAsync_throws_BootstrapConfigException_when_gamelog_path_value_empty()
    {
        var configPath = CreateConfigPath();
        // Quoted whitespace-only value; loader trims then sees empty.
        await File.WriteAllTextAsync(configPath, "gamelog_path: \"   \"\n");
        var loader = CreateLoader(configPath, CreateAutodetectPath(exists: true), out _);

        Func<Task> act = () => loader.LoadAsync();

        await act.Should().ThrowAsync<BootstrapConfigException>()
            .WithMessage("*gamelog_path*");
    }

    // 12
    [Fact]
    public async Task LoadAsync_ignores_unknown_keys_in_yaml()
    {
        var configPath = CreateConfigPath();
        await File.WriteAllTextAsync(configPath,
            "gamelog_path: D:\\path\nfuture_key: future_value\nanother_one: 42\n");
        var loader = CreateLoader(configPath, CreateAutodetectPath(exists: false), out _);

        var result = await loader.LoadAsync();

        result.Should().BeOfType<BootstrapConfigResult.Loaded>()
            .Which.GamelogPath.Should().Be(@"D:\path");
    }

    // 13
    [Fact]
    public async Task LoadAsync_honors_cancellation()
    {
        var configPath = CreateConfigPath();
        await File.WriteAllTextAsync(configPath, "gamelog_path: D:\\anything\n");
        var loader = CreateLoader(configPath, CreateAutodetectPath(exists: true), out _);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => loader.LoadAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // 14
    [Fact]
    public async Task SaveAsync_throws_on_null_path()
    {
        var loader = CreateLoader(CreateConfigPath(), CreateAutodetectPath(exists: false), out _);

        Func<Task> act = () => loader.SaveAsync(gamelogPath: null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("gamelogPath");
    }

    // 15
    [Fact]
    public async Task SaveAsync_writes_yaml_with_gamelog_path_and_round_trips_via_LoadAsync()
    {
        var configPath = CreateConfigPath();
        var loader = CreateLoader(configPath, CreateAutodetectPath(exists: false), out _);

        const string targetPath = @"D:\eve\my\gamelogs";
        await loader.SaveAsync(targetPath);

        File.Exists(configPath).Should().BeTrue();
        var written = await File.ReadAllTextAsync(configPath);
        written.Should().Contain("gamelog_path:")
            .And.Contain(targetPath);

        var result = await loader.LoadAsync();
        result.Should().BeOfType<BootstrapConfigResult.Loaded>()
            .Which.GamelogPath.Should().Be(targetPath);
    }

    // 16
    [Fact]
    public async Task SaveAsync_creates_parent_directory_if_missing()
    {
        var nestedDir = Path.Combine(_scratchRoot, "missing-parent", "and-grandparent");
        var configPath = Path.Combine(nestedDir, "config.yaml");
        var loader = CreateLoader(configPath, CreateAutodetectPath(exists: false), out _);

        Directory.Exists(nestedDir).Should().BeFalse("precondition: parent directory does not exist");

        await loader.SaveAsync(@"D:\some\path");

        Directory.Exists(nestedDir).Should().BeTrue();
        File.Exists(configPath).Should().BeTrue();
    }

    // 17
    [Fact]
    public async Task SaveAsync_overwrites_existing_file_atomically()
    {
        var configPath = CreateConfigPath();
        var loader = CreateLoader(configPath, CreateAutodetectPath(exists: false), out _);

        await loader.SaveAsync(@"D:\first\path");
        await loader.SaveAsync(@"D:\second\path");

        // Second save must win.
        var result = await loader.LoadAsync();
        result.Should().BeOfType<BootstrapConfigResult.Loaded>()
            .Which.GamelogPath.Should().Be(@"D:\second\path");

        // No leftover .tmp sibling after the atomic rename completes.
        File.Exists(configPath + ".tmp").Should().BeFalse(
            "the temp file used during atomic save must be renamed away by SaveAsync");
    }
}
