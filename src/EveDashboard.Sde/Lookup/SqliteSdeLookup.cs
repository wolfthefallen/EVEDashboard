namespace EveDashboard.Sde.Lookup;

using System.Diagnostics;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="ISdeLookup"/> implementation. Reads ore + gas rows from
/// the SDE SQLite file via <see cref="ISqliteDatabase"/> and publishes them
/// through two in-memory dictionaries (by name and by type id) referencing the
/// same <see cref="MiningProduct"/> instances.
/// </summary>
/// <remarks>
/// See <see cref="ISdeLookup"/> for the contract. Concurrency model:
/// <list type="bullet">
///   <item><description>Reads use <c>Volatile.Read</c> against the dictionary references — lock-free after the first <see cref="LoadAsync"/>.</description></item>
///   <item><description>Rebuilds publish via <c>Volatile.Write</c> on the same fields — readers see either the old or the new dictionary, never a torn state.</description></item>
///   <item><description>Two simultaneous <see cref="LoadAsync"/> callers serialize via <see cref="SemaphoreSlim"/>; the second runs after the first completes.</description></item>
///   <item><description>Pre-load lookups block on a <see cref="ManualResetEventSlim"/> until the first <see cref="LoadAsync"/> finishes.</description></item>
/// </list>
/// </remarks>
public sealed class SqliteSdeLookup : ISdeLookup, IDisposable
{
    /// <summary>
    /// EVE SDE group id <c>711</c> ("Harvestable Cloud") — the single group
    /// holding every gas type the parser may encounter (fullerites,
    /// cytoserocins, mykoserocins, etc.). Verified against the shipped fallback
    /// SDE snapshot. If CCP renumbers (extremely unlikely),
    /// the lookup tests against gas type ids fail loudly on the next SDE update.
    /// </summary>
    private const long GasGroupId = 711;

    private readonly ISqliteDatabase _db;
    private readonly ILogger<SqliteSdeLookup> _log;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly ManualResetEventSlim _ready = new(initialState: false);

    private Dictionary<string, MiningProduct> _byName =
        new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<long, MiningProduct> _byTypeId = new();
    private Dictionary<long, IReadOnlyList<TypeMaterial>> _typeMaterialsByTypeId = new();
    private IReadOnlyList<long> _refinedMaterialTypeIds = Array.Empty<long>();
    private Dictionary<long, string> _refinedMaterialNames = new();

    /// <summary>Construct a lookup over the given SDE database and logger.</summary>
    /// <param name="db">Open SDE SQLite database (the file at <see cref="EveDashboard.Sde.Storage.ISdeStorage.DatabasePath"/>).</param>
    /// <param name="log">Logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public SqliteSdeLookup(ISqliteDatabase db, ILogger<SqliteSdeLookup> log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(log);
        _db = db;
        _log = log;
    }

    /// <inheritdoc />
    public long? TryGetTypeId(string productName)
    {
        ArgumentNullException.ThrowIfNull(productName);
        _ready.Wait();
        var byName = Volatile.Read(ref _byName);
        return byName.TryGetValue(productName, out var p) ? p.TypeId : null;
    }

    /// <inheritdoc />
    public MiningProduct? TryGetProduct(long typeId)
    {
        _ready.Wait();
        var byId = Volatile.Read(ref _byTypeId);
        return byId.TryGetValue(typeId, out var p) ? p : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId)
    {
        _ready.Wait();
        var byId = Volatile.Read(ref _typeMaterialsByTypeId);
        return byId.TryGetValue(typeId, out var list) ? list : Array.Empty<TypeMaterial>();
    }

    /// <inheritdoc />
    public IReadOnlyList<long> GetRefinedMaterialTypeIds()
    {
        _ready.Wait();
        return Volatile.Read(ref _refinedMaterialTypeIds);
    }

    /// <inheritdoc />
    public string? TryGetRefinedMaterialName(long typeId)
    {
        _ready.Wait();
        var byId = Volatile.Read(ref _refinedMaterialNames);
        return byId.TryGetValue(typeId, out var name) ? name : null;
    }

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _loadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sw = Stopwatch.StartNew();

            var rows = await _db.QueryAsync(
                """
                SELECT type_id, name, volume, portion_size
                FROM types
                WHERE is_ore = 1 OR group_id = $gas
                """,
                static r => new MiningProduct(
                    TypeId: r.GetInt64(0),
                    Name: r.GetString(1),
                    VolumePerUnitM3: r.GetDouble(2),
                    PortionSize: r.GetInt32(3)),
                new Dictionary<string, object?> { ["$gas"] = GasGroupId },
                ct).ConfigureAwait(false);

            var byName = new Dictionary<string, MiningProduct>(
                rows.Count, StringComparer.OrdinalIgnoreCase);
            var byId = new Dictionary<long, MiningProduct>(rows.Count);
            foreach (var p in rows)
            {
                byName[p.Name] = p;
                byId[p.TypeId] = p;
            }

            // Load the type_materials recipe for every is_ore = 1 type.
            // Drives both IskCalculator (recursive reprocessing) and
            // MarketPriceFetcher (the prefetch type-id list).
            // The JOIN to the material's own `types` row carries its
            // display name (Tritanium, etc.) so we can cache refined-material
            // names — these are NOT in the ore/gas MiningProduct cache.
            var materialRows = await _db.QueryAsync(
                """
                SELECT tm.type_id, tm.material_type_id, tm.quantity, mt.name
                FROM type_materials tm
                JOIN types t  ON t.type_id  = tm.type_id
                JOIN types mt ON mt.type_id = tm.material_type_id
                WHERE t.is_ore = 1
                ORDER BY tm.type_id, tm.material_type_id
                """,
                static r => (
                    TypeId: r.GetInt64(0),
                    MaterialTypeId: r.GetInt64(1),
                    Quantity: r.GetInt64(2),
                    MaterialName: r.GetString(3)),
                null,
                ct).ConfigureAwait(false);

            var typeMaterialsByTypeId = new Dictionary<long, IReadOnlyList<TypeMaterial>>();
            var refinedSet = new HashSet<long>();
            var refinedNames = new Dictionary<long, string>();
            var pendingByType = new Dictionary<long, List<TypeMaterial>>();
            foreach (var m in materialRows)
            {
                if (!pendingByType.TryGetValue(m.TypeId, out var list))
                {
                    list = new List<TypeMaterial>();
                    pendingByType[m.TypeId] = list;
                }
                list.Add(new TypeMaterial(m.MaterialTypeId, m.Quantity));
                refinedSet.Add(m.MaterialTypeId);
                refinedNames[m.MaterialTypeId] = m.MaterialName;
            }
            foreach (var kvp in pendingByType)
            {
                typeMaterialsByTypeId[kvp.Key] = kvp.Value;
            }
            var refinedTypeIds = refinedSet.ToArray();

            Volatile.Write(ref _byName, byName);
            Volatile.Write(ref _byTypeId, byId);
            Volatile.Write(ref _typeMaterialsByTypeId, typeMaterialsByTypeId);
            Volatile.Write(ref _refinedMaterialTypeIds, refinedTypeIds);
            Volatile.Write(ref _refinedMaterialNames, refinedNames);
            _ready.Set();

            _log.LogInformation(
                "SDE lookup loaded {Count} ore+gas types ({OreRecipes} ore recipes, {RefinedIds} refined ids) in {Ms} ms",
                rows.Count, typeMaterialsByTypeId.Count, refinedTypeIds.Length, sw.ElapsedMilliseconds);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    /// <summary>Releases the semaphore and gate handles.</summary>
    public void Dispose()
    {
        _loadGate.Dispose();
        _ready.Dispose();
    }
}
