namespace EveDashboard.App.Wpf.DependencyInjection;

using EveDashboard.Stats.Historical;
using EveDashboard.Mining.Historical;
using EveDashboard.App.Wpf.About;
using EveDashboard.App.Wpf.Bootstrap;
using EveDashboard.App.Wpf.Dialogs;
using EveDashboard.App.Wpf.Geometry;
using EveDashboard.App.Wpf.Layout;
using EveDashboard.App.Wpf.Reporting;
using EveDashboard.App.Wpf.Reporting.HaulValue;
using EveDashboard.App.Wpf.Reporting.Lifetime;
using EveDashboard.App.Wpf.Reporting.Widgets;
using EveDashboard.App.Wpf.Services;
using EveDashboard.App.Wpf.Widgets.Pilot;
using EveDashboard.App.Wpf.Widgets.Session;
// AutoPauseSoundPlayer + ThemeService both live in EveDashboard.App.Wpf.Services
using EveDashboard.Core.Configuration;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Pricing;
using EveDashboard.Core.Realtime;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings;
using EveDashboard.Core.Storage;
using EveDashboard.Mining.Gamelogs;
using EveDashboard.Mining.Parsing;
using EveDashboard.Mining.Realtime;
using EveDashboard.MarketData.Pricing;
using EveDashboard.Core.Updates;
using EveDashboard.Updates;
using EveDashboard.Reporting.Queries;
using EveDashboard.Sde.Lookup;
using EveDashboard.Sde.Meta;
using EveDashboard.Sde.Storage;
using EveDashboard.Sde.Updater;
using EveDashboard.SessionIO.Excel;
using EveDashboard.SessionIO.Json;
using EveDashboard.SessionIO.Tables;
using EveDashboard.Stats.Events;
using EveDashboard.Stats.Fleets;
using EveDashboard.Stats.Queries;
using EveDashboard.Stats.Storage;
using EveDashboard.Stats.Writer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Builds the DI container for the WPF app. Called once from
/// <c>App.xaml.cs OnStartup</c> after all bootstrap-chain singletons have
/// been constructed by hand.
/// </summary>
/// <remarks>
/// Uses stock <c>Microsoft.Extensions.DependencyInjection</c> (transitive via
/// MEL); no third-party container. All singletons constructed in the bootstrap
/// chain are passed in by the caller; this builder just registers them and adds the
/// app-only services (<see cref="ThemeService"/>, <see cref="WindowGeometryService"/>,
/// <see cref="IConfirmDialog"/>, <see cref="ISettingsDialog"/> + <see cref="IAddStationDialog"/>,
/// <see cref="SessionController"/>, <see cref="MainWindow"/>).
/// </remarks>
public static class ServiceRegistration
{
    /// <summary>
    /// Build the <see cref="IServiceProvider"/>. All bootstrap-chain singletons
    /// must already be constructed and ready; this method just composes them.
    /// </summary>
    /// <param name="loggerFactory">Active logger factory (after <c>LoggingSetup.Configure</c>).</param>
    /// <param name="sessionDb">Open session SQLite wrapper.</param>
    /// <param name="sessionStore">Session-store (path + schema-init facade).</param>
    /// <param name="sdeDb">Open SDE SQLite wrapper.</param>
    /// <param name="sdeStorage">SDE storage manager.</param>
    /// <param name="sdeUpdater">SDE updater (already invoked once at startup).</param>
    /// <param name="sdeLookup">In-memory ore+gas lookup, already loaded.</param>
    /// <param name="settingsRepo">Settings repository.</param>
    /// <param name="stationCatalog">Market-stations catalog.</param>
    /// <param name="activeSession">Mutable active-session context.</param>
    /// <param name="writer">Session-event writer (constructed but not yet started).</param>
    /// <param name="watcher">Realtime log watcher (constructed but not yet started).</param>
    /// <param name="parser">Gamelog line parser.</param>
    /// <param name="discovery">Gamelog discovery service (already invoked at startup; registered for future re-discovery).</param>
    /// <param name="allCharacters">Characters discovered at startup. Drives the Session widget's pilot list.</param>
    /// <param name="bootstrapLoader">YAML bootstrap config loader. The Settings dialog VM consumes it for <c>gamelog_path</c> read/write.</param>
    /// <param name="priceClient">Price-source router (GoonMetrics vs official ESI, per the <c>price_source</c> setting); registered as the <see cref="IMarketPriceClient"/>.</param>
    /// <param name="esiPriceClient">The official-ESI price client, used directly by the Haul Value calculator (GoonMetrics 404s the non-Jita empire hubs, so the multi-hub comparison must use ESI regardless of the <c>price_source</c> setting).</param>
    /// <param name="marketPriceFetcher">Market-price fetcher orchestrator (subscribes to <see cref="IActiveSessionContext.ActiveSessionChanged"/> in its ctor).</param>
    /// <param name="iskCalculator">ISK calculator (canonical recursive reprocessing algorithm).</param>
    /// <param name="releaseClient">GitHub release-check client for the launch update check.</param>
    /// <returns>A constructed <see cref="ServiceProvider"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public static ServiceProvider BuildServiceProvider(
        ILoggerFactory loggerFactory,
        ISqliteDatabase sessionDb,
        ISessionStore sessionStore,
        ISqliteDatabase sdeDb,
        ISdeStorage sdeStorage,
        ISdeUpdater sdeUpdater,
        ISdeLookup sdeLookup,
        ISettingsRepository settingsRepo,
        IMarketStationCatalog stationCatalog,
        ActiveSessionContext activeSession,
        SessionEventWriter writer,
        RealtimeLogWatcher watcher,
        IGamelogLineParser parser,
        GamelogDiscoveryService discovery,
        IReadOnlyList<Character> allCharacters,
        IBootstrapConfigLoader bootstrapLoader,
        IMarketPriceClient priceClient,
        EsiMarketPriceClient esiPriceClient,
        MarketPriceFetcher marketPriceFetcher,
        IskCalculator iskCalculator,
        GitHubReleaseClient releaseClient)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(sessionDb);
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(sdeDb);
        ArgumentNullException.ThrowIfNull(sdeStorage);
        ArgumentNullException.ThrowIfNull(sdeUpdater);
        ArgumentNullException.ThrowIfNull(sdeLookup);
        ArgumentNullException.ThrowIfNull(settingsRepo);
        ArgumentNullException.ThrowIfNull(stationCatalog);
        ArgumentNullException.ThrowIfNull(activeSession);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(watcher);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(allCharacters);
        ArgumentNullException.ThrowIfNull(bootstrapLoader);
        ArgumentNullException.ThrowIfNull(priceClient);
        ArgumentNullException.ThrowIfNull(esiPriceClient);
        ArgumentNullException.ThrowIfNull(marketPriceFetcher);
        ArgumentNullException.ThrowIfNull(iskCalculator);
        ArgumentNullException.ThrowIfNull(releaseClient);

        var services = new ServiceCollection();

        // Logging — bridge the already-configured factory into MEL DI.
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        // Bootstrap-chain singletons.
        // NOTE: sessionDb and sdeDb are deliberately NOT registered as
        // ISqliteDatabase. Both are typed ISqliteDatabase, so a generic
        // AddSingleton would overwrite under one service-key (last-write-wins);
        // the result is whichever one DI returns is non-deterministic from a
        // reader's perspective and was the root cause of "no such table:
        // session_time" at first run. Consumers that need a specific db are
        // resolved via factory lambdas that explicitly capture the right one.
        services.AddSingleton(sessionStore);
        services.AddSingleton(sdeStorage);
        services.AddSingleton(sdeUpdater);
        services.AddSingleton(sdeLookup);
        // SDE meta reader for the About window's "SDE data" line.
        // Explicit sdeDb capture (NOT via ISqliteDatabase resolution), same
        // reasoning as the query layers: sdeDb is the SDE connection, not session.
        services.AddSingleton<ISdeMetaReader>(sp => new SdeMetaReader(
            sdeDb, sp.GetRequiredService<ILogger<SdeMetaReader>>()));
        services.AddSingleton(settingsRepo);
        services.AddSingleton(stationCatalog);
        services.AddSingleton(activeSession);
        services.AddSingleton<IActiveSessionContext>(activeSession);
        services.AddSingleton(writer);
        services.AddSingleton<ISessionEventNotifier>(writer);
        services.AddSingleton<IMiningEventSink>(writer);
        services.AddSingleton(watcher);
        services.AddSingleton(parser);
        services.AddSingleton(discovery);
        services.AddSingleton(allCharacters);
        services.AddSingleton(bootstrapLoader);

        // Pricing pipeline singletons. Mirror the existing
        // dual-registration pattern (concrete + interface) used for the
        // SessionEventWriter at lines above.
        services.AddSingleton<IMarketPriceClient>(priceClient);
        services.AddSingleton(marketPriceFetcher);
        services.AddSingleton<IMarketPriceFetcher>(marketPriceFetcher);
        services.AddSingleton(iskCalculator);
        services.AddSingleton<IIskCalculator>(iskCalculator);

        // Launch update check. Release client dual-registered
        // (concrete + interface), mirroring the pricing client; the browser
        // launcher and notifier are app-only services. UpdateNotifier is
        // resolved once after MainWindow.Show and run fire-and-forget.
        services.AddSingleton(releaseClient);
        services.AddSingleton<IReleaseClient>(releaseClient);
        services.AddSingleton<IBrowserLauncher, BrowserLauncher>();
        services.AddSingleton<UpdateNotifier>();

        // Fleet repository (explicit sessionDb capture, same pattern as
        // the stats repo). Owns the fleet / fleet_pilot tables; the New Session
        // dialog VM resolves it for load / create / delete.
        services.AddSingleton<IFleetRepository>(sp =>
            new SqliteFleetRepository(
                sessionDb,
                sp.GetRequiredService<ILogger<SqliteFleetRepository>>()));

        // Stats query layer — explicit sessionDb capture (NOT via ISqliteDatabase resolution).
        services.AddSingleton<ISessionStatsRepository>(sp =>
            new SqliteSessionStatsRepository(
                sessionDb,
                iskCalculator,
                settingsRepo,
                sdeLookup,
                sp.GetRequiredService<ILogger<SqliteSessionStatsRepository>>()));

        // Reporting query layer (uses the same explicit sessionDb capture pattern).
        services.AddSingleton<IReportingQueries>(sp =>
            new SqliteReportingQueries(
                sessionDb,
                sp.GetRequiredService<ISessionStatsRepository>(),
                sdeLookup,
                iskCalculator,
                settingsRepo,
                sp.GetRequiredService<ILogger<SqliteReportingQueries>>()));

        // App-only services.
        services.AddSingleton<ThemeService>();
        // Auto-pause / auto-resume sound player. Singleton — loads both WAVs
        // once at construction and holds the byte buffers for the app
        // lifetime. MainWindow's ctor subscribes the controller's
        // sound-request events to PlayPauseSound / PlayResumeSound.
        services.AddSingleton<AutoPauseSoundPlayer>();
        services.AddSingleton<IConfirmDialog, ConfirmDialogAdapter>();
        // WindowGeometryService is deliberately not auto-registered. Each
        // managed Window (MainWindow, Reports, Crystal Diagram, Lifetime
        // Totals, About) gets its own instance via its factory below, wired to
        // different ISettingsRepository typed methods and default sizes.

        // Bootstrap-error reporter test seam. Adapter delegates to
        // the existing static helper for production; unit tests substitute a
        // fake to assert the second-consecutive-SaveAsync-failure fallback.
        services.AddSingleton<IBootstrapErrorReporter, BootstrapErrorReporterAdapter>();

        // Settings UI. Adapter is singleton; the dialog Window and its
        // ViewModel are transient so each Show() call gets a fresh instance
        // with no carry-over state. SettingsDialogAdapter resolves
        // SettingsDialog from IServiceProvider; the AddStationDialogAdapter
        // does the same for the sub-dialog.
        services.AddSingleton<ISettingsDialog, SettingsDialogAdapter>();
        services.AddTransient<SettingsDialog>();
        services.AddTransient<SettingsDialogViewModel>();
        services.AddSingleton<IAddStationDialog, AddStationDialogAdapter>();
        services.AddTransient<AddStationDialog>();
        services.AddTransient<AddStationDialogViewModel>();

        // SessionIO. Reader / Writer / Exporters / Importer all need
        // sessionDb, which is NOT registered as ISqliteDatabase (see the
        // dual-DB note above). Use factory lambdas with explicit capture.
        services.AddSingleton(sp => new SessionTableReader(
            sessionDb,
            sp.GetRequiredService<ILogger<SessionTableReader>>()));
        services.AddSingleton(sp => new SessionTableWriter(
            sessionDb,
            sp.GetRequiredService<ILogger<SessionTableWriter>>()));
        services.AddSingleton(sp => new SessionTableDeleter(
            sessionDb,
            sp.GetRequiredService<ILogger<SessionTableDeleter>>()));
        services.AddSingleton(sp => new JsonSessionExporter(
            sp.GetRequiredService<SessionTableReader>(),
            sp.GetRequiredService<ILogger<JsonSessionExporter>>()));
        services.AddSingleton(sp => new JsonSessionImporter(
            sp.GetRequiredService<SessionTableWriter>(),
            sp.GetRequiredService<ILogger<JsonSessionImporter>>()));
        services.AddSingleton(sp => new ExcelSessionExporter(
            sp.GetRequiredService<SessionTableReader>(),
            sp.GetRequiredService<IReportingQueries>(),
            sp.GetRequiredService<ISessionStatsRepository>(),
            sp.GetRequiredService<ILogger<ExcelSessionExporter>>()));
        services.AddSingleton<IReportsFileDialog, ReportsFileDialogAdapter>();

        // Session controller — explicit sessionDb capture; the rest via DI.
        // This is the singleton that drives both the SessionStats thin VM and
        // the New Session dialog VM (registered just below).
        services.AddSingleton(sp => new SessionController(
            db:            sessionDb,
            statsRepo:     sp.GetRequiredService<ISessionStatsRepository>(),
            notifier:      sp.GetRequiredService<ISessionEventNotifier>(),
            writer:        sp.GetRequiredService<SessionEventWriter>(),
            watcher:       sp.GetRequiredService<RealtimeLogWatcher>(),
            activeSession: sp.GetRequiredService<ActiveSessionContext>(),
            confirmStop:   sp.GetRequiredService<IConfirmDialog>(),
            discovery:     sp.GetRequiredService<GamelogDiscoveryService>(),
            allCharacters: sp.GetRequiredService<IReadOnlyList<Character>>(),
            settings:      sp.GetRequiredService<ISettingsRepository>(),
            log:           sp.GetRequiredService<ILogger<SessionController>>()));

        // Thin VM for the SessionStats widget. Wraps the SessionController
        // singleton and forwards bound fields. Session configuration is not a
        // widget — it lives in the modal New Session dialog (see the
        // ISessionConfigDialog registration below).
        services.AddSingleton(sp => new SessionStatsViewModel(
            sp.GetRequiredService<SessionController>(),
            sp.GetRequiredService<ISettingsRepository>(),
            sp.GetRequiredService<ILogger<SessionStatsViewModel>>()));

        // New Session dialog. VM is singleton (wraps the
        // singleton SessionController; no dialog-instance state). Window is
        // transient — fresh instance per Show() call.
        services.AddSingleton<SessionConfigDialogViewModel>();
        services.AddTransient<SessionConfigDialog>();
        services.AddSingleton<ISessionConfigDialog, SessionConfigDialogAdapter>();

        // Historical sessions (build from a date range / combine saved sessions).
        // Explicit sessionDb capture like every other session-store service; the
        // gamelog reader shares the discovery service's folder and the live parser.
        services.AddSingleton<IHistoricalGamelogReader>(sp => new HistoricalGamelogReader(
            discovery.GamelogDirectory,
            parser,
            sp.GetRequiredService<ILogger<HistoricalGamelogReader>>()));
        services.AddSingleton<ISessionSpanQuery>(sp => new SqliteSessionSpanQuery(
            sessionDb,
            sp.GetRequiredService<ILogger<SqliteSessionSpanQuery>>()));
        services.AddSingleton<IHistoricalSessionBuilder>(sp => new HistoricalSessionBuilder(
            sessionDb,
            sdeLookup,
            sp.GetRequiredService<ILogger<HistoricalSessionBuilder>>()));
        services.AddSingleton<IHistoricalMarketPricer>(sp => new HistoricalMarketPricer(
            sessionDb,
            sdeLookup,
            settingsRepo,
            marketPriceFetcher,
            sp.GetRequiredService<ILogger<HistoricalMarketPricer>>()));
        services.AddSingleton<HistoricalSessionDialogViewModel>();
        services.AddTransient<HistoricalSessionDialog>();
        services.AddSingleton<IHistoricalSessionDialog, HistoricalSessionDialogAdapter>();

        // Per-pilot widget coordinator — explicit sessionDb capture
        // for the same reason the stats repo and Session VM use one (see the
        // dual-DB note above). Subscribes to events on construction; lives for
        // the app lifetime.
        //
        // The setPilotRoleCallback resolves
        // SessionController LAZILY inside the delegate body — NOT in the factory
        // body. Resolving SessionController eagerly here would create a DI cycle
        // (coordinator -> controller would need to exist before controller's own
        // singleton registration completes). The Func captures `sp` by closure and
        // resolves on first invocation (i.e. the first role-change command).
        services.AddSingleton(sp => new PerPilotWidgetCoordinator(
            activeSession:         sp.GetRequiredService<IActiveSessionContext>(),
            db:                    sessionDb,
            statsRepo:             sp.GetRequiredService<ISessionStatsRepository>(),
            notifier:              sp.GetRequiredService<ISessionEventNotifier>(),
            loggerFactory:         sp.GetRequiredService<ILoggerFactory>(),
            settings:              sp.GetRequiredService<ISettingsRepository>(),
            setPilotRoleCallback:  (pilotId, role, ct) =>
                sp.GetRequiredService<SessionController>().SetPilotRoleAsync(pilotId, role, ct)));

        // Layout coordinator. Owns the unified Items collection,
        // hidden-flag flips, and widget_layout persistence. Subscribes to
        // PerPilotWidgetCoordinator.Widgets.CollectionChanged AND to the
        // SessionController's PropertyChanged on construction (the first
        // Mode -> Running/Paused transition latches the _statsActivated flag,
        // which gates SessionStats descriptor renderability). LoadAsync is
        // invoked by App.OnStartup after BuildServiceProvider.
        services.AddSingleton(sp => new LayoutCoordinator(
            activeSession:     sp.GetRequiredService<IActiveSessionContext>(),
            perPilots:         sp.GetRequiredService<PerPilotWidgetCoordinator>(),
            sessionController: sp.GetRequiredService<SessionController>(),
            sessionStatsVm:    sp.GetRequiredService<SessionStatsViewModel>(),
            settings:          sp.GetRequiredService<ISettingsRepository>(),
            log:               sp.GetRequiredService<ILogger<LayoutCoordinator>>()));

        // MainWindow VM — pure composition, all deps via DI.
        services.AddSingleton<MainWindowViewModel>();

        // Reports — sidebar/content/root VMs (singletons; the Reports window holds
        // them for its lifetime), the saved-data widget VMs (transient — constructed by
        // BreakdownContentViewModel.LoadAsync per session selection), and the Reports
        // window itself (singleton; the adapter holds the instance to preserve geometry
        // across in-session open/close cycles).
        services.AddSingleton<SessionsSidebarViewModel>();
        services.AddSingleton<BreakdownContentViewModel>();

        services.AddSingleton<ReportsWindowViewModel>();
        services.AddSingleton<ReportsWindow>(sp => new ReportsWindow(
            sp.GetRequiredService<ReportsWindowViewModel>(),
            new WindowGeometryService(
                readGeometry:  ct => settingsRepo.GetReportsWindowGeometryAsync(ct),
                writeGeometry: g => settingsRepo.SetReportsWindowGeometryAsync(g),
                defaultWidth:  1100,
                defaultHeight: 700,
                log:           sp.GetRequiredService<ILogger<WindowGeometryService>>()),
            sp.GetRequiredService<ILogger<ReportsWindow>>()));
        services.AddSingleton<IReportsWindow, ReportsWindowAdapter>();

        // Crystal Diagram reference window — singleton + hide-on-close, mirrors
        // the Reports lifetime so geometry + dropdown selection survive in-session
        // open/close cycles. Default 900x650 centered; geometry persisted via the
        // Crystal-flavoured WindowGeometryService; the anomaly selection
        // persisted via the singleton CrystalDiagramWindowViewModel.
        services.AddSingleton<CrystalDiagramWindowViewModel>();
        services.AddSingleton<CrystalDiagramWindow>(sp => new CrystalDiagramWindow(
            sp.GetRequiredService<CrystalDiagramWindowViewModel>(),
            new WindowGeometryService(
                readGeometry:  ct => settingsRepo.GetCrystalDiagramWindowGeometryAsync(ct),
                writeGeometry: g => settingsRepo.SetCrystalDiagramWindowGeometryAsync(g),
                defaultWidth:  900,
                defaultHeight: 650,
                log:           sp.GetRequiredService<ILogger<WindowGeometryService>>()),
            sp.GetRequiredService<ILogger<CrystalDiagramWindow>>()));
        services.AddSingleton<ICrystalDiagramWindow, CrystalDiagramWindowAdapter>();

        // Lifetime Totals window. Singleton + hide-on-close, mirrors
        // Reports/Crystal so geometry survives in-session open/close cycles.
        // Default 700x640 centered; geometry persisted via a Lifetime-flavoured
        // WindowGeometryService. The adapter re-fetches the all-time rollup on
        // every Show (the data is global and can change while hidden).
        services.AddSingleton<LifetimeTotalsWindowViewModel>();
        services.AddSingleton<LifetimeTotalsWindow>(sp => new LifetimeTotalsWindow(
            sp.GetRequiredService<LifetimeTotalsWindowViewModel>(),
            new WindowGeometryService(
                readGeometry:  ct => settingsRepo.GetLifetimeTotalsWindowGeometryAsync(ct),
                writeGeometry: g => settingsRepo.SetLifetimeTotalsWindowGeometryAsync(g),
                defaultWidth:  700,
                defaultHeight: 640,
                log:           sp.GetRequiredService<ILogger<WindowGeometryService>>()),
            sp.GetRequiredService<ILogger<LifetimeTotalsWindow>>()));
        services.AddSingleton<ILifetimeTotalsWindow, LifetimeTotalsWindowAdapter>();

        // About window. Singleton + hide-on-close, mirrors
        // Reports/Crystal/Lifetime so geometry survives in-session open/close
        // cycles. Default 460x560 centered; geometry persisted via an
        // About-flavoured WindowGeometryService. The VM auto-resolves (its deps
        // ISdeMetaReader + ILogger<> are registered above).
        services.AddSingleton<AboutWindowViewModel>();
        services.AddSingleton<AboutWindow>(sp => new AboutWindow(
            sp.GetRequiredService<AboutWindowViewModel>(),
            new WindowGeometryService(
                readGeometry:  ct => settingsRepo.GetAboutWindowGeometryAsync(ct),
                writeGeometry: g => settingsRepo.SetAboutWindowGeometryAsync(g),
                defaultWidth:  460,
                defaultHeight: 560,
                log:           sp.GetRequiredService<ILogger<WindowGeometryService>>()),
            sp.GetRequiredService<IBrowserLauncher>(),
            sp.GetRequiredService<ILogger<AboutWindow>>()));
        services.AddSingleton<IAboutWindow, AboutWindowAdapter>();

        // Haul Value window (public-data multi-hub valuation; no geometry
        // persistence in v1). Its non-window deps: a full-types name resolver (over
        // the SDE db) for the paste box, and the valuation calculator over the
        // official-ESI price client (see the note below).
        services.AddSingleton<ISdeTypeNameResolver>(sp => new SqliteSdeTypeNameResolver(
            sdeDb, sp.GetRequiredService<ILogger<SqliteSdeTypeNameResolver>>()));
        // Haul Value prices via ESI DIRECTLY (not the price_source router): GoonMetrics
        // 404s the non-Jita empire hubs, so a multi-hub comparison must use the source
        // that covers every NPC hub. The live-session ISK pipeline still honours
        // price_source; only this ad-hoc appraisal is pinned to ESI.
        services.AddSingleton<HaulValuationCalculator>(_ => new HaulValuationCalculator(esiPriceClient));
        services.AddSingleton<HaulValueWindowViewModel>();
        services.AddSingleton<HaulValueWindow>(sp => new HaulValueWindow(
            sp.GetRequiredService<HaulValueWindowViewModel>(),
            sp.GetRequiredService<ILogger<HaulValueWindow>>()));
        services.AddSingleton<IHaulValueWindow, HaulValueWindowAdapter>();

        // MainWindow factory captures the MainWindow-flavoured
        // WindowGeometryService (wired to Get/SetWindowGeometryAsync, default
        // 1280x800) inline so the type itself isn't auto-registered.
        services.AddSingleton<MainWindow>(sp => new MainWindow(
            sp.GetRequiredService<MainWindowViewModel>(),
            new WindowGeometryService(
                readGeometry:  ct => settingsRepo.GetWindowGeometryAsync(ct),
                writeGeometry: g => settingsRepo.SetWindowGeometryAsync(g),
                defaultWidth:  1280,
                defaultHeight: 800,
                log:           sp.GetRequiredService<ILogger<WindowGeometryService>>()),
            sp.GetRequiredService<ILogger<MainWindow>>(),
            sp.GetRequiredService<ISettingsDialog>(),
            sp.GetRequiredService<IReportsWindow>(),
            sp.GetRequiredService<ICrystalDiagramWindow>(),
            sp.GetRequiredService<ISessionConfigDialog>(),
            sp.GetRequiredService<AutoPauseSoundPlayer>(),
            sp.GetRequiredService<IAboutWindow>(),
            sp.GetRequiredService<IHaulValueWindow>()));

        return services.BuildServiceProvider();
    }
}
