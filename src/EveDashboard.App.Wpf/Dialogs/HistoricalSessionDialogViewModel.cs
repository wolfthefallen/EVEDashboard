namespace EveDashboard.App.Wpf.Dialogs;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.Common;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Sessions.Historical;
using EveDashboard.Core.Settings;
using EveDashboard.MarketData.Pricing;
using EveDashboard.Mining.Historical;
using EveDashboard.Stats.Historical;
using Microsoft.Extensions.Logging;

/// <summary>
/// View-model of the modal "Build session from history" dialog. Two modes share
/// one flow: DATE-RANGE (the user types a window and picks a fleet; gamelogs are
/// parsed for the untracked parts) and COMBINE (opened with ticked sessions; the
/// window is their span, they are pre-ticked, nothing is parsed).
/// </summary>
/// <remarks>
/// <para>
/// Flow: <see cref="CheckOverlapsCommand"/> lists every saved session that
/// overlaps the window and shares pilots with the fleet, each with a Combine
/// tick-box and its match kind; the notice under the list spells out which
/// pilots' time will be EXCLUDED for the sessions left unticked (a pilot can be
/// in one session at a time). <see cref="BuildCommand"/> then: asks to create a
/// union fleet when a ticked session brings extra pilots (declining leaves those
/// sessions out instead), reads the gamelogs (date-range mode), builds the
/// session atomically, prices it, and hands an <see cref="Outcome"/> back.
/// </para>
/// <para>
/// Times are entered in EVE time (UTC), the same clock the gamelogs and the
/// Reports sidebar use. The dialog is blocked while a session is Running or
/// Paused so a historical window can never overlap in-flight data.
/// </para>
/// </remarks>
public sealed partial class HistoricalSessionDialogViewModel : ObservableObject
{
    private static readonly string[] TimeFormats = { "HH:mm", "HH:mm:ss", "H:mm", "H:mm:ss" };

    private readonly ISessionSpanQuery _spans;
    private readonly IHistoricalGamelogReader _reader;
    private readonly IHistoricalSessionBuilder _builder;
    private readonly IHistoricalMarketPricer _pricer;
    private readonly IFleetRepository _fleets;
    private readonly ISettingsRepository _settings;
    private readonly IActiveSessionContext _active;
    private readonly IConfirmDialog _confirm;
    private readonly ILogger<HistoricalSessionDialogViewModel> _log;

    private IReadOnlyList<string> _preselectedIds = Array.Empty<string>();

    /// <summary>Start date of the window (EVE time / UTC).</summary>
    [ObservableProperty] private DateTime? _startDate;

    /// <summary>Start time of day, <c>HH:mm</c> (EVE time / UTC).</summary>
    [ObservableProperty] private string _startTimeText = "00:00";

    /// <summary>Stop date of the window (EVE time / UTC).</summary>
    [ObservableProperty] private DateTime? _stopDate;

    /// <summary>Stop time of day, <c>HH:mm</c> (EVE time / UTC).</summary>
    [ObservableProperty] private string _stopTimeText = "23:59";

    /// <summary>Default session name template: the window's dates and year, e.g. <c>16Aug - 19Aug 2026 history</c>.</summary>
    public const string DefaultNameTemplate = "{{$STARTDATE}} - {{$ENDDATE}} {{$YYYY}} history";

    /// <summary>Session name template (same tokens as New Session, plus the window tokens).</summary>
    [ObservableProperty] private string _sessionName = DefaultNameTemplate;

    /// <summary>Optional notes.</summary>
    [ObservableProperty] private string? _notes;

    /// <summary>The fleet the new session is built for.</summary>
    [ObservableProperty] private Fleet? _selectedFleet;

    /// <summary>True when opened from "Combine selected" (no gamelog parsing).</summary>
    [ObservableProperty] private bool _isCombineMode;

    /// <summary>Non-empty while a live session blocks the feature.</summary>
    [ObservableProperty] private string _blockedMessage = string.Empty;

    /// <summary>Validation / progress / error line.</summary>
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>Explains which pilots' time is excluded for unticked sessions.</summary>
    [ObservableProperty] private string _exclusionNotice = string.Empty;

    /// <summary>
    /// True after a successful overlap check for the CURRENT inputs; editing any
    /// input clears it. Drives the single action button: "Check overlaps" until
    /// this is true, then "Build" - the workflow is forced, never a disabled
    /// button with no explanation.
    /// </summary>
    [ObservableProperty] private bool _hasChecked;

    /// <summary>True while checking or building.</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>Set to true to ask the window to close.</summary>
    [ObservableProperty] private bool _shouldClose;

    /// <summary>Fleets available to pick from.</summary>
    public ObservableCollection<Fleet> Fleets { get; } = new();

    /// <summary>The overlapping sessions found by the last check.</summary>
    public ObservableCollection<OverlapRowViewModel> Overlaps { get; } = new();

    /// <summary>What was built, or <c>null</c> until the user completes the dialog.</summary>
    public HistoricalBuildOutcome? Outcome { get; private set; }

    /// <summary>Dialog heading.</summary>
    public string HeaderText => IsCombineMode ? "Combine saved sessions" : "Build a session from history";

    /// <summary>True when nothing blocks the feature.</summary>
    public bool IsEnabled => BlockedMessage.Length == 0;

    /// <summary>True when the check found no overlapping sessions (shown as a hint).</summary>
    public bool HasNoOverlaps => HasChecked && Overlaps.Count == 0;

    /// <summary>Construct the view-model.</summary>
    /// <param name="spans">Saved-session span query.</param>
    /// <param name="reader">Historical gamelog reader.</param>
    /// <param name="builder">Historical session builder.</param>
    /// <param name="pricer">Historical market pricer.</param>
    /// <param name="fleets">Fleet repository (list + create union fleet).</param>
    /// <param name="settings">Settings (default fleet, idle minutes).</param>
    /// <param name="active">Live-session state (blocks the dialog while a session is active).</param>
    /// <param name="confirm">Confirm dialog for the union-fleet question.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public HistoricalSessionDialogViewModel(
        ISessionSpanQuery spans,
        IHistoricalGamelogReader reader,
        IHistoricalSessionBuilder builder,
        IHistoricalMarketPricer pricer,
        IFleetRepository fleets,
        ISettingsRepository settings,
        IActiveSessionContext active,
        IConfirmDialog confirm,
        ILogger<HistoricalSessionDialogViewModel> log)
    {
        _spans = spans ?? throw new ArgumentNullException(nameof(spans));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _pricer = pricer ?? throw new ArgumentNullException(nameof(pricer));
        _fleets = fleets ?? throw new ArgumentNullException(nameof(fleets));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _active = active ?? throw new ArgumentNullException(nameof(active));
        _confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Reset every field for a fresh open. Called by the adapter BEFORE the window
    /// is constructed so the first render is clean.
    /// </summary>
    /// <param name="preselectedSessionIds">Sessions to combine (combine mode) or null/empty (date-range mode).</param>
    public void ResetForOpen(IReadOnlyList<string>? preselectedSessionIds)
    {
        _preselectedIds = preselectedSessionIds ?? Array.Empty<string>();
        IsCombineMode = _preselectedIds.Count > 0;
        StartDate = null; StartTimeText = "00:00"; StopDate = null; StopTimeText = "23:59";
        SessionName = DefaultNameTemplate;
        Notes = null;
        SelectedFleet = null;
        Fleets.Clear();
        ClearCheck();
        StatusMessage = string.Empty;
        BlockedMessage = string.Empty;
        IsBusy = false;
        ShouldClose = false;
        Outcome = null;
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(IsEnabled));
    }

    /// <summary>
    /// Hydrate on the window's Loaded: block if a session is live, load the
    /// fleets (default preselected), and in combine mode pre-fill the window from
    /// the ticked sessions and run the overlap check so they appear pre-ticked.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_active.ActiveSessionId is not null)
        {
            BlockedMessage = "A mining session is running or paused. Stop it first - a historical window must never overlap live data.";
            OnPropertyChanged(nameof(IsEnabled));
            return;
        }

        var fleets = await _fleets.GetFleetsAsync(ct).ConfigureAwait(true);
        var defaultId = await _settings.GetDefaultFleetIdAsync(ct).ConfigureAwait(true);
        Fleets.Clear();
        foreach (var f in fleets.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)) Fleets.Add(f);
        SelectedFleet = Fleets.FirstOrDefault(f => f.FleetId == defaultId) ?? Fleets.FirstOrDefault();

        if (IsCombineMode)
        {
            var spans = await _spans.GetSpansAsync(_preselectedIds, ct: ct).ConfigureAwait(true);
            if (spans.Count == 0)
            {
                StatusMessage = "None of the ticked sessions can be combined (archived or still running).";
                return;
            }
            var start = spans.Min(s => s.Start);
            var stop = spans.Max(s => s.Stop);
            StartDate = start.UtcDateTime.Date; StartTimeText = start.ToString("HH:mm", CultureInfo.InvariantCulture);
            StopDate = stop.UtcDateTime.Date;  StopTimeText = stop.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (SelectedFleet is not null)
            {
                await CheckOverlapsAsync(ct).ConfigureAwait(true);
            }
        }
    }

    // Any input change invalidates the last check (the overlap list belongs to
    // specific inputs).
    partial void OnStartDateChanged(DateTime? value) => ClearCheck();
    partial void OnStartTimeTextChanged(string value) => ClearCheck();
    partial void OnStopDateChanged(DateTime? value) => ClearCheck();
    partial void OnStopTimeTextChanged(string value) => ClearCheck();
    partial void OnSelectedFleetChanged(Fleet? value) => ClearCheck();
    partial void OnHasCheckedChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNoOverlaps));
        BuildCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsBusyChanged(bool value)
    {
        CheckOverlapsCommand.NotifyCanExecuteChanged();
        BuildCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsCombineModeChanged(bool value) => OnPropertyChanged(nameof(HeaderText));

    private void ClearCheck()
    {
        foreach (var row in Overlaps) row.PropertyChanged -= OnOverlapRowChanged;
        Overlaps.Clear();
        ExclusionNotice = string.Empty;
        HasChecked = false;
        OnPropertyChanged(nameof(HasNoOverlaps));
    }

    /// <summary>Parse the four window fields into a UTC window. Returns an error message on failure.</summary>
    /// <param name="windowStart">Parsed start.</param>
    /// <param name="windowStop">Parsed stop.</param>
    /// <returns><c>null</c> when valid, else the message to show.</returns>
    public string? TryParseWindow(out DateTimeOffset windowStart, out DateTimeOffset windowStop)
    {
        windowStart = default; windowStop = default;
        if (StartDate is null || StopDate is null) return "Pick both a start and a stop date.";
        if (!TryParseTime(StartTimeText, out var startTime)) return "Start time must be HH:mm (EVE time).";
        if (!TryParseTime(StopTimeText, out var stopTime)) return "Stop time must be HH:mm (EVE time).";
        windowStart = new DateTimeOffset(StartDate.Value.Date + startTime, TimeSpan.Zero);
        windowStop = new DateTimeOffset(StopDate.Value.Date + stopTime, TimeSpan.Zero);
        if (windowStop <= windowStart) return "The stop must be after the start.";
        if (windowStop > DateTimeOffset.UtcNow) return "The stop cannot be in the future.";
        return null;
    }

    private static bool TryParseTime(string text, out TimeSpan time)
    {
        if (DateTime.TryParseExact(text?.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            time = dt.TimeOfDay;
            return true;
        }
        time = default;
        return false;
    }

    private bool CanCheck() => !IsBusy && IsEnabled;

    /// <summary>Find the saved sessions that overlap the window and share pilots with the fleet.</summary>
    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckOverlapsAsync(CancellationToken ct)
    {
        var error = TryParseWindow(out var start, out var stop);
        if (error is not null) { StatusMessage = error; return; }
        if (SelectedFleet is null) { StatusMessage = "Pick a fleet."; return; }

        IsBusy = true;
        try
        {
            var spans = await _spans.GetOverlappingAsync(start, stop, ct: ct).ConfigureAwait(true);
            var overlaps = OverlapAnalyzer.Analyze(SelectedFleet.Members, spans, start, stop);

            ClearCheck();
            foreach (var o in overlaps)
            {
                var row = new OverlapRowViewModel(o, isChecked: _preselectedIds.Contains(o.Session.SessionId, StringComparer.Ordinal));
                row.PropertyChanged += OnOverlapRowChanged;
                Overlaps.Add(row);
            }
            UpdateExclusionNotice();
            HasChecked = true;
            StatusMessage = overlaps.Count == 0
                ? "No saved session overlaps this window for these pilots."
                : $"{overlaps.Count} overlapping session(s) found - tick the ones to combine.";
        }
        catch (Exception ex) when (ex is DbException or IOException)
        {
            _log.LogWarning(ex, "Overlap check failed.");
            StatusMessage = "Could not read the saved sessions: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnOverlapRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlapRowViewModel.IsChecked)) UpdateExclusionNotice();
    }

    private void UpdateExclusionNotice()
    {
        var declined = Overlaps.Where(r => !r.IsChecked).ToList();
        if (declined.Count == 0) { ExclusionNotice = string.Empty; return; }
        var lines = declined.Select(r =>
            $"'{r.Name}' ({r.SpanText}) is not combined: {r.SharedPilotsText}'s time inside it will be excluded from this window so nothing is counted twice.");
        ExclusionNotice = string.Join(Environment.NewLine, lines);
    }

    private bool CanBuild() => HasChecked && !IsBusy && IsEnabled;

    /// <summary>Run the pipeline: union-fleet prompt, gamelog read, build, price.</summary>
    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync(CancellationToken ct)
    {
        var error = TryParseWindow(out var start, out var stop);
        if (error is not null) { StatusMessage = error; return; }
        if (SelectedFleet is null) { StatusMessage = "Pick a fleet."; return; }
        if (string.IsNullOrWhiteSpace(SessionName)) { StatusMessage = "Give the session a name."; return; }

        IsBusy = true;
        try
        {
            var roster = (IReadOnlyList<FleetMember>)SelectedFleet.Members;
            var ticked = Overlaps.Where(r => r.IsChecked).Select(r => r.Overlap).ToList();
            var declined = Overlaps.Where(r => !r.IsChecked).Select(r => r.Overlap.Session).ToList();

            // Partial matches need a union fleet; declining leaves them out instead.
            var partials = ticked.Where(o => o.Kind == FleetMatchKind.Partial).ToList();
            if (partials.Count > 0)
            {
                var union = OverlapAnalyzer.ProposeUnionRoster(roster, partials);
                var extras = union.Skip(roster.Count).Select(m => m.Name).ToList();
                var unionName = SelectedFleet.Name + " + " + string.Join(" + ", extras);
                var create = _confirm.Show(
                    title: "Create a union fleet?",
                    body: $"The ticked session(s) include pilots that are not in '{SelectedFleet.Name}': {string.Join(", ", extras)}.\n\n" +
                          $"Create fleet '{unionName}' with them and use it for this session? " +
                          "If you leave them out, those sessions are not combined and their pilots' time is excluded instead.",
                    confirmLabel: "Create fleet",
                    cancelLabel: "Leave them out");
                if (create)
                {
                    roster = union;
                    var created = await _fleets.CreateFleetAsync(unionName, union, ct).ConfigureAwait(true);
                    if (created.Status != FleetCreateStatus.Created)
                    {
                        _log.LogWarning("Union fleet '{Name}' was not saved ({Status}); building with the union roster anyway.", unionName, created.Status);
                    }
                }
                else
                {
                    foreach (var p in partials) { ticked.Remove(p); declined.Add(p.Session); }
                }
            }

            var exclusions = OverlapAnalyzer.ComputeExclusions(roster, declined, start, stop);

            IReadOnlyList<MiningEvent> events = Array.Empty<MiningEvent>();
            IReadOnlyList<long> pilotsWithoutLogs = Array.Empty<long>();
            if (!IsCombineMode)
            {
                StatusMessage = "Reading gamelogs...";
                var read = await _reader.ReadAsync(roster.Select(m => m.PilotId).ToList(), start, stop, ct).ConfigureAwait(true);
                events = read.Events;
                pilotsWithoutLogs = read.PilotsWithoutLogs;
            }

            var idle = await _settings.GetAutoPauseIdleMinutesAsync(ct).ConfigureAwait(true);
            var name = SessionNameTemplateExpander.Expand(
                SessionName,
                roster.Select(m => new Character(m.PilotId, m.Name, string.Empty)).ToList(),
                start,
                windowStart: start,
                windowStop: stop);

            StatusMessage = "Building the session...";
            var result = await _builder.BuildAsync(new HistoricalSessionRequest(
                name, Notes, start, stop, roster, events, ticked.Select(o => o.Session.SessionId).ToList(), exclusions, idle), ct).ConfigureAwait(true);

            StatusMessage = "Pricing...";
            var pricing = await _pricer.PriceAsync(result.SessionId, result.EffectiveStart, result.EffectiveStop, ct).ConfigureAwait(true);

            var summary = BuildSummary(name, result, pricing, pilotsWithoutLogs, roster);
            Outcome = new HistoricalBuildOutcome(result.SessionId, name, summary);
            _log.LogInformation("Historical session built from the dialog: {Summary}", summary);
            ShouldClose = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or DbException or IOException)
        {
            _log.LogWarning(ex, "Building the historical session failed.");
            StatusMessage = "Build failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Compose the one-line outcome summary, including the pricing notice the spec requires.</summary>
    internal static string BuildSummary(
        string name, HistoricalSessionResult result, HistoricalPricingResult pricing, IReadOnlyList<long> pilotsWithoutLogs, IReadOnlyList<FleetMember> roster)
    {
        var parts = new List<string>
        {
            $"Built '{name}': {result.CopiedLogRows} copied + {result.ParsedLogRows} parsed mining rows, {result.ArchivedSessionIds.Count} session(s) archived.",
        };
        parts.Add(pricing.Basis switch
        {
            HistoricalPriceBasis.AveragedFromWindow => $"Prices averaged from {pricing.RowsAveraged} market rows captured inside the window.",
            HistoricalPriceBasis.Mixed => $"Prices averaged from {pricing.RowsAveraged} window rows; {pricing.TypesFetchedNow} material(s) had no window data and use TODAY's prices.",
            _ => "No market prices were captured inside this window at the selected station, so the session is valued with TODAY's prices.",
        });
        if (pilotsWithoutLogs.Count > 0)
        {
            var names = pilotsWithoutLogs.Select(id => roster.FirstOrDefault(m => m.PilotId == id)?.Name ?? id.ToString(CultureInfo.InvariantCulture));
            parts.Add($"No gamelog found for: {string.Join(", ", names)}.");
        }
        return string.Join(" ", parts);
    }

    /// <summary>Close without building.</summary>
    [RelayCommand]
    private void Cancel()
    {
        Outcome = null;
        ShouldClose = true;
    }
}
