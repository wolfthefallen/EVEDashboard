namespace EveDashboard.App.Wpf.Dialogs;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EveDashboard.App.Wpf.Bootstrap;
using EveDashboard.Core.Configuration;
using EveDashboard.Core.Settings;
using EveDashboard.Mining.Gamelogs;
using Microsoft.Extensions.Logging;

/// <summary>
/// ViewModel for the Settings dialog (plan 22.1). Owns every editable user
/// setting from plans 14 + 15: theme, refining yield, price mode,
/// selected station, market-stations list, gamelog path. Apply/Close pattern
/// with deferred validation: <see cref="ApplyAsync"/> validates first and only
/// persists when every field passes; on validation failure the caller surfaces
/// the error list and the dialog stays open.
/// </summary>
/// <remarks>
/// <para>
/// Markets pending model (plan 22.1 decision 22.K): the catalog is NOT mutated
/// while the user clicks Add or Remove. Edits accumulate in
/// <c>_pendingAdds</c> / <c>_pendingRemovals</c>; the displayed
/// <see cref="Stations"/> view is the union of <c>persisted - removals + adds</c>.
/// <see cref="ApplyAsync"/> flushes pending mutations to the catalog only after
/// every other validator passes, then re-baselines.
/// </para>
/// <para>
/// Subscriber wiring for <c>refining_yield_percent</c> / <c>price_mode</c> /
/// <c>selected_station_id</c> is intentionally out of scope here (plan 22.1
/// decision 1.2 = C). Plan 18's ISK pipeline owns those subscriptions when its
/// recalc path is real. Theme reapplies live on Apply via the existing
/// <c>ThemeService</c> subscription (plan 16).
/// </para>
/// <para>
/// Bootstrap-failure mode (plan 22.2 decisions 22.O / 22.T / 22.U): when the
/// optional <c>bootstrapFailure</c> ctor parameter is non-null, the VM is in
/// "fix your gamelog path or exit" mode. <see cref="LoadAsync"/> reads only
/// <c>gamelog_path</c> via <see cref="IBootstrapConfigLoader"/>; the Settings /
/// Markets services may be stub instances that throw if called.
/// <see cref="ApplyAsync"/> validates and persists only the path. On success it
/// raises <see cref="BootstrapApplySucceeded"/>; the dialog code-behind re-runs
/// <c>App.RunBootstrapChainAsync</c>. On a second consecutive
/// <see cref="IBootstrapConfigLoader.SaveAsync"/> failure the VM falls back to
/// <see cref="IBootstrapErrorReporter.ReportAndShutdown"/> per decision 2.4 = B.
/// </para>
/// </remarks>
public sealed class SettingsDialogViewModel : INotifyPropertyChanged
{
    /// <summary>Inclusive lower bound of acceptable refining-yield input, in percent.</summary>
    public const double YieldMin = 50.0;

    /// <summary>Inclusive upper bound of acceptable refining-yield input, in percent.</summary>
    public const double YieldMax = 90.6;

    /// <summary>Plan 37 — inclusive lower bound of acceptable idle-minutes input.</summary>
    public const int IdleMinutesMin = 1;

    /// <summary>Plan 37 — inclusive upper bound of acceptable idle-minutes input.</summary>
    public const int IdleMinutesMax = 60;

    private readonly ISettingsRepository _settings;
    private readonly IMarketStationCatalog _stations;
    private readonly IBootstrapConfigLoader _config;
    private readonly IAddStationDialog _addStationDialog;
    private readonly IBootstrapErrorReporter _errorReporter;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SettingsDialogViewModel> _log;

    private readonly BootstrapFailureContext? _failure;
    private int _consecutiveSaveFailures;
    private string _bannerMessage = string.Empty;

    private ThemeMode _theme;
    private PriceMode _priceMode;
    private string _refiningYieldText = string.Empty;
    private long _selectedStationId;
    private string _gamelogPath = string.Empty;
    private string _autoPauseIdleMinutesText = string.Empty;
    private bool _autoPauseSoundEnabled = true;
    private bool _autoShowRefinedMaterials = true;
    private bool _dirty;

    private IReadOnlyList<MarketStation> _persistedStations = Array.Empty<MarketStation>();
    private readonly List<MarketStation> _pendingAdds = new();
    private readonly HashSet<long> _pendingRemovals = new();

    /// <summary>Construct the ViewModel. Required dependencies are DI-resolved; the optional <paramref name="bootstrapFailure"/> selects bootstrap-failure mode.</summary>
    /// <param name="settings">Typed settings repository (plan 14). May be a throwing stub when <paramref name="bootstrapFailure"/> is non-null.</param>
    /// <param name="stations">Market-stations catalog (plan 14). May be a throwing stub when <paramref name="bootstrapFailure"/> is non-null.</param>
    /// <param name="config">Bootstrap config loader (plan 15) for <c>gamelog_path</c> read/write.</param>
    /// <param name="addStationDialog">Test seam for the Add-station sub-dialog. May be a throwing stub when <paramref name="bootstrapFailure"/> is non-null.</param>
    /// <param name="errorReporter">Bootstrap-error reporter test seam (plan 22.2) — used as the second-consecutive-save-failure fallback per decision 2.4 = B.</param>
    /// <param name="loggerFactory">Logger factory; used to construct the gamelog-path validator's logger and this VM's logger.</param>
    /// <param name="bootstrapFailure">Plan 22.2: non-null = bootstrap-failure mode (banner shown; only the gamelog-path section visible; <see cref="ApplyAsync"/> writes only <c>gamelog_path</c>). Null = normal chrome-button mode.</param>
    /// <exception cref="ArgumentNullException">Thrown if any required argument is null.</exception>
    public SettingsDialogViewModel(
        ISettingsRepository settings,
        IMarketStationCatalog stations,
        IBootstrapConfigLoader config,
        IAddStationDialog addStationDialog,
        IBootstrapErrorReporter errorReporter,
        ILoggerFactory loggerFactory,
        BootstrapFailureContext? bootstrapFailure = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(addStationDialog);
        ArgumentNullException.ThrowIfNull(errorReporter);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _settings = settings;
        _stations = stations;
        _config = config;
        _addStationDialog = addStationDialog;
        _errorReporter = errorReporter;
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger<SettingsDialogViewModel>();

        _failure = bootstrapFailure;
        _bannerMessage = bootstrapFailure?.Message ?? string.Empty;

        Stations = new ObservableCollection<MarketStation>();

        AddStationCommand = new RelayCommand(OnAddStation);
        RemoveStationCommand = new RelayCommand<MarketStation>(
            station => OnRemoveStation(station),
            station => station is not null);
        BrowseGamelogCommand = new RelayCommand(OnBrowseGamelog);
    }

    /// <summary>Selected theme. Setter marks the VM dirty.</summary>
    public ThemeMode Theme
    {
        get => _theme;
        set => SetField(ref _theme, value);
    }

    /// <summary>Selected price mode. Setter marks the VM dirty.</summary>
    public PriceMode PriceMode
    {
        get => _priceMode;
        set => SetField(ref _priceMode, value);
    }

    /// <summary>
    /// Refining yield as the user typed it. Validated and parsed in
    /// <see cref="ApplyAsync"/>; bound bidirectionally to a <c>TextBox</c>.
    /// </summary>
    public string RefiningYieldText
    {
        get => _refiningYieldText;
        set => SetField(ref _refiningYieldText, value ?? string.Empty);
    }

    /// <summary>Currently selected station id (drives plan 18 ISK calc).</summary>
    public long SelectedStationId
    {
        get => _selectedStationId;
        set => SetField(ref _selectedStationId, value);
    }

    /// <summary>Path to the EVE Gamelogs folder. Validated in <see cref="ApplyAsync"/>.</summary>
    public string GamelogPath
    {
        get => _gamelogPath;
        set => SetField(ref _gamelogPath, value ?? string.Empty);
    }

    /// <summary>
    /// Plan 37 — user-typed idle-minutes threshold (string for input + validation,
    /// parsed to an int in <see cref="ApplyAsync"/>). Setter marks the VM dirty.
    /// </summary>
    public string AutoPauseIdleMinutesText
    {
        get => _autoPauseIdleMinutesText;
        set => SetField(ref _autoPauseIdleMinutesText, value ?? string.Empty);
    }

    /// <summary>
    /// Plan 37 — master on/off for the auto-pause / auto-resume notification
    /// sounds. Setter marks the VM dirty.
    /// </summary>
    public bool AutoPauseSoundEnabled
    {
        get => _autoPauseSoundEnabled;
        set => SetField(ref _autoPauseSoundEnabled, value);
    }

    /// <summary>
    /// Plan 43: whether newly-produced refined materials auto-show in the live
    /// widgets' "Refined output" section. Setter marks the VM dirty.
    /// </summary>
    public bool AutoShowRefinedMaterials
    {
        get => _autoShowRefinedMaterials;
        set => SetField(ref _autoShowRefinedMaterials, value);
    }

    /// <summary>
    /// Live view of the markets list shown in the dialog. Equals the persisted
    /// snapshot minus pending removals plus pending adds; rebuilt on every
    /// <see cref="OnAddStation"/> / <see cref="OnRemoveStation"/> /
    /// <see cref="LoadAsync"/> / <see cref="ApplyAsync"/>.
    /// </summary>
    public ObservableCollection<MarketStation> Stations { get; }

    /// <summary>Theme dropdown options.</summary>
    public IReadOnlyList<ThemeMode> ThemeOptions { get; } = new[] { ThemeMode.Light, ThemeMode.Dark };

    /// <summary>Price-mode dropdown options.</summary>
    public IReadOnlyList<PriceMode> PriceModeOptions { get; } = new[] { PriceMode.Buy, PriceMode.Sell, PriceMode.Split };

    /// <summary>
    /// True after any setter or markets mutation has fired since the last
    /// <see cref="LoadAsync"/> or successful <see cref="ApplyAsync"/>.
    /// Drives the close-with-unsaved-changes confirm prompt.
    /// </summary>
    public bool IsDirty => _dirty;

    /// <summary>
    /// Plan 22.2: <c>true</c> iff the dialog was constructed with a
    /// <see cref="BootstrapFailureContext"/>. Drives section visibility (only
    /// the gamelog-path section is visible) and the close-without-Apply =
    /// <c>Application.Shutdown(1)</c> behavior in the dialog code-behind.
    /// </summary>
    public bool IsBootstrapFailureMode => _failure is not null;

    /// <summary>
    /// Plan 22.2: banner text rendered at the top of the dialog when
    /// <see cref="IsBootstrapFailureMode"/> is <c>true</c>. Initialised from
    /// the <see cref="BootstrapFailureContext.Message"/>; mutated by
    /// <see cref="ApplyAsync"/> on save failures to surface the underlying
    /// error inline.
    /// </summary>
    public string BannerMessage
    {
        get => _bannerMessage;
        private set
        {
            if (_bannerMessage == value) return;
            _bannerMessage = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Plan 22.2: raised after a successful <see cref="ApplyAsync"/> in
    /// bootstrap-failure mode. The dialog code-behind subscribes to this and
    /// re-runs the bootstrap chain via <c>App.RunBootstrapChainAsync</c>; on
    /// chain success the dialog closes cleanly behind the now-open MainWindow.
    /// </summary>
    public event EventHandler? BootstrapApplySucceeded;

    /// <summary>Bound to the Add-station button.</summary>
    public ICommand AddStationCommand { get; }

    /// <summary>Bound to the Remove-station button; parameter = the markets <c>ListBox.SelectedItem</c>.</summary>
    public ICommand RemoveStationCommand { get; }

    /// <summary>Bound to the Browse... button next to the gamelog path TextBox.</summary>
    public ICommand BrowseGamelogCommand { get; }

    /// <summary>Raised when the user clicks Browse; subscribed by the dialog code-behind to open the WPF folder dialog on the UI thread.</summary>
    public event EventHandler? BrowseRequested;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Read every setting via <see cref="ISettingsRepository"/>,
    /// <see cref="IMarketStationCatalog"/>, and
    /// <see cref="IBootstrapConfigLoader"/>; populate every field; clear any
    /// pending markets edits; reset <see cref="IsDirty"/> to <c>false</c>.
    /// Called by the dialog code-behind on <c>Loaded</c>, and again after a
    /// successful <see cref="ApplyAsync"/>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (IsBootstrapFailureMode)
        {
            // Plan 22.2 decision 22.T: read only gamelog_path. _settings and
            // _stations are throwing stubs at this point — the bootstrap chain
            // hasn't constructed the real services yet (failure fired before
            // chain step 7).
            _gamelogPath = await ReadCurrentGamelogPathAsync(ct).ConfigureAwait(true);
            _dirty = false;
            OnPropertyChanged(string.Empty);
            return;
        }

        // Read every value into a local first so the field assignments (and the
        // markets ObservableCollection rebuild) all happen synchronously below.
        var theme = await _settings.GetThemeAsync(ct).ConfigureAwait(true);
        var priceMode = await _settings.GetPriceModeAsync(ct).ConfigureAwait(true);
        var yield = await _settings.GetRefiningYieldPercentAsync(ct).ConfigureAwait(true);
        var selectedStationId = await _settings.GetSelectedStationIdAsync(ct).ConfigureAwait(true);
        var gamelogPath = await ReadCurrentGamelogPathAsync(ct).ConfigureAwait(true);
        var persisted = await _stations.GetAllAsync(ct).ConfigureAwait(true);
        // Plan 37 — auto-pause section.
        var idleMinutes = await _settings.GetAutoPauseIdleMinutesAsync(ct).ConfigureAwait(true);
        var soundEnabled = await _settings.GetAutoPauseSoundEnabledAsync(ct).ConfigureAwait(true);
        // Plan 43 — refined-output auto-show.
        var autoShowRefined = await _settings.GetAutoShowRefinedMaterialsAsync(ct).ConfigureAwait(true);

        // Populate Stations BEFORE assigning _selectedStationId. The ComboBox's
        // TwoWay SelectedValue binding (SettingsDialog.xaml) evaluates against
        // ItemsSource at notification time; if items are empty when SelectedValue
        // re-resolves, the binding may write 0 back to the source via TwoWay
        // and the selection ends up blank even after OnPropertyChanged fires.
        // Bug 1: ordering invariant — items first, selection last, then notify.
        _persistedStations = persisted;
        _pendingAdds.Clear();
        _pendingRemovals.Clear();
        RebuildStationsView();

        _theme = theme;
        _priceMode = priceMode;
        _refiningYieldText = yield.ToString("R", CultureInfo.InvariantCulture);
        _gamelogPath = gamelogPath;
        _selectedStationId = selectedStationId;
        // Plan 37 — populate the auto-pause fields.
        _autoPauseIdleMinutesText = idleMinutes.ToString(CultureInfo.InvariantCulture);
        _autoPauseSoundEnabled = soundEnabled;
        _autoShowRefinedMaterials = autoShowRefined;

        _dirty = false;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Validate the current state and, on success, persist every setting and
    /// flush pending markets edits. Apply/Close pattern: dialog stays open
    /// after either outcome; caller surfaces returned errors to the user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="ApplyResult.Saved"/> = <c>true</c> with empty errors on
    /// success; <c>false</c> with one or more human-readable error strings on
    /// validation failure (in which case nothing was persisted).
    /// </returns>
    public async Task<ApplyResult> ApplyAsync(CancellationToken ct = default)
    {
        if (IsBootstrapFailureMode)
        {
            return await ApplyBootstrapFailureAsync(ct).ConfigureAwait(true);
        }

        var errors = await ValidateAsync(ct).ConfigureAwait(true);
        if (errors.Count > 0)
        {
            return new ApplyResult(false, errors);
        }

        await _settings.SetThemeAsync(_theme, ct).ConfigureAwait(true);
        await _settings.SetPriceModeAsync(_priceMode, ct).ConfigureAwait(true);

        var yieldValue = double.Parse(_refiningYieldText, NumberStyles.Float, CultureInfo.InvariantCulture);
        await _settings.SetRefiningYieldPercentAsync(yieldValue, ct).ConfigureAwait(true);

        // Plan 37 — auto-pause section. ValidateAsync has already ensured the
        // int parse succeeds and the value is in range.
        var idleValue = int.Parse(_autoPauseIdleMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture);
        await _settings.SetAutoPauseIdleMinutesAsync(idleValue, ct).ConfigureAwait(true);
        await _settings.SetAutoPauseSoundEnabledAsync(_autoPauseSoundEnabled, ct).ConfigureAwait(true);
        await _settings.SetAutoShowRefinedMaterialsAsync(_autoShowRefinedMaterials, ct).ConfigureAwait(true);

        await _settings.SetSelectedStationIdAsync(_selectedStationId, ct).ConfigureAwait(true);

        await _config.SaveAsync(_gamelogPath, ct).ConfigureAwait(true);

        foreach (var stationId in _pendingRemovals)
        {
            await _stations.RemoveAsync(stationId, ct).ConfigureAwait(true);
        }
        foreach (var station in _pendingAdds)
        {
            await _stations.AddAsync(station, ct).ConfigureAwait(true);
        }

        _persistedStations = await _stations.GetAllAsync(ct).ConfigureAwait(true);
        _pendingAdds.Clear();
        _pendingRemovals.Clear();

        // Snapshot SelectedStationId before RebuildStationsView. Stations.Clear()
        // briefly empties the ComboBox's ItemsSource and the TwoWay SelectedValue
        // binding writes 0 back into the source — same root cause as Bug 1
        // (LoadAsync), different code path. Restore + notify after the rebuild
        // so the ComboBox re-resolves the selection against the populated items.
        var selectionToPreserve = _selectedStationId;
        RebuildStationsView();
        _selectedStationId = selectionToPreserve;
        OnPropertyChanged(nameof(SelectedStationId));

        _dirty = false;
        OnPropertyChanged(nameof(IsDirty));

        return new ApplyResult(true, Array.Empty<string>());
    }

    /// <summary>
    /// Validate every editable field. Returns one human-readable error string
    /// per problem; an empty list means safe to persist. Async because the
    /// gamelog-path check enumerates the directory and reads file headers via
    /// <see cref="GamelogDiscoveryService.DiscoverCharactersAsync"/>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Zero or more error messages.</returns>
    public async Task<IReadOnlyList<string>> ValidateAsync(CancellationToken ct = default)
    {
        var errors = new List<string>();

        if (!double.TryParse(_refiningYieldText, NumberStyles.Float, CultureInfo.InvariantCulture, out var yield))
        {
            errors.Add($"Refining yield is not a valid number (you entered '{_refiningYieldText}').");
        }
        else if (yield < YieldMin || yield > YieldMax)
        {
            errors.Add($"Refining yield must be between {YieldMin} and {YieldMax} (you entered {yield}).");
        }

        // Plan 37 — auto-pause idle threshold: int parse + range [1, 60].
        if (!int.TryParse(_autoPauseIdleMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idleMin))
        {
            errors.Add($"Idle minutes is not a valid integer (you entered '{_autoPauseIdleMinutesText}').");
        }
        else if (idleMin < IdleMinutesMin || idleMin > IdleMinutesMax)
        {
            errors.Add($"Idle minutes must be between {IdleMinutesMin} and {IdleMinutesMax} (you entered {idleMin}).");
        }

        var pathErrors = await ValidateGamelogPathAsync(ct).ConfigureAwait(true);
        errors.AddRange(pathErrors);

        return errors;
    }

    /// <summary>
    /// Validate the <c>gamelog_path</c> field in isolation. Used by
    /// <see cref="ValidateAsync"/> for the normal Apply flow and by
    /// <see cref="ApplyBootstrapFailureAsync"/> when only the path is editable.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Zero or more error messages describing problems with <see cref="GamelogPath"/>.</returns>
    private async Task<IReadOnlyList<string>> ValidateGamelogPathAsync(CancellationToken ct)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(_gamelogPath))
        {
            errors.Add("Gamelog folder path is empty.");
            return errors;
        }

        try
        {
            if (!Directory.Exists(_gamelogPath))
            {
                errors.Add($"Gamelog folder does not exist: {_gamelogPath}");
            }
            else
            {
                var discovery = new GamelogDiscoveryService(
                    _gamelogPath,
                    _loggerFactory.CreateLogger<GamelogDiscoveryService>());
                var characters = await discovery.DiscoverCharactersAsync(ct).ConfigureAwait(true);
                if (characters.Count == 0)
                {
                    errors.Add(
                        $"Gamelog folder contains no EVE gamelog files: {_gamelogPath}\n" +
                        "Expected filename pattern YYYYMMDD_HHMMSS_<characterID>.txt. Has EVE run at least once at this path?");
                }
            }
        }
        catch (ArgumentException ex)
        {
            errors.Add($"Gamelog folder is not a valid path: {ex.Message}");
        }
        catch (IOException ex)
        {
            errors.Add($"Gamelog folder is not a valid path: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            errors.Add($"Gamelog folder is not a valid path: {ex.Message}");
        }

        return errors;
    }

    /// <summary>
    /// Plan 22.2 / decision 22.U: Apply path for bootstrap-failure mode.
    /// Validates only <see cref="GamelogPath"/>, calls
    /// <see cref="IBootstrapConfigLoader.SaveAsync"/>, and on success raises
    /// <see cref="BootstrapApplySucceeded"/> for the dialog code-behind to
    /// re-run the bootstrap chain. Two consecutive save failures escalate
    /// to <see cref="IBootstrapErrorReporter.ReportAndShutdown"/> per
    /// decision 2.4 = B.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="ApplyResult"/> mirroring the normal-mode shape.</returns>
    private async Task<ApplyResult> ApplyBootstrapFailureAsync(CancellationToken ct)
    {
        var errors = await ValidateGamelogPathAsync(ct).ConfigureAwait(true);
        if (errors.Count > 0)
        {
            return new ApplyResult(false, errors);
        }

        try
        {
            await _config.SaveAsync(_gamelogPath, ct).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _consecutiveSaveFailures++;
            BannerMessage = $"Could not save: {ex.Message}. Try again or close to exit.";
            _log.LogError(ex, "Bootstrap-failure SaveAsync failed (attempt {Attempt}).", _consecutiveSaveFailures);

            if (_consecutiveSaveFailures >= 2)
            {
                // Per decision 2.4 = B: second consecutive failure -> escalate
                // to the BootstrapErrorReporter shutdown path. Test seam runs
                // a fake; production runs the real BootstrapErrorReporter.
                _errorReporter.ReportAndShutdown(
                    "Could not save your gamelog folder",
                    $"Original failure: {_failure!.Message}\n\nSecond save attempt failed: {ex.Message}\n\n" +
                    "Please edit %APPDATA%\\EveDashboard\\config.yaml directly and restart.");
            }

            return new ApplyResult(false, new[] { ex.Message });
        }

        _consecutiveSaveFailures = 0;
        BootstrapApplySucceeded?.Invoke(this, EventArgs.Empty);
        return new ApplyResult(true, Array.Empty<string>());
    }

    private void OnAddStation()
    {
        var newStation = _addStationDialog.Show();
        if (newStation is null) return;

        var alreadyPersisted = _persistedStations.Any(
            s => s.StationId == newStation.StationId && !_pendingRemovals.Contains(s.StationId));
        var alreadyPending = _pendingAdds.Any(s => s.StationId == newStation.StationId);
        if (alreadyPersisted || alreadyPending)
        {
            _log.LogWarning("Duplicate station id {StationId} ignored.", newStation.StationId);
            return;
        }

        _pendingAdds.Add(newStation);
        RebuildStationsView();
        SetDirty();
    }

    private void OnRemoveStation(MarketStation? station)
    {
        if (station is null) return;

        var pendingAddIndex = _pendingAdds.FindIndex(s => s.StationId == station.StationId);
        if (pendingAddIndex >= 0)
        {
            _pendingAdds.RemoveAt(pendingAddIndex);
        }
        else
        {
            _pendingRemovals.Add(station.StationId);
        }

        if (_selectedStationId == station.StationId)
        {
            var fallback = _persistedStations
                .Where(s => !_pendingRemovals.Contains(s.StationId))
                .Select(s => (long?)s.StationId)
                .FirstOrDefault();
            _selectedStationId = fallback ?? 0L;
            OnPropertyChanged(nameof(SelectedStationId));
        }

        RebuildStationsView();
        SetDirty();
    }

    private void OnBrowseGamelog()
    {
        BrowseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildStationsView()
    {
        Stations.Clear();
        foreach (var s in _persistedStations.Where(s => !_pendingRemovals.Contains(s.StationId)))
        {
            Stations.Add(s);
        }
        foreach (var s in _pendingAdds)
        {
            Stations.Add(s);
        }
    }

    private async Task<string> ReadCurrentGamelogPathAsync(CancellationToken ct)
    {
        try
        {
            var result = await _config.LoadAsync(ct).ConfigureAwait(true);
            return result switch
            {
                BootstrapConfigResult.Loaded l => l.GamelogPath,
                BootstrapConfigResult.AutodetectUsed a => a.GamelogPath,
                BootstrapConfigResult.AutodetectFailed f => f.AttemptedPath,
                _ => string.Empty,
            };
        }
        catch (BootstrapConfigException ex)
        {
            _log.LogWarning(ex, "Bootstrap config malformed while loading current gamelog path; field starts empty so the user can correct it.");
            return string.Empty;
        }
    }

    private void SetDirty()
    {
        if (_dirty) return;
        _dirty = true;
        OnPropertyChanged(nameof(IsDirty));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        SetDirty();
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
    }
}

/// <summary>
/// Outcome of <see cref="SettingsDialogViewModel.ApplyAsync"/>.
/// </summary>
/// <param name="Saved"><c>true</c> if validation passed and every setting was persisted; <c>false</c> if validation failed (nothing was persisted).</param>
/// <param name="Errors">Empty when <paramref name="Saved"/> = <c>true</c>; one human-readable string per validation problem otherwise.</param>
public sealed record ApplyResult(bool Saved, IReadOnlyList<string> Errors);
