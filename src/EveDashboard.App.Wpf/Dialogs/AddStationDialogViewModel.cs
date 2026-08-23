namespace EveDashboard.App.Wpf.Dialogs;

using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using EveDashboard.Core.Settings;

/// <summary>
/// ViewModel for the Add-station sub-dialog. Holds the five input fields,
/// validates them, and projects a <see cref="MarketStation"/> record on success.
/// Pure POCO: no WPF refs beyond <see cref="INotifyPropertyChanged"/>.
/// </summary>
/// <remarks>
/// Field shape mirrors <see cref="MarketStation"/> exactly. Region id and region name
/// are optional; if region id is supplied it must parse as a positive
/// <see cref="long"/>. Validation runs once on OK click via
/// <see cref="Validate"/>; <see cref="BuildStation"/> assumes validated state
/// and may throw if called against unvalidated input.
/// </remarks>
public sealed class AddStationDialogViewModel : INotifyPropertyChanged
{
    private string _stationIdText = string.Empty;
    private string _systemName = string.Empty;
    private string _stationName = string.Empty;
    private string _regionIdText = string.Empty;
    private string _regionName = string.Empty;

    /// <summary>EVE station id as a string. Required; must parse as a positive <see cref="long"/>.</summary>
    public string StationIdText
    {
        get => _stationIdText;
        set => SetField(ref _stationIdText, value ?? string.Empty);
    }

    /// <summary>Solar system display name (e.g. <c>"Jita"</c>). Required.</summary>
    public string SystemName
    {
        get => _systemName;
        set => SetField(ref _systemName, value ?? string.Empty);
    }

    /// <summary>Station display name without the system prefix. Required.</summary>
    public string StationName
    {
        get => _stationName;
        set => SetField(ref _stationName, value ?? string.Empty);
    }

    /// <summary>Region id as a string. Optional; if supplied, must parse as a positive <see cref="long"/>.</summary>
    public string RegionIdText
    {
        get => _regionIdText;
        set => SetField(ref _regionIdText, value ?? string.Empty);
    }

    /// <summary>Region display name. Optional.</summary>
    public string RegionName
    {
        get => _regionName;
        set => SetField(ref _regionName, value ?? string.Empty);
    }

    /// <summary>
    /// Validate every field. Returns an empty list when the inputs are
    /// acceptable; otherwise one human-readable error string per problem.
    /// </summary>
    /// <returns>Zero or more error messages, in field order (station id, system, station, region id).</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!long.TryParse(_stationIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stationId)
            || stationId <= 0)
        {
            errors.Add("Station ID must be a positive integer.");
        }

        if (string.IsNullOrWhiteSpace(_systemName))
        {
            errors.Add("System name is required.");
        }

        if (string.IsNullOrWhiteSpace(_stationName))
        {
            errors.Add("Station name is required.");
        }

        if (!string.IsNullOrWhiteSpace(_regionIdText))
        {
            if (!long.TryParse(_regionIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var regionId)
                || regionId <= 0)
            {
                errors.Add("Region ID, if provided, must be a positive integer.");
            }
        }

        return errors;
    }

    /// <summary>
    /// Project the validated input into a <see cref="MarketStation"/> record.
    /// Optional region id and region name become <c>null</c> when blank.
    /// </summary>
    /// <returns>The constructed <see cref="MarketStation"/>.</returns>
    /// <exception cref="FormatException">Thrown if <see cref="StationIdText"/> is not parseable. Call <see cref="Validate"/> first.</exception>
    public MarketStation BuildStation()
    {
        var stationId = long.Parse(_stationIdText, NumberStyles.Integer, CultureInfo.InvariantCulture);
        long? regionId = string.IsNullOrWhiteSpace(_regionIdText)
            ? null
            : long.Parse(_regionIdText, NumberStyles.Integer, CultureInfo.InvariantCulture);
        var regionName = string.IsNullOrWhiteSpace(_regionName) ? null : _regionName;
        var systemName = _systemName.Trim();
        var stationName = _stationName.Trim();
        return new MarketStation(stationId, systemName, stationName, regionId, regionName);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (string.Equals(field, value, StringComparison.Ordinal)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
    }
}
