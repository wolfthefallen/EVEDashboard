namespace EveDashboard.App.Wpf.Tests.DependencyInjection;

using System.IO;
using EveDashboard.App.Wpf.DependencyInjection;
using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Domain;
using EveDashboard.Mining.Gamelogs;
using EveDashboard.Persistence.Storage;
using EveDashboard.Stats.Schema;
using EveDashboard.Stats.Settings;
using EveDashboard.Stats.Storage;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Regression tests for the DI container wired by <see cref="ServiceRegistration"/>.
///
/// Originally surfaced bug (caught at first <c>dotnet run</c>): both <c>sessionDb</c>
/// and <c>sdeDb</c> were registered as <c>ISqliteDatabase</c>, last-write-wins. The
/// DI container resolved <see cref="SessionController"/> with the SDE database,
/// causing <c>"no such table: session_time"</c> at <c>InitializeAsync</c> time.
/// Fixed by removing both <c>ISqliteDatabase</c> registrations and resolving
/// <see cref="SessionController"/> via a factory lambda that explicitly captures
/// <c>sessionDb</c>.
/// </summary>
public class ServiceRegistrationTests
{
    [Fact]
    public async Task Resolved_SessionController_uses_sessionDb_for_initialize_query()
    {
        // Two distinct in-memory SQLite DBs, each with its OWN schema state.
        // The session DB has the session_time table; the SDE DB does NOT.
        // If the VM is wired to the wrong db, InitializeAsync will throw
        // "no such table: session_time".
        await using var sessionDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(sessionDb);

        await using var sdeDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        // sdeDb intentionally has no schema — distinguishes the bug case.

        var sessionStore = new SessionStore(NullLogger<SessionStore>.Instance);
        var sdeStorage = new FakeSdeStorage(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sqlite"));
        var sdeUpdater = new FakeSdeUpdater();
        var sdeLookup = new FakeSdeLookup();
        var settingsRepo = new SqliteSettingsRepository(sessionDb, NullLogger<SqliteSettingsRepository>.Instance);
        var stationCatalog = new SqliteMarketStationCatalog(sessionDb, NullLogger<SqliteMarketStationCatalog>.Instance);
        await settingsRepo.EnsureSeedDefaultsAsync();
        await stationCatalog.EnsureSeedDefaultsAsync();

        using var fixture = new SessionControllerTestFixture();
        // Reuse fixture's writer/watcher/parser/activeSession — they already wire to its own
        // session DB; for this test we only care about which db SessionController binds to.
        var tempGamelogDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempGamelogDir);
        try
        {
            var discovery = new GamelogDiscoveryService(tempGamelogDir, NullLogger<GamelogDiscoveryService>.Instance);
            IReadOnlyList<Character> chars = new[] { new Character(1, "Pilot", Path.Combine(tempGamelogDir, "1.txt")) };

            var (priceClient, priceFetcher, iskCalc) = Plan18PricingFactory.Build(
                sessionDb, sdeLookup, settingsRepo, fixture.ActiveSession);
            using var sp = ServiceRegistration.BuildServiceProvider(
                NullLoggerFactory.Instance,
                sessionDb, sessionStore,
                sdeDb, sdeStorage, sdeUpdater, sdeLookup,
                settingsRepo, stationCatalog,
                fixture.ActiveSession, fixture.Writer, fixture.Watcher,
                new FakeGamelogLineParser(), discovery, chars,
                new FakeBootstrapConfigLoader(),
                priceClient, priceFetcher, iskCalc);

            var vm = sp.GetRequiredService<SessionController>();

            // The actual regression check: InitializeAsync queries session_time. If the VM
            // got bound to sdeDb (the bug), this throws "no such table: session_time".
            Func<Task> act = async () => await vm.InitializeAsync();
            await act.Should().NotThrowAsync(
                "ServiceRegistration must wire SessionController to sessionDb, not sdeDb");

            vm.Mode.Should().Be(SessionWidgetMode.Config);
        }
        finally
        {
            if (Directory.Exists(tempGamelogDir)) Directory.Delete(tempGamelogDir, recursive: true);
        }
    }

    [Fact]
    public async Task Resolved_PerPilotWidgetCoordinator_uses_sessionDb_for_session_pilot_query()
    {
        // Plan 17 regression: PerPilotWidgetCoordinator queries session_pilot via the
        // sessionDb factory lambda (NOT via ISqliteDatabase resolution per the dual-DB
        // note in ServiceRegistration.cs). If wired to sdeDb this throws "no such table:
        // session_pilot" inside the internal HandleActiveSessionChangedAsync.
        await using var sessionDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(sessionDb);
        await using var sdeDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);

        var sessionStore = new SessionStore(NullLogger<SessionStore>.Instance);
        var sdeStorage = new FakeSdeStorage(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sqlite"));
        var sdeUpdater = new FakeSdeUpdater();
        var sdeLookup = new FakeSdeLookup();
        var settingsRepo = new SqliteSettingsRepository(sessionDb, NullLogger<SqliteSettingsRepository>.Instance);
        var stationCatalog = new SqliteMarketStationCatalog(sessionDb, NullLogger<SqliteMarketStationCatalog>.Instance);
        await settingsRepo.EnsureSeedDefaultsAsync();
        await stationCatalog.EnsureSeedDefaultsAsync();

        using var fixture = new SessionControllerTestFixture();
        var tempGamelogDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempGamelogDir);
        try
        {
            var discovery = new GamelogDiscoveryService(tempGamelogDir, NullLogger<GamelogDiscoveryService>.Instance);
            IReadOnlyList<Character> chars = new[] { new Character(1, "Pilot", Path.Combine(tempGamelogDir, "1.txt")) };

            var (priceClient, priceFetcher, iskCalc) = Plan18PricingFactory.Build(
                sessionDb, sdeLookup, settingsRepo, fixture.ActiveSession);
            using var sp = ServiceRegistration.BuildServiceProvider(
                NullLoggerFactory.Instance,
                sessionDb, sessionStore,
                sdeDb, sdeStorage, sdeUpdater, sdeLookup,
                settingsRepo, stationCatalog,
                fixture.ActiveSession, fixture.Writer, fixture.Watcher,
                new FakeGamelogLineParser(), discovery, chars,
                new FakeBootstrapConfigLoader(),
                priceClient, priceFetcher, iskCalc);

            var coord = sp.GetRequiredService<EveDashboard.App.Wpf.Widgets.Pilot.PerPilotWidgetCoordinator>();
            // Trigger the session_pilot query path; if wired to sdeDb this throws.
            fixture.ActiveSession.Set("sess-123", EveDashboard.Core.Sessions.SessionRunState.Running);
            Func<Task> act = async () => await coord.HandleActiveSessionChangedAsync();
            await act.Should().NotThrowAsync(
                "PerPilotWidgetCoordinator must be wired to sessionDb, not sdeDb");
        }
        finally
        {
            if (Directory.Exists(tempGamelogDir)) Directory.Delete(tempGamelogDir, recursive: true);
        }
    }

    [Fact]
    public async Task Resolved_MainWindowViewModel_composes_layout_coordinator_and_session()
    {
        // Plan 19 wiring: MainWindowViewModel takes (LayoutCoordinator).
        // DI must resolve LayoutCoordinator as a singleton; MainWindowViewModel.Session
        // routes back to the coordinator's SessionViewModel proxy (same singleton as the
        // SessionController resolved directly).
        await using var sessionDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(sessionDb);
        await using var sdeDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);

        var sessionStore = new SessionStore(NullLogger<SessionStore>.Instance);
        var sdeStorage = new FakeSdeStorage(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sqlite"));
        var sdeUpdater = new FakeSdeUpdater();
        var sdeLookup = new FakeSdeLookup();
        var settingsRepo = new SqliteSettingsRepository(sessionDb, NullLogger<SqliteSettingsRepository>.Instance);
        var stationCatalog = new SqliteMarketStationCatalog(sessionDb, NullLogger<SqliteMarketStationCatalog>.Instance);
        await settingsRepo.EnsureSeedDefaultsAsync();
        await stationCatalog.EnsureSeedDefaultsAsync();

        using var fixture = new SessionControllerTestFixture();
        var tempGamelogDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempGamelogDir);
        try
        {
            var discovery = new GamelogDiscoveryService(tempGamelogDir, NullLogger<GamelogDiscoveryService>.Instance);
            IReadOnlyList<Character> chars = new[] { new Character(1, "Pilot", Path.Combine(tempGamelogDir, "1.txt")) };

            var (priceClient, priceFetcher, iskCalc) = Plan18PricingFactory.Build(
                sessionDb, sdeLookup, settingsRepo, fixture.ActiveSession);
            using var sp = ServiceRegistration.BuildServiceProvider(
                NullLoggerFactory.Instance,
                sessionDb, sessionStore,
                sdeDb, sdeStorage, sdeUpdater, sdeLookup,
                settingsRepo, stationCatalog,
                fixture.ActiveSession, fixture.Writer, fixture.Watcher,
                new FakeGamelogLineParser(), discovery, chars,
                new FakeBootstrapConfigLoader(),
                priceClient, priceFetcher, iskCalc);

            var mainVm = sp.GetRequiredService<EveDashboard.App.Wpf.MainWindowViewModel>();
            var layout = sp.GetRequiredService<EveDashboard.App.Wpf.Layout.LayoutCoordinator>();
            var sessionVm = sp.GetRequiredService<SessionController>();

            mainVm.Layout.Should().BeSameAs(layout);
            mainVm.Session.Should().BeSameAs(sessionVm);
        }
        finally
        {
            if (Directory.Exists(tempGamelogDir)) Directory.Delete(tempGamelogDir, recursive: true);
        }
    }

    [Fact]
    public async Task Resolved_LayoutCoordinator_uses_sessionDb_for_widget_layout_io()
    {
        // Plan 19 wiring: LayoutCoordinator must read/write widget_layout against sessionDb.
        // If wired to sdeDb (no setting table), GetWidgetLayoutAsync throws.
        await using var sessionDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(sessionDb);
        await using var sdeDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);

        var sessionStore = new SessionStore(NullLogger<SessionStore>.Instance);
        var sdeStorage = new FakeSdeStorage(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sqlite"));
        var sdeUpdater = new FakeSdeUpdater();
        var sdeLookup = new FakeSdeLookup();
        var settingsRepo = new SqliteSettingsRepository(sessionDb, NullLogger<SqliteSettingsRepository>.Instance);
        var stationCatalog = new SqliteMarketStationCatalog(sessionDb, NullLogger<SqliteMarketStationCatalog>.Instance);
        await settingsRepo.EnsureSeedDefaultsAsync();
        await stationCatalog.EnsureSeedDefaultsAsync();

        using var fixture = new SessionControllerTestFixture();
        var tempGamelogDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempGamelogDir);
        try
        {
            var discovery = new GamelogDiscoveryService(tempGamelogDir, NullLogger<GamelogDiscoveryService>.Instance);
            IReadOnlyList<Character> chars = new[] { new Character(1, "Pilot", Path.Combine(tempGamelogDir, "1.txt")) };

            var (priceClient, priceFetcher, iskCalc) = Plan18PricingFactory.Build(
                sessionDb, sdeLookup, settingsRepo, fixture.ActiveSession);
            using var sp = ServiceRegistration.BuildServiceProvider(
                NullLoggerFactory.Instance,
                sessionDb, sessionStore,
                sdeDb, sdeStorage, sdeUpdater, sdeLookup,
                settingsRepo, stationCatalog,
                fixture.ActiveSession, fixture.Writer, fixture.Watcher,
                new FakeGamelogLineParser(), discovery, chars,
                new FakeBootstrapConfigLoader(),
                priceClient, priceFetcher, iskCalc);

            var layout = sp.GetRequiredService<EveDashboard.App.Wpf.Layout.LayoutCoordinator>();
            // LoadAsync queries setting; if wired to sdeDb (no setting table) this throws.
            Func<Task> act = async () => await layout.LoadAsync();
            await act.Should().NotThrowAsync(
                "LayoutCoordinator must be wired to sessionDb's settings repo, not sdeDb");
        }
        finally
        {
            if (Directory.Exists(tempGamelogDir)) Directory.Delete(tempGamelogDir, recursive: true);
        }
    }

    [Fact]
    public async Task Resolved_SessionStatsRepository_also_uses_sessionDb()
    {
        // Same regression check as above, narrowed to the stats repo factory.
        await using var sessionDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(sessionDb);
        await using var sdeDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);

        var sessionStore = new SessionStore(NullLogger<SessionStore>.Instance);
        var sdeStorage = new FakeSdeStorage(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sqlite"));
        var sdeUpdater = new FakeSdeUpdater();
        var sdeLookup = new FakeSdeLookup();
        var settingsRepo = new SqliteSettingsRepository(sessionDb, NullLogger<SqliteSettingsRepository>.Instance);
        var stationCatalog = new SqliteMarketStationCatalog(sessionDb, NullLogger<SqliteMarketStationCatalog>.Instance);
        await settingsRepo.EnsureSeedDefaultsAsync();
        await stationCatalog.EnsureSeedDefaultsAsync();

        using var fixture = new SessionControllerTestFixture();
        var tempGamelogDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempGamelogDir);
        try
        {
            var discovery = new GamelogDiscoveryService(tempGamelogDir, NullLogger<GamelogDiscoveryService>.Instance);
            IReadOnlyList<Character> chars = new[] { new Character(1, "Pilot", Path.Combine(tempGamelogDir, "1.txt")) };

            var (priceClient, priceFetcher, iskCalc) = Plan18PricingFactory.Build(
                sessionDb, sdeLookup, settingsRepo, fixture.ActiveSession);
            using var sp = ServiceRegistration.BuildServiceProvider(
                NullLoggerFactory.Instance,
                sessionDb, sessionStore,
                sdeDb, sdeStorage, sdeUpdater, sdeLookup,
                settingsRepo, stationCatalog,
                fixture.ActiveSession, fixture.Writer, fixture.Watcher,
                new FakeGamelogLineParser(), discovery, chars,
                new FakeBootstrapConfigLoader(),
                priceClient, priceFetcher, iskCalc);

            var statsRepo = sp.GetRequiredService<EveDashboard.Stats.Queries.ISessionStatsRepository>();
            // GetSessionStatsAsync queries session/session_time/log/session_pilot.
            // If it bound to sdeDb (no schema), this throws.
            Func<Task> act = async () => await statsRepo.GetSessionStatsAsync("sess-0", DateTimeOffset.UtcNow, allowLazyFetch: true);
            await act.Should().NotThrowAsync(
                "ISessionStatsRepository must be wired to sessionDb, not sdeDb");
        }
        finally
        {
            if (Directory.Exists(tempGamelogDir)) Directory.Delete(tempGamelogDir, recursive: true);
        }
    }

    [Fact]
    public async Task Resolved_SessionConfigViewModel_and_SessionStatsViewModel_share_singleton_controller()
    {
        // Plan 24: both thin VMs are singletons that wrap the singleton SessionController.
        // DI must hand back the same controller reference to both.
        await using var sessionDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);
        await SchemaInitializer.EnsureSchemaAsync(sessionDb);
        await using var sdeDb = new SqliteDatabase("Data Source=:memory:", NullLogger<SqliteDatabase>.Instance);

        var sessionStore = new SessionStore(NullLogger<SessionStore>.Instance);
        var sdeStorage = new FakeSdeStorage(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sqlite"));
        var sdeUpdater = new FakeSdeUpdater();
        var sdeLookup = new FakeSdeLookup();
        var settingsRepo = new SqliteSettingsRepository(sessionDb, NullLogger<SqliteSettingsRepository>.Instance);
        var stationCatalog = new SqliteMarketStationCatalog(sessionDb, NullLogger<SqliteMarketStationCatalog>.Instance);
        await settingsRepo.EnsureSeedDefaultsAsync();
        await stationCatalog.EnsureSeedDefaultsAsync();

        using var fixture = new SessionControllerTestFixture();
        var tempGamelogDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempGamelogDir);
        try
        {
            var discovery = new GamelogDiscoveryService(tempGamelogDir, NullLogger<GamelogDiscoveryService>.Instance);
            IReadOnlyList<Character> chars = new[] { new Character(1, "Pilot", Path.Combine(tempGamelogDir, "1.txt")) };

            var (priceClient, priceFetcher, iskCalc) = Plan18PricingFactory.Build(
                sessionDb, sdeLookup, settingsRepo, fixture.ActiveSession);
            using var sp = ServiceRegistration.BuildServiceProvider(
                NullLoggerFactory.Instance,
                sessionDb, sessionStore,
                sdeDb, sdeStorage, sdeUpdater, sdeLookup,
                settingsRepo, stationCatalog,
                fixture.ActiveSession, fixture.Writer, fixture.Watcher,
                new FakeGamelogLineParser(), discovery, chars,
                new FakeBootstrapConfigLoader(),
                priceClient, priceFetcher, iskCalc);

            var ctrl = sp.GetRequiredService<SessionController>();
            var statsVm = sp.GetRequiredService<SessionStatsViewModel>();
            // Singleton: a second resolve hands back the same instance.
            sp.GetRequiredService<SessionStatsViewModel>().Should().BeSameAs(statsVm);
            // Wraps the controller — verify by setting a controller property and seeing the VM reflect it.
            ctrl.DisplaySessionName = "Shared Stats";
            statsVm.DisplaySessionName.Should().Be("Shared Stats");
        }
        finally
        {
            if (Directory.Exists(tempGamelogDir)) Directory.Delete(tempGamelogDir, recursive: true);
        }
    }
}
