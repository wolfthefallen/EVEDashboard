namespace EveDashboard.App.Wpf.Tests.Helpers;

using EveDashboard.Core.Domain;
using EveDashboard.Core.Sessions.Historical;
using EveDashboard.MarketData.Pricing;
using EveDashboard.Mining.Historical;
using EveDashboard.Stats.Historical;

/// <summary>
/// Test doubles for the historical-session pipeline, shared by the dialog
/// view-model tests and the daily-batch runner tests - the two callers that
/// compose the same four seams.
/// </summary>
internal sealed class FakeSpanQuery : ISessionSpanQuery
{
    /// <summary>The saved sessions the store is pretending to hold.</summary>
    public List<SessionSpan> Spans { get; } = new();

    /// <summary>How many times the overlap query was asked.</summary>
    public int OverlapCalls { get; private set; }

    /// <summary>How many times the whole-store query was asked.</summary>
    public int GetAllCalls { get; private set; }

    public Task<IReadOnlyList<SessionSpan>> GetOverlappingAsync(DateTimeOffset windowStart, DateTimeOffset windowStop, DateTimeOffset? asOf = null, CancellationToken ct = default)
    {
        OverlapCalls++;
        return Task.FromResult<IReadOnlyList<SessionSpan>>(Spans.Where(s => s.Start <= windowStop && s.Stop >= windowStart).OrderBy(s => s.Start).ToList());
    }

    public Task<IReadOnlyList<SessionSpan>> GetSpansAsync(IReadOnlyCollection<string> sessionIds, DateTimeOffset? asOf = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SessionSpan>>(Spans.Where(s => sessionIds.Contains(s.SessionId)).OrderBy(s => s.Start).ToList());

    public Task<IReadOnlyList<SessionSpan>> GetAllAsync(DateTimeOffset? asOf = null, CancellationToken ct = default)
    {
        GetAllCalls++;
        return Task.FromResult<IReadOnlyList<SessionSpan>>(Spans.OrderBy(s => s.Start).ToList());
    }
}

/// <summary>Gamelog reader double: canned inventory and canned window read.</summary>
internal sealed class FakeReader : IHistoricalGamelogReader
{
    /// <summary>What <see cref="ReadAsync"/> returns.</summary>
    public HistoricalReadResult Result { get; set; } = new(Array.Empty<MiningEvent>(), Array.Empty<string>(), Array.Empty<long>());

    /// <summary>What <see cref="InventoryAsync"/> returns.</summary>
    public GamelogInventory Inventory { get; set; } = new(Array.Empty<GamelogInventoryEntry>(), null, null);

    public int Calls { get; private set; }
    public int InventoryCalls { get; private set; }
    public IReadOnlyCollection<long>? LastPilotIds { get; private set; }
    public DateTimeOffset LastStart { get; private set; }
    public DateTimeOffset LastStop { get; private set; }

    /// <summary>The instant the canned inventory claims its logs were last written.</summary>
    /// <remarks>
    /// Relative to now, not a fixed date: a window taken from the inventory has
    /// to be in the past to be valid, so a hard-coded date would make these tests
    /// depend on the machine's clock being later than it.
    /// </remarks>
    public static DateTimeOffset InventoryAt => DateTimeOffset.UtcNow.AddDays(-10);

    /// <summary>Set the inventory from a simple pilot id / name list.</summary>
    /// <param name="pilots">Pilot ids and names to report as having logs.</param>
    public void SetInventory(params (long Id, string Name)[] pilots)
    {
        var at = InventoryAt;
        Inventory = new GamelogInventory(
            pilots.Select(p => new GamelogInventoryEntry(p.Id, p.Name, at, at, 1)).ToList(),
            pilots.Length == 0 ? null : at,
            pilots.Length == 0 ? null : at);
    }

    public Task<HistoricalReadResult> ReadAsync(IReadOnlyCollection<long> pilotIds, DateTimeOffset windowStart, DateTimeOffset windowStop, CancellationToken ct = default)
    {
        Calls++; LastPilotIds = pilotIds; LastStart = windowStart; LastStop = windowStop;
        return Task.FromResult(Result);
    }

    public Task<GamelogInventory> InventoryAsync(CancellationToken ct = default)
    {
        InventoryCalls++;
        return Task.FromResult(Inventory);
    }
}

/// <summary>Session builder double: records every request, can be told to fail.</summary>
internal sealed class FakeBuilder : IHistoricalSessionBuilder
{
    /// <summary>Every request it was handed, in order.</summary>
    public List<HistoricalSessionRequest> Requests { get; } = new();

    /// <summary>The most recent request, or <c>null</c> if it was never called.</summary>
    public HistoricalSessionRequest? LastRequest => Requests.Count == 0 ? null : Requests[^1];

    /// <summary>When set, every call throws this.</summary>
    public Exception? Throw { get; set; }

    /// <summary>When set, a call throws whatever this returns for its request - for failing one day of a batch.</summary>
    public Func<HistoricalSessionRequest, Exception?>? ThrowFor { get; set; }

    public Task<HistoricalSessionResult> BuildAsync(HistoricalSessionRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        var boom = Throw ?? ThrowFor?.Invoke(request);
        if (boom is not null) throw boom;

        // The first id stays "new-id" so single-build expectations read naturally;
        // a batch needs the rest to differ, since the real builder's ids are a
        // hash of name plus start.
        var id = Requests.Count == 1 ? "new-id" : "new-id-" + Requests.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Task.FromResult(new HistoricalSessionResult(
            id, request.WindowStart, request.WindowStop, 3, 4, 0, 0, 0, 1, 2, request.SessionIdsToMerge));
    }
}

/// <summary>Market pricer double for both the single and the batch path.</summary>
internal sealed class FakePricer : IHistoricalMarketPricer
{
    /// <summary>The outcome handed back; the batch path clones it per target.</summary>
    public HistoricalPricingResult Result { get; set; } =
        new("new-id", 60003760, HistoricalPriceBasis.AveragedFromWindow, 1, 1, 0, 0);

    public string? LastSessionId { get; private set; }
    public DateTimeOffset LastStart { get; private set; }
    public DateTimeOffset LastStop { get; private set; }

    /// <summary>Every batch call's targets, so a test can assert the market was asked once.</summary>
    public List<IReadOnlyList<HistoricalPricingTarget>> BatchCalls { get; } = new();

    public Task<HistoricalPricingResult> PriceAsync(string sessionId, DateTimeOffset windowStart, DateTimeOffset windowStop, CancellationToken ct = default)
    {
        LastSessionId = sessionId; LastStart = windowStart; LastStop = windowStop;
        return Task.FromResult(Result);
    }

    public Task<IReadOnlyList<HistoricalPricingResult>> PriceBatchAsync(IReadOnlyList<HistoricalPricingTarget> targets, CancellationToken ct = default)
    {
        BatchCalls.Add(targets);
        return Task.FromResult<IReadOnlyList<HistoricalPricingResult>>(
            targets.Select(t => Result with { SessionId = t.SessionId }).ToList());
    }
}
