namespace EveDashboard.App.Wpf.Dialogs;

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Sessions;
using EveDashboard.Core.Settings;

/// <summary>
/// ViewModel behind the <see cref="SessionConfigDialog"/> (plan 42). Owns the
/// fleet list (single-select, with a caret expander and right-click Make-default
/// / Delete), the Create-Fleet flow (a sortable pilot picker with per-pilot role
/// dropdowns, then an inline confirm/name panel), and the Start command. A fleet
/// is the only way to start a session — selecting one and clicking Start drives
/// <see cref="SessionController.StartAsync(IReadOnlyList{FleetMember}, CancellationToken)"/>,
/// whose stored roles flow into the session's <c>session_pilot</c> rows.
/// </summary>
/// <remarks>
/// The session-name textbox still forwards to the controller. The default
/// pointer + sort order are <c>setting</c> rows owned by
/// <see cref="ISettingsRepository"/>; this VM coordinates them around the
/// fleet-repository calls. <see cref="InitializeAsync"/> (invoked from the
/// dialog's <c>Loaded</c> event) hydrates the fleet list + persisted sort and
/// auto-selects the default fleet; <see cref="ResetForNewOpen"/> synchronously
/// clears transient state on each open.
/// </remarks>
public sealed partial class SessionConfigDialogViewModel : ObservableObject, IDisposable
{
    private readonly SessionController _controller;
    private readonly IReadOnlyList<Character> _allCharacters;
    private readonly IFleetRepository _fleetRepo;
    private readonly ISettingsRepository _settings;
    private readonly IConfirmDialog _confirmDialog;

    private bool _disposed;
    private bool _suppressSortPersist;
    private bool _suppressSelectionCascade;

    /// <summary>Construct the dialog VM. All dependencies are required.</summary>
    /// <param name="controller">The singleton session controller (owns the session-name field + Start).</param>
    /// <param name="allCharacters">All EVE characters discovered at app startup — the baseline for the pilot picker.</param>
    /// <param name="fleetRepo">Fleet repository (load / create / delete).</param>
    /// <param name="settings">Settings repository (default-fleet pointer + persisted sort order).</param>
    /// <param name="confirmDialog">Confirm-dialog seam for Delete.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public SessionConfigDialogViewModel(
        SessionController controller,
        IReadOnlyList<Character> allCharacters,
        IFleetRepository fleetRepo,
        ISettingsRepository settings,
        IConfirmDialog confirmDialog)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(allCharacters);
        ArgumentNullException.ThrowIfNull(fleetRepo);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(confirmDialog);
        _controller = controller;
        _allCharacters = allCharacters;
        _fleetRepo = fleetRepo;
        _settings = settings;
        _confirmDialog = confirmDialog;
        _controller.PropertyChanged += OnControllerPropertyChanged;
    }

    /* ----- session name (forwarded to the controller) + close signal ----- */

    /// <summary>Round-trip user-typed session name (template variables expanded at Start by the controller).</summary>
    public string SessionName
    {
        get => _controller.SessionName;
        set => _controller.SessionName = value;
    }

    /// <summary>True when the view should close itself (Start success or Cancel).</summary>
    [ObservableProperty]
    private bool _shouldClose;

    /// <summary>Inline error shown when Start cannot proceed (no discoverable fleet pilots).</summary>
    [ObservableProperty]
    private string? _startError;

    /// <summary>Discards the open and closes the dialog.</summary>
    [RelayCommand]
    private void Cancel() => ShouldClose = true;

    /* ----- fleet list (single-select) ----- */

    /// <summary>The saved fleets, in name order. Single-select is enforced in this VM.</summary>
    public ObservableCollection<FleetRowViewModel> Fleets { get; } = new();

    /// <summary>The currently selected fleet (drives Start). Null when none is selected.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private FleetRowViewModel? _selectedFleet;

    /// <summary>True when there are no saved fleets (drives the empty-state Create-Fleet prompt).</summary>
    public bool HasNoFleets => Fleets.Count == 0;

    /* ----- create-fleet flow state ----- */

    /// <summary>True while the pilot picker is visible (Create Fleet clicked).</summary>
    [ObservableProperty]
    private bool _isCreatingFleet;

    /// <summary>True while the inline confirm/name panel is visible (Add Fleet clicked).</summary>
    [ObservableProperty]
    private bool _isConfirmingFleet;

    /// <summary>The pilot picker rows (checkbox + role dropdown each), rebuilt when the sort changes.</summary>
    public ObservableCollection<PilotToggleViewModel> PilotPicker { get; } = new();

    /// <summary>The four sort options for the picker's Sort-by dropdown (decision 1.6 = A).</summary>
    public IReadOnlyList<PilotSortOrder> SortOptions { get; } = new[]
    {
        PilotSortOrder.LastActiveNewest,
        PilotSortOrder.LastActiveOldest,
        PilotSortOrder.NameAsc,
        PilotSortOrder.NameDesc,
    };

    /// <summary>The picker's current sort order. Persisted on change (decision 1.7 = A).</summary>
    [ObservableProperty]
    private PilotSortOrder _sortOrder = PilotSortOrder.LastActiveNewest;

    /// <summary>The four selectable roles for each picker row's role dropdown.</summary>
    public IReadOnlyList<PilotRole> RoleOptions { get; } = new[]
    {
        PilotRole.Miner, PilotRole.Booster, PilotRole.Cyno, PilotRole.Combat,
    };

    /* ----- confirm/name panel state ----- */

    /// <summary>The fleet name being entered in the confirm panel.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveFleetCommand))]
    private string _newFleetName = string.Empty;

    /// <summary>Whether the new fleet should be made the default on Save.</summary>
    [ObservableProperty]
    private bool _makeDefault;

    /// <summary>Inline error shown in the confirm panel (e.g. duplicate name).</summary>
    [ObservableProperty]
    private string? _saveError;

    /// <summary>The picked-pilots-and-roles display lines shown in the confirm panel.</summary>
    public ObservableCollection<string> ConfirmedRoster { get; } = new();

    /* ----- lifecycle ----- */

    /// <summary>
    /// Hydrate the fleet list + persisted sort order and auto-select the default
    /// fleet. Invoked from the dialog's <c>Loaded</c> event.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _suppressSortPersist = true;
        try
        {
            SortOrder = await _settings.GetPilotSortOrderAsync(ct);
        }
        finally
        {
            _suppressSortPersist = false;
        }
        await LoadFleetsAsync(selectFleetId: null, useDefaultAsSelection: true, ct);
    }

    /// <summary>Reset transient state for a fresh open (synchronous — the fleet list reloads in <see cref="InitializeAsync"/>).</summary>
    public void ResetForNewOpen()
    {
        ShouldClose = false;
        SessionName = string.Empty;
        IsCreatingFleet = false;
        IsConfirmingFleet = false;
        NewFleetName = string.Empty;
        MakeDefault = false;
        SaveError = null;
        StartError = null;
    }

    /* ----- Start ----- */

    /// <summary>CanExecute for Start: a fleet is selected AND the session name is non-empty.</summary>
    private bool CanStart() => SelectedFleet is not null && !string.IsNullOrWhiteSpace(SessionName);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        StartError = null;
        var result = await _controller.StartAsync(SelectedFleet!.Members);
        if (result == SessionStartResult.Started)
        {
            ShouldClose = true;
        }
        else
        {
            StartError = "None of this fleet's pilots are currently logged in. " +
                         "Start a pilot's client (or pick another fleet), then try again.";
        }
    }

    /* ----- Create / Add / Save / Cancel fleet ----- */

    /// <summary>Reveal the pilot picker for building a new fleet (resets picks).</summary>
    [RelayCommand]
    private void CreateFleet()
    {
        SaveError = null;
        StartError = null;
        IsConfirmingFleet = false;
        NewFleetName = string.Empty;
        MakeDefault = false;
        RebuildPilotPicker(resetPicks: true);
        IsCreatingFleet = true;
    }

    /// <summary>CanExecute for Add Fleet: at least one pilot is picked.</summary>
    private bool CanAddFleet() => PilotPicker.Any(p => p.IsSelected);

    /// <summary>Snapshot the picked pilots + roles into the confirm panel and reveal it.</summary>
    [RelayCommand(CanExecute = nameof(CanAddFleet))]
    private void AddFleet()
    {
        SaveError = null;
        ConfirmedRoster.Clear();
        foreach (var p in PilotPicker.Where(p => p.IsSelected))
        {
            ConfirmedRoster.Add($"{p.Character.Name}  —  {PilotRoleExtensions.ToCanonicalString(p.Role)}");
        }
        IsConfirmingFleet = true;
    }

    /// <summary>CanExecute for Save: a non-empty fleet name.</summary>
    private bool CanSaveFleet() => !string.IsNullOrWhiteSpace(NewFleetName);

    [RelayCommand(CanExecute = nameof(CanSaveFleet))]
    private async Task SaveFleetAsync()
    {
        SaveError = null;
        var roster = PilotPicker
            .Where(p => p.IsSelected)
            .Select(p => new FleetMember(p.Character.CharacterId, p.Character.Name, p.Role))
            .ToList();
        if (roster.Count == 0)
        {
            SaveError = "Pick at least one pilot before saving.";
            return;
        }

        var result = await _fleetRepo.CreateFleetAsync(NewFleetName, roster);
        if (result.Status == FleetCreateStatus.DuplicateName)
        {
            SaveError = $"A fleet named '{NewFleetName.Trim()}' already exists. Choose a different name.";
            return;
        }

        if (MakeDefault && result.FleetId is not null)
        {
            await _settings.SetDefaultFleetIdAsync(result.FleetId);
        }

        IsConfirmingFleet = false;
        IsCreatingFleet = false;
        await LoadFleetsAsync(selectFleetId: result.FleetId, useDefaultAsSelection: false, default);
    }

    /// <summary>Abandon the create/confirm flow and return to the fleet list.</summary>
    [RelayCommand]
    private void CancelCreate()
    {
        IsCreatingFleet = false;
        IsConfirmingFleet = false;
        SaveError = null;
    }

    /* ----- per-row manage commands ----- */

    /// <summary>Make the given fleet the default (right-click menu).</summary>
    [RelayCommand]
    private async Task MakeDefaultAsync(FleetRowViewModel? row)
    {
        if (row is null) return;
        await _settings.SetDefaultFleetIdAsync(row.FleetId);
        foreach (var f in Fleets)
        {
            f.IsDefault = ReferenceEquals(f, row);
        }
    }

    /// <summary>Delete the given fleet after confirmation; clears the default pointer if it was the default.</summary>
    [RelayCommand]
    private async Task DeleteFleetAsync(FleetRowViewModel? row)
    {
        if (row is null) return;
        var confirmed = _confirmDialog.Show(
            "Delete fleet?",
            $"Are you sure you want to delete '{row.Name}'? It cannot be undone.",
            "Delete");
        if (!confirmed) return;

        await _fleetRepo.DeleteFleetAsync(row.FleetId);
        if (row.IsDefault)
        {
            await _settings.SetDefaultFleetIdAsync(null);
        }
        await LoadFleetsAsync(selectFleetId: null, useDefaultAsSelection: true, default);
    }

    /* ----- internals ----- */

    /// <summary>
    /// Reload the fleet list from the repository, rebuild the rows, and select a
    /// fleet: <paramref name="selectFleetId"/> if given, else the default when
    /// <paramref name="useDefaultAsSelection"/> is true.
    /// </summary>
    private async Task LoadFleetsAsync(string? selectFleetId, bool useDefaultAsSelection, CancellationToken ct)
    {
        var fleets = await _fleetRepo.GetFleetsAsync(ct);
        var defaultId = await _settings.GetDefaultFleetIdAsync(ct);

        foreach (var existing in Fleets)
        {
            existing.PropertyChanged -= OnFleetRowPropertyChanged;
        }
        Fleets.Clear();

        _suppressSelectionCascade = true;
        FleetRowViewModel? toSelect = null;
        foreach (var f in fleets)
        {
            var row = new FleetRowViewModel(f, isDefault: f.FleetId == defaultId);
            row.PropertyChanged += OnFleetRowPropertyChanged;
            Fleets.Add(row);

            if (selectFleetId is not null && f.FleetId == selectFleetId)
            {
                toSelect = row;
            }
            else if (selectFleetId is null && useDefaultAsSelection && row.IsDefault)
            {
                toSelect = row;
            }
        }
        if (toSelect is not null)
        {
            toSelect.IsSelected = true;
        }
        _suppressSelectionCascade = false;

        SelectedFleet = toSelect;
        OnPropertyChanged(nameof(HasNoFleets));
    }

    /// <summary>Rebuild the pilot picker from the current sort, preserving each pilot's pick + role by character id.</summary>
    private void RebuildPilotPicker(bool resetPicks)
    {
        var prior = resetPicks
            ? new Dictionary<long, (bool Selected, PilotRole Role)>()
            : PilotPicker.ToDictionary(
                p => p.Character.CharacterId,
                p => (Selected: p.IsSelected, Role: p.Role));

        foreach (var existing in PilotPicker)
        {
            existing.PropertyChanged -= OnPilotPickerPropertyChanged;
        }
        PilotPicker.Clear();

        foreach (var c in PilotSorter.Sort(_allCharacters, SortOrder))
        {
            var row = new PilotToggleViewModel(c);
            if (prior.TryGetValue(c.CharacterId, out var prev))
            {
                row.IsSelected = prev.Selected;
                row.Role = prev.Role;
            }
            row.PropertyChanged += OnPilotPickerPropertyChanged;
            PilotPicker.Add(row);
        }
        AddFleetCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Persist the new sort + rebuild the picker (preserving picks) when the user changes the sort dropdown.</summary>
    partial void OnSortOrderChanged(PilotSortOrder value)
    {
        if (IsCreatingFleet)
        {
            RebuildPilotPicker(resetPicks: false);
        }
        if (!_suppressSortPersist)
        {
            // Fire-and-forget persistence (mirrors the Crystal Diagram VM's
            // OnSelectedEntryChanged pattern) — the read path defaults safely.
            _ = _settings.SetPilotSortOrderAsync(value);
        }
    }

    private void OnPilotPickerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PilotToggleViewModel.IsSelected))
        {
            AddFleetCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnFleetRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressSelectionCascade) return;
        if (e.PropertyName != nameof(FleetRowViewModel.IsSelected)) return;
        if (sender is not FleetRowViewModel row) return;

        if (row.IsSelected)
        {
            // Single-select: deselect every other row.
            _suppressSelectionCascade = true;
            foreach (var other in Fleets)
            {
                if (!ReferenceEquals(other, row))
                {
                    other.IsSelected = false;
                }
            }
            _suppressSelectionCascade = false;
            SelectedFleet = row;
        }
        else if (ReferenceEquals(SelectedFleet, row))
        {
            SelectedFleet = null;
        }
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionController.SessionName))
        {
            OnPropertyChanged(nameof(SessionName));
            StartCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Dispose: unsubscribe from the controller + all row handlers. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.PropertyChanged -= OnControllerPropertyChanged;
        foreach (var row in Fleets)
        {
            row.PropertyChanged -= OnFleetRowPropertyChanged;
        }
        foreach (var p in PilotPicker)
        {
            p.PropertyChanged -= OnPilotPickerPropertyChanged;
        }
    }
}
