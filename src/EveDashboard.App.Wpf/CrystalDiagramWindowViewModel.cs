namespace EveDashboard.App.Wpf;

using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.Core.Settings;
using Microsoft.Extensions.Logging;

/// <summary>
/// Plan 35 VM for <see cref="CrystalDiagramWindow"/>. Owns the static
/// <see cref="Entries"/> list (8 entries: <c>all</c> + 7 nullsec SOV asteroid
/// deposits), the currently-selected entry, and the persistence write-back
/// to <see cref="ISettingsRepository.SetSelectedCrystalAnomalyAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Singleton lifetime: matches the singleton <see cref="CrystalDiagramWindow"/>
/// so the dropdown selection survives in-session hide/show cycles. The persisted
/// id (key <c>crystal_diagram_selected_anomaly</c>) is read by
/// <see cref="InitializeAsync"/> on first window open and applied to
/// <see cref="SelectedEntry"/>; from then on, every user pick triggers a
/// fire-and-forget settings write via the CommunityToolkit-generated
/// <c>OnSelectedEntryChanged</c> partial-method hook.
/// </para>
/// <para>
/// Unknown / null persisted id => fall back to <c>"all"</c> (the first entry).
/// This is also the first-ever-launch case.
/// </para>
/// </remarks>
public sealed partial class CrystalDiagramWindowViewModel : ObservableObject
{
    private const string AllId = "all";
    private const string AnomalyAssetPrefix = "pack://application:,,,/Assets/anomaly_";

    private readonly ISettingsRepository _settings;
    private readonly ILogger<CrystalDiagramWindowViewModel> _log;
    private bool _suppressPersist;

    /// <summary>
    /// The 8 dropdown entries. Order matches plan 35 (All first, then the
    /// 7 deposits in visual reading order of the source PNG).
    /// </summary>
    public IReadOnlyList<CrystalAnomalyEntry> Entries { get; } = new[]
    {
        new CrystalAnomalyEntry(AllId,         "All / Full Chart",     "pack://application:,,,/Assets/Mineral_to_asteroid_crystal.png"),
        new CrystalAnomalyEntry("griemeer",    "Griemeer (Isogen)",    AnomalyAssetPrefix + "griemeer.png"),
        new CrystalAnomalyEntry("veldspar",    "Veldspar (Tritanium)", AnomalyAssetPrefix + "veldspar.png"),
        new CrystalAnomalyEntry("kylixium",    "Kylixium (Mexallon)",  AnomalyAssetPrefix + "kylixium.png"),
        new CrystalAnomalyEntry("hezorime",    "Hezorime (Zydrine)",   AnomalyAssetPrefix + "hezorime.png"),
        new CrystalAnomalyEntry("ueganite",    "Ueganite (Megacyte)",  AnomalyAssetPrefix + "ueganite.png"),
        new CrystalAnomalyEntry("nocxite",     "Nocxite (Nocxium)",    AnomalyAssetPrefix + "nocxite.png"),
        new CrystalAnomalyEntry("mordunium",   "Mordunium (Pyerite)",  AnomalyAssetPrefix + "mordunium.png"),
    };

    /// <summary>
    /// The currently-displayed entry. Two-way bound to the window's ComboBox
    /// <c>SelectedItem</c>; its <see cref="CrystalAnomalyEntry.ImagePackUri"/>
    /// drives the window's <c>&lt;Image&gt; Source</c>.
    /// </summary>
    [ObservableProperty]
    private CrystalAnomalyEntry _selectedEntry;

    /// <summary>Construct the VM. <see cref="SelectedEntry"/> starts at the <c>all</c>
    /// entry until <see cref="InitializeAsync"/> applies any persisted preference.</summary>
    /// <param name="settings">Settings repository.</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public CrystalDiagramWindowViewModel(
        ISettingsRepository settings,
        ILogger<CrystalDiagramWindowViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);
        _settings = settings;
        _log = log;
        _selectedEntry = Entries[0];
    }

    /// <summary>
    /// Read the persisted anomaly id and set <see cref="SelectedEntry"/>
    /// accordingly. Called once from <see cref="CrystalDiagramWindow.OnLoaded"/>.
    /// Null / unknown id falls back to the <c>all</c> entry. The selection write
    /// triggered by this method is suppressed so we do not persist back the value
    /// we just read.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        string? persistedId;
        try
        {
            persistedId = await _settings.GetSelectedCrystalAnomalyAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Crystal Diagram VM: failed to read persisted selection; defaulting to 'all'.");
            persistedId = null;
        }

        var match = persistedId is null
            ? Entries[0]
            : Entries.FirstOrDefault(e => e.Id == persistedId) ?? Entries[0];

        _suppressPersist = true;
        try
        {
            SelectedEntry = match;
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    partial void OnSelectedEntryChanged(CrystalAnomalyEntry value)
    {
        if (_suppressPersist || value is null) return;

        // Fire-and-forget write. The SettingsRepository write is a single-row
        // UPSERT; failure is logged at Warning and the next user pick will
        // retry. This matches the in-flight-fire-and-forget pattern used by
        // other VM->settings writes in the App.Wpf layer.
        _ = PersistAsync(value.Id);
    }

    private async Task PersistAsync(string id)
    {
        try
        {
            await _settings.SetSelectedCrystalAnomalyAsync(id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Crystal Diagram VM: failed to persist selected anomaly id {Id}.", id);
        }
    }
}
