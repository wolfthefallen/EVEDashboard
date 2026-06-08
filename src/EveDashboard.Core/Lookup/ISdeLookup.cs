namespace EveDashboard.Core.Lookup;

using EveDashboard.Core.Domain;

/// <summary>
/// Runtime lookup service for the EVE Static Data Export (SDE). Owns an
/// in-memory dictionary of every mineable item — all ore (<c>is_ore = 1</c>,
/// covering asteroid + moon + ice as a single bucket per
/// <c>MEMORY.md → feedback_ore_is_just_ore</c>) and all gas
/// (<c>group_id = 711</c>, "Harvestable Cloud") — keyed by both EVE display
/// name (case-insensitive) and EVE type id.
/// </summary>
/// <remarks>
/// <para>
/// Plan 09's deliverable. The interface lives in <c>EveDashboard.Core.Lookup</c>
/// (relocated by plan 10 — see TODO.md "Amendments to completed plans") so
/// peer projects (notably <c>EveDashboard.Mining</c>) can consume it without taking
/// a peer dependency on <c>EveDashboard.Sde</c>; the implementation
/// <c>SqliteSdeLookup</c> still lives in <c>EveDashboard.Sde.Lookup</c>.
/// Refined outputs (Tritanium, moon materials, ice products) are intentionally
/// NOT cached here — the parser only ever encounters ore/gas names from
/// gamelog lines, and the recursive ISK pipeline queries
/// <c>type_materials</c> and <c>portion_size</c> for refined outputs directly
/// from the SDE SQLite at math time
/// (see <c>MEMORY.md → project_isk_calculation</c>).
/// </para>
/// <para>
/// <b>Wiring (App-driven, not self-subscribing).</b> The lookup does not take
/// <c>ISdeUpdater</c> in its constructor. Plan 16's <c>App.xaml.cs OnStartup</c>
/// runs the bootstrap sequence
/// <c>storage.EnsureDatabaseExistsAsync</c> →
/// <c>updater.CheckAndUpdateAsync</c> →
/// <c>lookup.LoadAsync</c>, then wires the updater's <c>Updated</c> event to
/// call <see cref="LoadAsync"/> again on subsequent runs (relevant when the v3
/// manual-update GUI button lands).
/// </para>
/// <para>
/// <b>Block-and-queue gate.</b> Lookups called BEFORE the first
/// <see cref="LoadAsync"/> completes block until the cache is ready (no events
/// dropped). Lookups during a SUBSEQUENT <see cref="LoadAsync"/> do NOT block —
/// readers continue serving the previously-published dictionaries via atomic
/// swap until the new dictionaries replace them.
/// </para>
/// </remarks>
public interface ISdeLookup
{
    /// <summary>
    /// Resolve an EVE product name (e.g. <c>"Veldspar"</c>) to its type id.
    /// Case-insensitive (<see cref="System.StringComparer.OrdinalIgnoreCase"/>).
    /// </summary>
    /// <param name="productName">Product display name as it appears in EVE log lines.</param>
    /// <returns>The EVE type id, or <c>null</c> if no ore/gas with that name is in the cache.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="productName"/> is null.</exception>
    /// <remarks>
    /// Blocks until the first <see cref="LoadAsync"/> completes if called
    /// before the cache is ready.
    /// </remarks>
    long? TryGetTypeId(string productName);

    /// <summary>
    /// Resolve an EVE type id to its <see cref="MiningProduct"/> value object.
    /// </summary>
    /// <param name="typeId">EVE type id.</param>
    /// <returns>
    /// The matching <see cref="MiningProduct"/>, or <c>null</c> if the type id
    /// is not in the cached set (i.e. it is not ore and not gas — refined
    /// outputs and unrelated EVE items return <c>null</c>).
    /// </returns>
    /// <remarks>
    /// Blocks until the first <see cref="LoadAsync"/> completes if called
    /// before the cache is ready.
    /// </remarks>
    MiningProduct? TryGetProduct(long typeId);

    /// <summary>
    /// Refining outputs for an ore type. Empty list when the type id is gas,
    /// scrap, refined output, or unknown.
    /// </summary>
    /// <param name="typeId">EVE type id to look up the refining recipe for.</param>
    /// <returns>The refining outputs (material type id + per-batch quantity), or an empty list.</returns>
    /// <remarks>
    /// Blocks on the same block-and-queue gate as <see cref="TryGetProduct"/>
    /// until the first <see cref="LoadAsync"/> completes. Backed by a single
    /// SQL query against <c>type_materials JOIN types ON ... WHERE is_ore = 1</c>
    /// during <see cref="LoadAsync"/>; subsequent reads are O(1) dictionary
    /// lookups against the in-memory cache.
    /// </remarks>
    IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId);

    /// <summary>
    /// Distinct set of refined-output type ids — the union of
    /// <c>material_type_id</c> from <c>type_materials</c> for every
    /// <c>is_ore = 1</c> type. Used to build the prefetch URL for the
    /// market-price fetcher.
    /// </summary>
    /// <returns>The distinct refined-material type ids (order is implementation-defined but stable for the lifetime of one <see cref="LoadAsync"/>).</returns>
    /// <remarks>
    /// Recomputed on each <see cref="LoadAsync"/>; new EVE moon materials in a
    /// fresh SDE flow in automatically. Blocks on the same gate as the other
    /// methods until the first <see cref="LoadAsync"/> completes.
    /// </remarks>
    IReadOnlyList<long> GetRefinedMaterialTypeIds();

    /// <summary>
    /// Build (or rebuild) the in-memory cache by querying the SDE SQLite
    /// database. Concurrent invocations serialize via an internal semaphore.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the cache has been published.</returns>
    /// <remarks>
    /// On the first successful load the block-and-queue gate is set, releasing
    /// any pending <see cref="TryGetTypeId"/> / <see cref="TryGetProduct"/>
    /// callers. Subsequent successful loads atomically swap the published
    /// dictionaries via <see cref="System.Threading.Volatile"/>; concurrent
    /// readers never block during a swap.
    /// </remarks>
    Task LoadAsync(CancellationToken ct = default);
}
