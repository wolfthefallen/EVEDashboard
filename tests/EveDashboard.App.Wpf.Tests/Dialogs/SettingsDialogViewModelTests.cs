namespace EveDashboard.App.Wpf.Tests.Dialogs;

using System.IO;
using EveDashboard.App.Wpf.Dialogs;
using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.Core.Configuration;
using EveDashboard.Core.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Unit tests for <see cref="SettingsDialogViewModel"/>.
/// Covers Load, Apply success path, Apply validation failure (yield + path),
/// markets pending Add/Remove flush semantics (edits reach the catalog only
/// on Apply), dirty flag tracking, and the bootstrap-failure-mode
/// branches: gated LoadAsync, gated ApplyAsync with retry-once
/// on save failure, <see cref="SettingsDialogViewModel.IsBootstrapFailureMode"/>,
/// <see cref="SettingsDialogViewModel.BannerMessage"/>, and the
/// <c>BootstrapApplySucceeded</c> event.
/// </summary>
public class SettingsDialogViewModelTests
{
    private static MarketStation Jita => new(60003760L, "Jita", "IV - Moon 4 - Caldari Navy", 10000002L, "The Forge");
    private static MarketStation Cj6mt => new(1000000001L, "C-J6MT", "1st Tier - Sotiyo", null, null);
    private static MarketStation Amarr => new(60008494L, "Amarr", "VIII - Oris - Emperor Family Academy", 10000043L, "Domain");

    private static SettingsDialogViewModel MakeVm(
        FakeSettingsRepository? settings = null,
        FakeMarketStationCatalog? stations = null,
        FakeBootstrapConfigLoader? config = null,
        FakeAddStationDialog? add = null,
        FakeBootstrapErrorReporter? errorReporter = null,
        BootstrapFailureContext? bootstrapFailure = null)
    {
        return new SettingsDialogViewModel(
            settings ?? new FakeSettingsRepository(),
            stations ?? new FakeMarketStationCatalog(Jita),
            config ?? new FakeBootstrapConfigLoader(),
            add ?? new FakeAddStationDialog(),
            errorReporter ?? new FakeBootstrapErrorReporter(),
            NullLoggerFactory.Instance,
            bootstrapFailure);
    }

    /// <summary>
    /// Create a temporary directory with one well-formed gamelog file the
    /// validator's <c>GamelogDiscoveryService</c> will accept (regex match +
    /// readable Listener: line within first 15 lines).
    /// </summary>
    private static string CreateValidGamelogDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "evm-22.1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "20260101_120000_900000001.txt");
        File.WriteAllText(file,
            "---------------------------------------------------------------\n" +
            "  Gamelog\n" +
            "  Listener: Test Pilot\n" +
            "  Session Started: 2026.01.01 12:00:00\n" +
            "---------------------------------------------------------------\n");
        return dir;
    }

    /* ---------- Constructor null guards ---------- */

    [Fact]
    public void Ctor_throws_on_null_settings()
    {
        Action act = () => new SettingsDialogViewModel(
            null!, new FakeMarketStationCatalog(), new FakeBootstrapConfigLoader(),
            new FakeAddStationDialog(), new FakeBootstrapErrorReporter(), NullLoggerFactory.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Ctor_throws_on_null_stations()
    {
        Action act = () => new SettingsDialogViewModel(
            new FakeSettingsRepository(), null!, new FakeBootstrapConfigLoader(),
            new FakeAddStationDialog(), new FakeBootstrapErrorReporter(), NullLoggerFactory.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("stations");
    }

    [Fact]
    public void Ctor_throws_on_null_config()
    {
        Action act = () => new SettingsDialogViewModel(
            new FakeSettingsRepository(), new FakeMarketStationCatalog(), null!,
            new FakeAddStationDialog(), new FakeBootstrapErrorReporter(), NullLoggerFactory.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    [Fact]
    public void Ctor_throws_on_null_addStationDialog()
    {
        Action act = () => new SettingsDialogViewModel(
            new FakeSettingsRepository(), new FakeMarketStationCatalog(), new FakeBootstrapConfigLoader(),
            null!, new FakeBootstrapErrorReporter(), NullLoggerFactory.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("addStationDialog");
    }

    [Fact]
    public void Ctor_throws_on_null_errorReporter()
    {
        Action act = () => new SettingsDialogViewModel(
            new FakeSettingsRepository(), new FakeMarketStationCatalog(), new FakeBootstrapConfigLoader(),
            new FakeAddStationDialog(), null!, NullLoggerFactory.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("errorReporter");
    }

    [Fact]
    public void Ctor_throws_on_null_loggerFactory()
    {
        Action act = () => new SettingsDialogViewModel(
            new FakeSettingsRepository(), new FakeMarketStationCatalog(), new FakeBootstrapConfigLoader(),
            new FakeAddStationDialog(), new FakeBootstrapErrorReporter(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("loggerFactory");
    }

    /* ---------- LoadAsync ---------- */

    [Fact]
    public async Task LoadAsync_populates_all_fields_and_resets_dirty()
    {
        var settings = new FakeSettingsRepository
        {
            Theme = ThemeMode.Light,
            PriceMode = PriceMode.Sell,
            RefiningYieldPercent = 75.5,
            SelectedStationId = Cj6mt.StationId,
        };
        var stations = new FakeMarketStationCatalog(Jita, Cj6mt);
        var config = new FakeBootstrapConfigLoader(new BootstrapConfigResult.Loaded(@"D:\path\Gamelogs"));

        var vm = MakeVm(settings, stations, config);
        await vm.LoadAsync();

        vm.Theme.Should().Be(ThemeMode.Light);
        vm.PriceMode.Should().Be(PriceMode.Sell);
        vm.RefiningYieldText.Should().Be("75.5");
        vm.SelectedStationId.Should().Be(Cj6mt.StationId);
        vm.GamelogPath.Should().Be(@"D:\path\Gamelogs");
        vm.Stations.Should().HaveCount(2);
        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_with_AutodetectFailed_uses_AttemptedPath()
    {
        var config = new FakeBootstrapConfigLoader(
            new BootstrapConfigResult.AutodetectFailed(@"C:\bogus\Gamelogs"));
        var vm = MakeVm(config: config);
        await vm.LoadAsync();
        vm.GamelogPath.Should().Be(@"C:\bogus\Gamelogs");
    }

    /* ---------- ApplyAsync — success path ---------- */

    [Fact]
    public async Task ApplyAsync_AllValid_PersistsAndResetsDirty()
    {
        var dir = CreateValidGamelogDir();
        try
        {
            var settings = new FakeSettingsRepository();
            var stations = new FakeMarketStationCatalog(Jita);
            var config = new FakeBootstrapConfigLoader();
            var vm = MakeVm(settings, stations, config);

            await vm.LoadAsync();
            vm.Theme = ThemeMode.Light;
            vm.RefiningYieldText = "75.5";
            vm.PriceMode = PriceMode.Sell;
            vm.GamelogPath = dir;
            vm.IsDirty.Should().BeTrue();

            var result = await vm.ApplyAsync();

            result.Saved.Should().BeTrue();
            result.Errors.Should().BeEmpty();
            settings.Theme.Should().Be(ThemeMode.Light);
            settings.PriceMode.Should().Be(PriceMode.Sell);
            settings.RefiningYieldPercent.Should().Be(75.5);
            settings.SetThemeCount.Should().Be(1);
            settings.SetPriceModeCount.Should().Be(1);
            settings.SetRefiningYieldCount.Should().Be(1);
            settings.SetSelectedStationIdCount.Should().Be(1);
            config.SaveCount.Should().Be(1);
            config.LastSavedPath.Should().Be(dir);
            vm.IsDirty.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_PreservesSelectedStationIdAfterRebuild()
    {
        // Regression: prior to the fix, ApplyAsync's RebuildStationsView call
        // briefly emptied the markets collection. In production WPF, that
        // empties the ComboBox's ItemsSource, the TwoWay SelectedValue binding
        // writes 0 back into _selectedStationId via the property setter, and
        // the user sees the Selected station ComboBox go blank after clicking
        // Apply. The fix snapshots _selectedStationId before the rebuild and
        // restores it afterwards. This test exercises the VM-side semantic by
        // asserting SelectedStationId is preserved across Apply.
        var dir = CreateValidGamelogDir();
        try
        {
            var settings = new FakeSettingsRepository { SelectedStationId = Cj6mt.StationId };
            var stations = new FakeMarketStationCatalog(Jita, Cj6mt);
            var vm = MakeVm(settings, stations);
            await vm.LoadAsync();
            vm.GamelogPath = dir;
            vm.SelectedStationId.Should().Be(Cj6mt.StationId);

            var result = await vm.ApplyAsync();

            result.Saved.Should().BeTrue();
            vm.SelectedStationId.Should().Be(Cj6mt.StationId,
                "SelectedStationId must survive the markets-collection rebuild that ApplyAsync performs");
            settings.SelectedStationId.Should().Be(Cj6mt.StationId);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /* ---------- ApplyAsync — validation failures ---------- */

    [Fact]
    public async Task ApplyAsync_OutOfRangeYield_ReturnsErrorAndDoesNotPersist()
    {
        var dir = CreateValidGamelogDir();
        try
        {
            var settings = new FakeSettingsRepository();
            var vm = MakeVm(settings);
            await vm.LoadAsync();
            vm.GamelogPath = dir;
            vm.RefiningYieldText = "95";

            var result = await vm.ApplyAsync();

            result.Saved.Should().BeFalse();
            result.Errors.Should().ContainSingle()
                .Which.Should().Contain("Refining yield must be between");
            settings.SetRefiningYieldCount.Should().Be(0);
            settings.SetThemeCount.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_NonNumericYield_ReturnsErrorAndDoesNotPersist()
    {
        var dir = CreateValidGamelogDir();
        try
        {
            var settings = new FakeSettingsRepository();
            var vm = MakeVm(settings);
            await vm.LoadAsync();
            vm.GamelogPath = dir;
            vm.RefiningYieldText = "abc";

            var result = await vm.ApplyAsync();

            result.Saved.Should().BeFalse();
            result.Errors.Should().ContainSingle()
                .Which.Should().Contain("not a valid number");
            settings.SetRefiningYieldCount.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_InvalidGamelogPath_ReturnsErrorAndDoesNotPersist()
    {
        var settings = new FakeSettingsRepository();
        var vm = MakeVm(settings);
        await vm.LoadAsync();
        vm.GamelogPath = @"Z:\definitely\does\not\exist-22.1-test";

        var result = await vm.ApplyAsync();

        result.Saved.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("does not exist"));
        settings.SetThemeCount.Should().Be(0);
        settings.SetRefiningYieldCount.Should().Be(0);
    }

    [Fact]
    public async Task ApplyAsync_EmptyGamelogPath_ReturnsErrorAndDoesNotPersist()
    {
        var settings = new FakeSettingsRepository();
        var vm = MakeVm(settings);
        await vm.LoadAsync();
        vm.GamelogPath = "   ";

        var result = await vm.ApplyAsync();

        result.Saved.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Gamelog folder path is empty"));
        settings.SetThemeCount.Should().Be(0);
    }

    [Fact]
    public async Task ApplyAsync_GamelogFolderHasNoMatchingFiles_ReturnsError()
    {
        var dir = Path.Combine(Path.GetTempPath(), "evm-22.1-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var settings = new FakeSettingsRepository();
            var vm = MakeVm(settings);
            await vm.LoadAsync();
            vm.GamelogPath = dir;

            var result = await vm.ApplyAsync();

            result.Saved.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("contains no EVE gamelog files"));
            settings.SetThemeCount.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /* ---------- Markets pending model ---------- */

    [Fact]
    public async Task AddStation_PendingThenApply_PersistsViaCatalog()
    {
        var dir = CreateValidGamelogDir();
        try
        {
            var stations = new FakeMarketStationCatalog(Jita);
            var add = new FakeAddStationDialog(Amarr);
            var vm = MakeVm(stations: stations, add: add);
            await vm.LoadAsync();
            vm.GamelogPath = dir;

            vm.AddStationCommand.Execute(null);

            vm.Stations.Should().Contain(s => s.StationId == Amarr.StationId);
            stations.AddCount.Should().Be(0);
            vm.IsDirty.Should().BeTrue();

            var result = await vm.ApplyAsync();

            result.Saved.Should().BeTrue();
            stations.AddCount.Should().Be(1);
            stations.AddedStations.Should().ContainSingle(s => s.StationId == Amarr.StationId);
            vm.IsDirty.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RemoveStation_PendingThenApply_PersistsViaCatalog()
    {
        var dir = CreateValidGamelogDir();
        try
        {
            var stations = new FakeMarketStationCatalog(Jita, Cj6mt);
            var vm = MakeVm(stations: stations);
            await vm.LoadAsync();
            vm.GamelogPath = dir;

            vm.RemoveStationCommand.Execute(Cj6mt);

            vm.Stations.Should().NotContain(s => s.StationId == Cj6mt.StationId);
            stations.RemoveCount.Should().Be(0);
            vm.IsDirty.Should().BeTrue();

            var result = await vm.ApplyAsync();

            result.Saved.Should().BeTrue();
            stations.RemoveCount.Should().Be(1);
            stations.RemovedIds.Should().ContainSingle(id => id == Cj6mt.StationId);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RemoveStation_OfPendingAdd_DropsPendingNotRemovesPersisted()
    {
        var dir = CreateValidGamelogDir();
        try
        {
            var stations = new FakeMarketStationCatalog(Jita);
            var add = new FakeAddStationDialog(Amarr);
            var vm = MakeVm(stations: stations, add: add);
            await vm.LoadAsync();
            vm.GamelogPath = dir;

            vm.AddStationCommand.Execute(null);
            vm.Stations.Should().Contain(s => s.StationId == Amarr.StationId);

            vm.RemoveStationCommand.Execute(Amarr);
            vm.Stations.Should().NotContain(s => s.StationId == Amarr.StationId);

            await vm.ApplyAsync();

            stations.AddCount.Should().Be(0);
            stations.RemoveCount.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task AddStation_DuplicateId_IsNoOp()
    {
        // Persisted Jita already in catalog; the AddStationDialog returns a
        // station with the same id — VM should warn-and-ignore (no pending add).
        var stations = new FakeMarketStationCatalog(Jita);
        var add = new FakeAddStationDialog(new MarketStation(Jita.StationId, "Jita", "Different name", null, null));
        var vm = MakeVm(stations: stations, add: add);
        await vm.LoadAsync();

        vm.AddStationCommand.Execute(null);

        vm.Stations.Where(s => s.StationId == Jita.StationId).Should().HaveCount(1);
        vm.Stations.First(s => s.StationId == Jita.StationId).StationName.Should().Be(Jita.StationName);
    }

    /* ---------- Dirty flag ---------- */

    [Fact]
    public async Task SetField_DirtyFlagSetCorrectly()
    {
        var settings = new FakeSettingsRepository { Theme = ThemeMode.Dark };
        var vm = MakeVm(settings);

        await vm.LoadAsync();
        vm.IsDirty.Should().BeFalse();

        vm.Theme = ThemeMode.Light;
        vm.IsDirty.Should().BeTrue();

        await vm.LoadAsync();
        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveStation_OfSelectedStation_FallsBackToFirstRemaining()
    {
        var stations = new FakeMarketStationCatalog(Jita, Cj6mt);
        var settings = new FakeSettingsRepository { SelectedStationId = Cj6mt.StationId };
        var vm = MakeVm(settings, stations);
        await vm.LoadAsync();

        vm.SelectedStationId.Should().Be(Cj6mt.StationId);

        vm.RemoveStationCommand.Execute(Cj6mt);

        vm.SelectedStationId.Should().NotBe(Cj6mt.StationId);
        vm.SelectedStationId.Should().BeOneOf(Jita.StationId, 0L);
    }

    /* ---------- Bootstrap-failure mode ---------- */

    [Fact]
    public void IsBootstrapFailureMode_is_false_in_normal_mode()
    {
        var vm = MakeVm();
        vm.IsBootstrapFailureMode.Should().BeFalse();
        vm.BannerMessage.Should().BeEmpty();
    }

    [Fact]
    public void BannerMessage_populated_from_BootstrapFailureContext()
    {
        var failure = new BootstrapFailureContext(
            BootstrapFailureKind.ConfigException,
            "Your config.yaml is malformed: foo bar");

        var vm = MakeVm(bootstrapFailure: failure);

        vm.IsBootstrapFailureMode.Should().BeTrue();
        vm.BannerMessage.Should().Be(failure.Message);
    }

    [Fact]
    public async Task LoadAsync_in_bootstrap_failure_mode_reads_only_gamelog_path()
    {
        // Stubs throw if their methods are called; failure-mode LoadAsync must NOT touch them.
        var config = new FakeBootstrapConfigLoader(new BootstrapConfigResult.Loaded(@"C:\fake\Gamelogs"));
        var vm = new SettingsDialogViewModel(
            settings:         NoOpSettingsRepository.Instance,
            stations:         NoOpMarketStationCatalog.Instance,
            config:           config,
            addStationDialog: NoOpAddStationDialog.Instance,
            errorReporter:    new FakeBootstrapErrorReporter(),
            loggerFactory:    NullLoggerFactory.Instance,
            bootstrapFailure: new BootstrapFailureContext(BootstrapFailureKind.ZeroCharacters, "no chars"));

        await vm.LoadAsync();

        vm.GamelogPath.Should().Be(@"C:\fake\Gamelogs");
        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_in_bootstrap_failure_mode_skips_set_async_and_calls_config_save()
    {
        var dir = CreateValidGamelogDir();
        try
        {
            var config = new FakeBootstrapConfigLoader();
            var vm = new SettingsDialogViewModel(
                settings:         NoOpSettingsRepository.Instance,
                stations:         NoOpMarketStationCatalog.Instance,
                config:           config,
                addStationDialog: NoOpAddStationDialog.Instance,
                errorReporter:    new FakeBootstrapErrorReporter(),
                loggerFactory:    NullLoggerFactory.Instance,
                bootstrapFailure: new BootstrapFailureContext(BootstrapFailureKind.AutodetectFailed, "missing"));

            vm.GamelogPath = dir;

            int eventCount = 0;
            vm.BootstrapApplySucceeded += (_, _) => eventCount++;

            var result = await vm.ApplyAsync();

            result.Saved.Should().BeTrue();
            config.SaveCount.Should().Be(1);
            config.LastSavedPath.Should().Be(dir);
            eventCount.Should().Be(1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_in_bootstrap_failure_mode_invalid_path_returns_error_and_does_not_save()
    {
        var config = new FakeBootstrapConfigLoader();
        var vm = new SettingsDialogViewModel(
            settings:         NoOpSettingsRepository.Instance,
            stations:         NoOpMarketStationCatalog.Instance,
            config:           config,
            addStationDialog: NoOpAddStationDialog.Instance,
            errorReporter:    new FakeBootstrapErrorReporter(),
            loggerFactory:    NullLoggerFactory.Instance,
            bootstrapFailure: new BootstrapFailureContext(BootstrapFailureKind.AutodetectFailed, "missing"));

        vm.GamelogPath = @"C:\does\not\exist\nope-" + Guid.NewGuid().ToString("N");

        int eventCount = 0;
        vm.BootstrapApplySucceeded += (_, _) => eventCount++;

        var result = await vm.ApplyAsync();

        result.Saved.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        config.SaveCount.Should().Be(0);
        eventCount.Should().Be(0);
    }

    [Fact]
    public async Task ApplyAsync_in_bootstrap_failure_mode_save_failure_first_attempt_retains_retry()
    {
        var dir = CreateValidGamelogDir();
        try
        {
            var config = new FakeBootstrapConfigLoader { SaveExceptionToThrowOnce = new IOException("locked") };
            var reporter = new FakeBootstrapErrorReporter();
            var vm = new SettingsDialogViewModel(
                settings:         NoOpSettingsRepository.Instance,
                stations:         NoOpMarketStationCatalog.Instance,
                config:           config,
                addStationDialog: NoOpAddStationDialog.Instance,
                errorReporter:    reporter,
                loggerFactory:    NullLoggerFactory.Instance,
                bootstrapFailure: new BootstrapFailureContext(BootstrapFailureKind.ConfigException, "original"));

            vm.GamelogPath = dir;

            int eventCount = 0;
            vm.BootstrapApplySucceeded += (_, _) => eventCount++;

            var result = await vm.ApplyAsync();

            result.Saved.Should().BeFalse();
            vm.BannerMessage.Should().Contain("Could not save");
            vm.BannerMessage.Should().Contain("locked");
            reporter.CallCount.Should().Be(0);  // first failure: no fallback
            eventCount.Should().Be(0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_in_bootstrap_failure_mode_save_failure_second_attempt_falls_back_to_shutdown()
    {
        var dir = CreateValidGamelogDir();
        try
        {
            // SaveExceptionToThrow keeps throwing on every call.
            var config = new FakeBootstrapConfigLoader { SaveExceptionToThrow = new IOException("permanent") };
            var reporter = new FakeBootstrapErrorReporter();
            var vm = new SettingsDialogViewModel(
                settings:         NoOpSettingsRepository.Instance,
                stations:         NoOpMarketStationCatalog.Instance,
                config:           config,
                addStationDialog: NoOpAddStationDialog.Instance,
                errorReporter:    reporter,
                loggerFactory:    NullLoggerFactory.Instance,
                bootstrapFailure: new BootstrapFailureContext(BootstrapFailureKind.ConfigException, "original failure"));

            vm.GamelogPath = dir;

            // First attempt: bumps counter to 1, no shutdown.
            await vm.ApplyAsync();
            reporter.CallCount.Should().Be(0);

            // Second consecutive attempt: bumps counter to 2, fires fallback.
            await vm.ApplyAsync();
            reporter.CallCount.Should().Be(1);
            reporter.LastTitle.Should().Be("Could not save your gamelog folder");
            reporter.LastBody.Should().Contain("original failure");
            reporter.LastBody.Should().Contain("permanent");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_in_bootstrap_failure_mode_save_success_after_failure_resets_retry_counter()
    {
        var dir = CreateValidGamelogDir();
        try
        {
            // First call throws, second succeeds.
            var config = new FakeBootstrapConfigLoader { SaveExceptionToThrowOnce = new IOException("transient") };
            var reporter = new FakeBootstrapErrorReporter();
            var vm = new SettingsDialogViewModel(
                settings:         NoOpSettingsRepository.Instance,
                stations:         NoOpMarketStationCatalog.Instance,
                config:           config,
                addStationDialog: NoOpAddStationDialog.Instance,
                errorReporter:    reporter,
                loggerFactory:    NullLoggerFactory.Instance,
                bootstrapFailure: new BootstrapFailureContext(BootstrapFailureKind.ZeroCharacters, "no chars"));

            vm.GamelogPath = dir;

            int eventCount = 0;
            vm.BootstrapApplySucceeded += (_, _) => eventCount++;

            // First Apply throws.
            var first = await vm.ApplyAsync();
            first.Saved.Should().BeFalse();

            // Second Apply succeeds — retry counter resets, fallback NOT triggered.
            var second = await vm.ApplyAsync();
            second.Saved.Should().BeTrue();

            reporter.CallCount.Should().Be(0);  // never escalated.
            eventCount.Should().Be(1);
            config.SaveCount.Should().Be(1);    // only the successful Save bumped this.
            config.LastSavedPath.Should().Be(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
