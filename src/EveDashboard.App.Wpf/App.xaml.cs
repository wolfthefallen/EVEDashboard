namespace EveDashboard.App.Wpf;

using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using EveDashboard.App.Wpf.Bootstrap;
using EveDashboard.App.Wpf.DependencyInjection;
using EveDashboard.App.Wpf.Dialogs;
using EveDashboard.App.Wpf.Layout;
using EveDashboard.App.Wpf.Services;
using EveDashboard.Core.Configuration;
using EveDashboard.Core.Logging;
using EveDashboard.Core.Sessions;
using EveDashboard.Mining.Gamelogs;
using EveDashboard.Mining.Parsing;
using EveDashboard.Mining.Realtime;
using EveDashboard.MarketData.Pricing;
using EveDashboard.Updates;
using EveDashboard.Persistence.Configuration;
using EveDashboard.Persistence.Logging;
using EveDashboard.Persistence.Storage;
using EveDashboard.Sde.Conversion;
using EveDashboard.Sde.Lookup;
using EveDashboard.Sde.Storage;
using EveDashboard.Sde.Updater;
using EveDashboard.Stats.Schema;
using EveDashboard.Stats.Settings;
using EveDashboard.Stats.Storage;
using EveDashboard.Stats.Writer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Interaction logic for App.xaml. Owns the bootstrap chain that wires every
/// prior plan together before the MainWindow is shown.
/// </summary>
/// <remarks>
/// <para>
/// API-drift corrections applied per plan-16 alignment 1.1-1.4 = A:
/// <see cref="SessionEventWriter"/> ctor takes <c>ISdeLookup</c> (1.1);
/// <see cref="SessionEventWriter"/>/<see cref="RealtimeLogWatcher"/> have
/// per-session lifecycle managed by <c>SessionController</c>, NOT
/// started here (1.2); <see cref="GamelogDiscoveryService"/> takes the
/// directory at construction (1.3); <see cref="EverefSdeUpdater"/> takes
/// <see cref="HttpClient"/> + <see cref="EveDashboard.Core.Storage.ISqliteDatabase"/> in its ctor (1.4).
/// </para>
/// <para>
/// The wrapping <c>try</c>/<c>catch</c> is the documented exception to
/// CLAUDE.md's "no blanket-catch to keep the UI alive" rule (per plan-16
/// risk note): the UI is not yet alive at this point, so the catch surfaces
/// unhandled bootstrap failures via the same MessageBox-and-shutdown path as
/// the typed errors instead of a silent crash.
/// </para>
/// <para>
/// Plan 22.2: the chain body is extracted into
/// <see cref="RunBootstrapChainAsync"/> so the bootstrap-failure dialog's
/// Apply-success handler can re-run it after the user fixes the gamelog
/// folder. Each chain step is null-aware via <c>??=</c> against an instance
/// field, so already-constructed singletons (SQLite wrappers, HttpClient,
/// SDE updater + lookup) are reused on retry. The DI container <see cref="_services"/>
/// is rebuilt every call because path-dependent values (<c>discovery</c> and
/// the discovered-characters list) bake into factory closures and the
/// MainWindow singleton.
/// </para>
/// <para>
/// Plan 22.2: <see cref="_bootstrapDialogServices"/> is a separate
/// "mini-container" used only when the bootstrap chain fires a re-route
/// before <see cref="_services"/> is built (failure points fire at chain
/// steps 2 / 9 — chain step 10 is what builds <see cref="_services"/>). It
/// registers the minimum surface the Settings dialog needs in
/// bootstrap-failure mode: <see cref="IBootstrapConfigLoader"/>,
/// <see cref="ILoggerFactory"/>, <see cref="IBootstrapErrorReporter"/>, plus
/// the dialog adapter itself.
/// </para>
/// </remarks>
public partial class App : Application
{
    /// <summary>App-lifetime <see cref="HttpClient"/> for everef.net SDE updates (per plan-16 alignment 1.4 = A).</summary>
    private HttpClient? _httpClient;

    /// <summary>App-lifetime DI container (chain step 10); disposed and rebuilt across plan-22.2 retries; final dispose at <see cref="OnExit"/>.</summary>
    private ServiceProvider? _services;

    /// <summary>Plan 22.2: mini-container for the bootstrap-failure dialog re-route. Built lazily on first failure; cached for subsequent retries; disposed at <see cref="OnExit"/>.</summary>
    private ServiceProvider? _bootstrapDialogServices;

    // Plan 22.2: chain-step singletons promoted from method-locals to
    // instance fields so they survive across RunBootstrapChainAsync calls
    // (already-constructed singletons skipped on retry via ??= or the
    // is-null pattern below).
    private YamlBootstrapConfigLoader? _bootstrapLoader;
    private SessionStore? _sessionStore;
    private SqliteDatabase? _sessionDb;
    private SdeStorage? _sdeStorage;
    private SqliteDatabase? _sdeDb;
    private EverefSdeFormatConverter? _sdeFormatConverter;
    private EverefSdeUpdater? _sdeUpdater;
    private SqliteSdeLookup? _sdeLookup;
    private SqliteSettingsRepository? _settingsRepo;
    private SqliteMarketStationCatalog? _stationCatalog;
    private GamelogLineParser? _parser;
    private ActiveSessionContext? _activeSessionContext;
    private SessionEventWriter? _sessionEventWriter;
    private RealtimeLogWatcher? _watcher;

    // Plan 18 — pricing pipeline. Constructed in chain step 8.5; the existing
    // _httpClient (chain step 5) is reused.
    private GoonMetricsPriceClient? _marketPriceClient;
    private MarketPriceFetcher?     _marketPriceFetcher;
    private IskCalculator?          _iskCalculator;

    // Plan 41 — launch update check. The release client reuses the app-lifetime
    // _httpClient; the fire-and-forget notify task is rooted in _updateCheckTask
    // so it is not collected before it completes.
    private GitHubReleaseClient? _releaseClient;
    private Task?                _updateCheckTask;

    /// <summary>
    /// Application startup. Configures logging, then runs the bootstrap chain
    /// via <see cref="RunBootstrapChainAsync"/>. On any unexpected error,
    /// surfaces the failure via <see cref="BootstrapErrorReporter.ReportAndShutdown"/>.
    /// </summary>
    /// <param name="e">The startup event arguments.</param>
    protected override async void OnStartup(StartupEventArgs e)
    {
        // 0. Plan 39: one-time data-folder rename (EveMiningDashboard -> EveDashboard).
        //    MUST run BEFORE LoggingSetup.Configure (which creates the new folder) —
        //    otherwise the move is skipped and the old data is stranded. Logging is
        //    not up yet, so a failure surfaces via a plain MessageBox + shutdown; the
        //    user's data stays safe at the old path.
        try
        {
            EveDashboard.Core.Storage.AppDataFolderMigration.MigrateIfNeeded();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "EVE Dashboard could not move your data folder to its new location.\n\n" +
                "Your data is safe at %APPDATA%\\EveMiningDashboard. Please restore/contact support before reopening.\n\n" +
                "Details: " + ex.Message,
                "Data folder update failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // 1. Configure logging (plan 02 + plan 21). Must run first so subsequent failures get logged.
        //    Default-overload resolves %APPDATA%\EveDashboard\logs and creates the folder.
        LoggingSetup.Configure();
        var logger = LogProvider.Factory.CreateLogger<App>();
        logger.LogInformation("EVE Dashboard starting up.");

        // Plan 22.2 Bug 2 fix (2026-05-03): merge default Dark theme dictionary
        // into Application.Resources NOW so any UI shown before chain step 10
        // — notably the bootstrap-failure dialog — has theme brushes to bind
        // against. Without this, DynamicResource lookups for BackgroundBrush /
        // ForegroundBrush / HoverOverlayBrush / etc. return null and the
        // dialog renders as a solid black rectangle. Chain step 10's
        // ThemeService.LoadInitialAsync later replaces this with the user-
        // persisted theme via the existing dedup-by-URI logic in
        // ThemeService.ApplyResourceDictionary. Idempotent.
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Themes/Dark.xaml", UriKind.Relative),
        });

        try
        {
            await RunBootstrapChainAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bootstrap failed unexpectedly.");
            BootstrapErrorReporter.ReportAndShutdown(
                "Startup failed",
                "An unexpected error occurred during startup:\n\n" + ex.Message + "\n\n" +
                "Check the log file at %APPDATA%\\EveDashboard\\logs\\EveDashboard.log.");
            return;
        }

        base.OnStartup(e);
    }

    /// <summary>
    /// Plan 22.2: the bootstrap chain itself. Called once from <see cref="OnStartup"/>;
    /// called again from the bootstrap-failure dialog's Apply-success handler
    /// after the user fixes the gamelog folder. Each step is null-aware so
    /// already-constructed singletons are reused.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Internal accessibility (plan 22.2 decision 22.V) so the dialog
    /// code-behind can re-invoke via <c>(App)Application.Current</c>, and
    /// so <c>EveDashboard.App.Wpf.Tests</c> can drive it directly via
    /// <c>InternalsVisibleTo</c>.
    /// </remarks>
    internal async Task RunBootstrapChainAsync(CancellationToken ct = default)
    {
        var logger = LogProvider.Factory.CreateLogger<App>();

        // 2. Load YAML bootstrap config (plan 15). Resolves gamelog_path or fails.
        _bootstrapLoader ??= new YamlBootstrapConfigLoader(
            LogProvider.Factory.CreateLogger<YamlBootstrapConfigLoader>());

        BootstrapConfigResult bootstrapResult;
        try
        {
            bootstrapResult = await _bootstrapLoader.LoadAsync(ct).ConfigureAwait(true);
        }
        catch (BootstrapConfigException bex)
        {
            ShowBootstrapFailureDialog(new BootstrapFailureContext(
                BootstrapFailureKind.ConfigException,
                "Your config.yaml is malformed:\n\n" + bex.Message + "\n\n" +
                "Edit the gamelog folder below and click Apply to retry."));
            return;
        }

        string gamelogPath;
        switch (bootstrapResult)
        {
            case BootstrapConfigResult.Loaded l:
                gamelogPath = l.GamelogPath;
                break;
            case BootstrapConfigResult.AutodetectUsed a:
                gamelogPath = a.GamelogPath;
                logger.LogInformation("Bootstrap autodetected gamelog path: {Path}", gamelogPath);
                break;
            case BootstrapConfigResult.AutodetectFailed f:
                ShowBootstrapFailureDialog(new BootstrapFailureContext(
                    BootstrapFailureKind.AutodetectFailed,
                    "Cannot find your EVE gamelog folder. Looked at:\n\n" + f.AttemptedPath + "\n\n" +
                    "but it does not exist on disk. Set the gamelog folder below and click Apply to retry."));
                return;
            default:
                throw new InvalidOperationException("Unhandled BootstrapConfigResult subtype.");
        }

        // 3. Session SQLite bootstrap (plan 11). Caller owns parent-dir creation.
        if (_sessionDb is null)
        {
            _sessionStore = new SessionStore(LogProvider.Factory.CreateLogger<SessionStore>());
            var sessionParent = Path.GetDirectoryName(_sessionStore.DatabasePath);
            if (!string.IsNullOrEmpty(sessionParent))
            {
                Directory.CreateDirectory(sessionParent);
            }
            _sessionDb = new SqliteDatabase(
                "Data Source=" + _sessionStore.DatabasePath,
                LogProvider.Factory.CreateLogger<SqliteDatabase>());

            // Plan 38: one-time in-place migration of a pre-sha256 database.
            // Backed up + announced + aborted-on-failure so a tester's real data
            // is never silently corrupted. Runs BEFORE EnsureSchemaAsync (which
            // then sees the migrated tables and only (re)creates indexes).
            if (await LegacySessionIdMigration.IsLegacyAsync(_sessionDb, ct).ConfigureAwait(true))
            {
                try
                {
                    await _sessionDb.ExecuteScriptAsync("PRAGMA wal_checkpoint(TRUNCATE);", ct).ConfigureAwait(true);
                    var backupPath = _sessionStore.DatabasePath + ".pre-sha256-" +
                        DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ".bak";
                    File.Copy(_sessionStore.DatabasePath, backupPath, overwrite: false);
                    logger.LogInformation(
                        "Session DB is legacy; backed up to {BackupPath} before the sha256 migration.", backupPath);

                    MessageBox.Show(
                        "EVE Dashboard is updating your local database to a new format. " +
                        "A backup was saved next to your data. This happens once and may take a few seconds.",
                        "Updating local database",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    await LegacySessionIdMigration.MigrateAsync(_sessionDb, ct).ConfigureAwait(true);
                    logger.LogInformation("Session DB sha256 migration complete.");
                }
                catch (Exception ex)
                {
                    // Top-level safety: any migration failure must abort startup so
                    // the user can restore the .bak rather than run on a half-built DB.
                    logger.LogError(ex, "Session DB sha256 migration failed; aborting startup.");
                    BootstrapErrorReporter.ReportAndShutdown(
                        "Database update failed",
                        "EVE Dashboard could not update your local database to the new format.\n\n" +
                        "Your original data was backed up next to it (a .pre-sha256-*.bak file). " +
                        "Please restore that backup before reopening.\n\nDetails: " + ex.Message);
                    return;
                }
            }

            await _sessionStore.EnsureSchemaAsync(_sessionDb, ct).ConfigureAwait(true);
        }

        // 4. SDE storage (plan 07). Extracts the embedded fallback if needed.
        if (_sdeDb is null)
        {
            _sdeStorage = new SdeStorage(LogProvider.Factory.CreateLogger<SdeStorage>());
            await _sdeStorage.EnsureDatabaseExistsAsync(ct).ConfigureAwait(true);
            _sdeDb = new SqliteDatabase(
                "Data Source=" + _sdeStorage.DatabasePath,
                LogProvider.Factory.CreateLogger<SqliteDatabase>());

            // Forward-migrate the live SDE store in place (idempotent). The fallback
            // is only extracted when the file is ABSENT, so an upgrading user keeps
            // their existing eve.sqlite; without this, a store predating the
            // meta.last_modified / meta.etag columns would throw "no such column" on
            // the updater's conditional-GET read (EverefSdeUpdater step 1) and lose
            // SDE auto-update permanently. Fully-qualified to disambiguate from
            // EveDashboard.Stats.Schema.SchemaInitializer (both namespaces imported).
            await EveDashboard.Sde.Schema.SchemaInitializer
                .EnsureSchemaAsync(_sdeDb, ct).ConfigureAwait(true);
        }

        // 5. SDE updater (plan 08). API-drift fix per 1.4 = A: pass HttpClient + sdeDb in ctor.
        //    Result is non-fatal — Unreachable just logs Warning and continues with the local SDE.
        if (_sdeUpdater is null)
        {
            _httpClient ??= new HttpClient();
            _sdeFormatConverter = new EverefSdeFormatConverter(
                LogProvider.Factory.CreateLogger<EverefSdeFormatConverter>());
            _sdeUpdater = new EverefSdeUpdater(
                _httpClient, _sdeStorage!, _sdeDb!, _sdeFormatConverter,
                LogProvider.Factory.CreateLogger<EverefSdeUpdater>());
            try
            {
                var updateResult = await _sdeUpdater.CheckAndUpdateAsync(ct).ConfigureAwait(true);
                if (updateResult is SdeUpdateResult.Unreachable u)
                {
                    logger.LogWarning(
                        "SDE updater unreachable; using shipped fallback or last-good local data. Reason: {Reason}",
                        u.Reason);
                }
            }
            catch (Exception updateEx)
            {
                logger.LogWarning(updateEx, "SDE updater threw unexpectedly; continuing with local SDE.");
            }
        }

        // 6. SDE lookup (plan 09). Loads ore + gas into the in-memory cache.
        //    The Updated subscription happens once-only here — ??= on _sdeLookup
        //    skips the entire block on retries, preventing double-subscription.
        if (_sdeLookup is null)
        {
            _sdeLookup = new SqliteSdeLookup(_sdeDb!, LogProvider.Factory.CreateLogger<SqliteSdeLookup>());
            await _sdeLookup.LoadAsync(ct).ConfigureAwait(true);
            var lookup = _sdeLookup;
            _sdeUpdater!.Updated += async (_, _) =>
            {
                try { await lookup.LoadAsync().ConfigureAwait(false); }
                catch (Exception ex) { logger.LogWarning(ex, "SDE lookup reload after Updated event failed."); }
            };
        }

        // 7. Settings repo + market-stations catalog (plan 14). Seed on first launch.
        if (_settingsRepo is null)
        {
            _settingsRepo = new SqliteSettingsRepository(_sessionDb!,
                LogProvider.Factory.CreateLogger<SqliteSettingsRepository>());
            _stationCatalog = new SqliteMarketStationCatalog(_sessionDb!,
                LogProvider.Factory.CreateLogger<SqliteMarketStationCatalog>());
            await _settingsRepo.EnsureSeedDefaultsAsync(ct).ConfigureAwait(true);
            await _stationCatalog.EnsureSeedDefaultsAsync(ct).ConfigureAwait(true);
        }

        // 8. Realtime watcher + writer + active-session context (plan 12).
        //    Constructed here, NOT started — per plan-16 alignment 1.2 = A
        //    the SessionController manages the per-session lifecycle.
        //    API-drift fix per 1.1 = A: writer ctor takes ISdeLookup.
        if (_watcher is null)
        {
            _parser = new GamelogLineParser(_sdeLookup!,
                LogProvider.Factory.CreateLogger<GamelogLineParser>());
            _activeSessionContext = new ActiveSessionContext();
            _sessionEventWriter = new SessionEventWriter(
                _sessionDb!, _sdeLookup!, _activeSessionContext,
                LogProvider.Factory.CreateLogger<SessionEventWriter>());
            _watcher = new RealtimeLogWatcher(_parser, _sessionEventWriter,
                LogProvider.Factory.CreateLogger<RealtimeLogWatcher>());
        }

        // 8.5. Pricing services (plan 18). Constructed AFTER step 8 because
        //      MarketPriceFetcher's ctor subscribes to IActiveSessionContext.
        //      Constructed BEFORE step 10 because BuildServiceProvider takes
        //      them as parameters. _httpClient was already created at chain
        //      step 5 for the SDE updater; reused here.
        if (_marketPriceFetcher is null)
        {
            _httpClient ??= new HttpClient();
            _marketPriceClient = new GoonMetricsPriceClient(
                _httpClient,
                LogProvider.Factory.CreateLogger<GoonMetricsPriceClient>());
            _marketPriceFetcher = new MarketPriceFetcher(
                _marketPriceClient,
                _sessionDb!,
                _sdeLookup!,
                _settingsRepo!,
                _activeSessionContext!,
                LogProvider.Factory.CreateLogger<MarketPriceFetcher>());
            _iskCalculator = new IskCalculator(
                _marketPriceFetcher,
                _sessionDb!,
                _sdeLookup!,
                LogProvider.Factory.CreateLogger<IskCalculator>());
        }

        // 8.6. Plan 41 — GitHub release-check client for the launch update
        //      check. Reuses the app-lifetime _httpClient created at chain
        //      step 5 (guaranteed non-null after step 8.5).
        _releaseClient ??= new GitHubReleaseClient(
            _httpClient!,
            LogProvider.Factory.CreateLogger<GitHubReleaseClient>());

        // 9. Validate gamelog_path. API-drift fix per 1.3 = A: discovery ctor
        //    takes the directory; DiscoverCharactersAsync takes only a CT.
        //    Path-dependent: rebuilt every call so a retry uses the new path.
        var discovery = new GamelogDiscoveryService(
            gamelogPath,
            LogProvider.Factory.CreateLogger<GamelogDiscoveryService>());
        var characters = await discovery.DiscoverCharactersAsync(ct).ConfigureAwait(true);
        if (characters.Count == 0)
        {
            ShowBootstrapFailureDialog(new BootstrapFailureContext(
                BootstrapFailureKind.ZeroCharacters,
                "No EVE characters detected at:\n\n" + gamelogPath + "\n\n" +
                "Make sure EVE has run at least once and produced gamelogs. Set the gamelog folder below and click Apply to retry."));
            return;
        }
        logger.LogInformation("Discovered {Count} EVE characters at startup.", characters.Count);

        // 10. Build IServiceProvider, theme load, MainWindow open.
        //     On retry: dispose the prior container before reassigning, and
        //     clear MainWindow first so Application.Current.MainWindow does not
        //     reference a now-disposed instance during teardown.
        if (_services is not null)
        {
            MainWindow = null;
            _services.Dispose();
            _services = null;
        }

        _services = ServiceRegistration.BuildServiceProvider(
            LogProvider.Factory,
            _sessionDb!, _sessionStore!,
            _sdeDb!, _sdeStorage!, _sdeUpdater!, _sdeLookup!,
            _settingsRepo!, _stationCatalog!,
            _activeSessionContext!, _sessionEventWriter!, _watcher!,
            _parser!, discovery,
            characters,
            _bootstrapLoader!,
            _marketPriceClient!, _marketPriceFetcher!, _iskCalculator!,
            _releaseClient!);

        var themeService = _services.GetRequiredService<ThemeService>();
        await themeService.LoadInitialAsync().ConfigureAwait(true);

        // Plan 19 — load persisted dashboard layout BEFORE MainWindow.Show so the
        // first paint reflects the user's saved order + hidden state. Must run AFTER
        // BuildServiceProvider (the coordinator is constructed inside DI) and AFTER
        // ThemeService.LoadInitialAsync (so theme brushes are available before the
        // first event might fire).
        var layoutCoordinator = _services.GetRequiredService<LayoutCoordinator>();
        await layoutCoordinator.LoadAsync(ct).ConfigureAwait(true);

        var mainWindow = _services.GetRequiredService<MainWindow>();
        await mainWindow.InitializeAsync().ConfigureAwait(true);
        MainWindow = mainWindow;
        mainWindow.Show();

        logger.LogInformation("EVE Dashboard startup complete.");

        // Plan 41 — fire-and-forget launch update check. Runs AFTER Show() so it
        // never delays startup; UpdateNotifier swallows + logs all failures.
        // Rooted in _updateCheckTask so the task is not collected mid-flight.
        var runningVersion = Assembly.GetEntryAssembly()?.GetName().Version;
        if (runningVersion is not null)
        {
            _updateCheckTask = _services.GetRequiredService<UpdateNotifier>()
                .NotifyIfUpdateAvailableAsync(runningVersion);
        }
        else
        {
            logger.LogWarning("Could not determine the running assembly version; skipping the update check.");
        }
    }

    /// <summary>
    /// Plan 22.2: build the bootstrap-failure mini-container on first call,
    /// then resolve and show the Settings dialog with the supplied failure
    /// context. The dialog blocks; on Apply success its code-behind re-invokes
    /// <see cref="RunBootstrapChainAsync"/>; on Cancel/Close the dialog itself
    /// calls <see cref="Application.Shutdown(int)"/>.
    /// </summary>
    /// <param name="failure">Which OnStartup branch fired the re-route, plus the human-readable banner text.</param>
    private void ShowBootstrapFailureDialog(BootstrapFailureContext failure)
    {
        if (_bootstrapDialogServices is null)
        {
            var sc = new ServiceCollection();
            sc.AddSingleton<ILoggerFactory>(LogProvider.Factory);
            sc.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
            sc.AddSingleton<IBootstrapConfigLoader>(_bootstrapLoader!);
            sc.AddSingleton<IBootstrapErrorReporter, BootstrapErrorReporterAdapter>();
            sc.AddSingleton<ISettingsDialog, SettingsDialogAdapter>();
            _bootstrapDialogServices = sc.BuildServiceProvider();
        }

        var dialog = _bootstrapDialogServices.GetRequiredService<ISettingsDialog>();
        dialog.Show(failure);
    }

    /// <summary>
    /// Application shutdown. Async-disposes the DI container so
    /// <c>IAsyncDisposable</c> singletons (the watcher, writer, and SQLite
    /// wrappers) clean up their async state before the process exits.
    /// </summary>
    /// <param name="e">Exit event arguments.</param>
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // Dispose the DI containers synchronously here — WPF's OnExit is
            // synchronous. ServiceProvider.Dispose runs IDisposable; for
            // IAsyncDisposable services, Microsoft.Extensions.DependencyInjection
            // calls .GetAwaiter().GetResult() on DisposeAsync internally, which
            // is the documented sync-shutdown path for desktop apps.
            _services?.Dispose();
            _bootstrapDialogServices?.Dispose();
            _httpClient?.Dispose();
        }
        catch (Exception ex)
        {
            // Last-chance log; the logger may already be flushed.
            try { LogProvider.Factory.CreateLogger<App>().LogWarning(ex, "OnExit cleanup failed."); }
            catch { /* swallow — process is exiting */ }
        }

        base.OnExit(e);
    }
}
