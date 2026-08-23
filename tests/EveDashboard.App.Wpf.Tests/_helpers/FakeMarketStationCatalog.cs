namespace EveDashboard.App.Wpf.Tests.Helpers;

using EveDashboard.Core.Settings;

/// <summary>
/// Hand-rolled fake of <see cref="IMarketStationCatalog"/> for unit tests
/// (no Moq / NSubstitute in this project). Stored stations live in an ordered dictionary;
/// <see cref="GetAllAsync"/> returns a snapshot list. Counters expose Add /
/// Remove invocations so VM tests can assert pending markets edits flush
/// to the catalog only on Apply.
/// </summary>
internal sealed class FakeMarketStationCatalog : IMarketStationCatalog
{
    private readonly Dictionary<long, MarketStation> _stations = new();

    public int AddCount { get; private set; }
    public int RemoveCount { get; private set; }
    public int UpdateCount { get; private set; }
    public List<long> RemovedIds { get; } = new();
    public List<MarketStation> AddedStations { get; } = new();

    public FakeMarketStationCatalog(params MarketStation[] initial)
    {
        foreach (var s in initial)
        {
            _stations[s.StationId] = s;
        }
    }

    public Task<IReadOnlyList<MarketStation>> GetAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<MarketStation> result = _stations.Values.OrderBy(s => s.SystemName).ToList();
        return Task.FromResult(result);
    }

    public Task<MarketStation?> GetAsync(long stationId, CancellationToken ct = default)
        => Task.FromResult(_stations.GetValueOrDefault(stationId));

    public Task AddAsync(MarketStation station, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(station);
        _stations[station.StationId] = station;
        AddCount++;
        AddedStations.Add(station);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(MarketStation station, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(station);
        if (_stations.ContainsKey(station.StationId))
        {
            _stations[station.StationId] = station;
        }
        UpdateCount++;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(long stationId, CancellationToken ct = default)
    {
        _stations.Remove(stationId);
        RemoveCount++;
        RemovedIds.Add(stationId);
        return Task.CompletedTask;
    }

    public Task EnsureSeedDefaultsAsync(CancellationToken ct = default) => Task.CompletedTask;
}
