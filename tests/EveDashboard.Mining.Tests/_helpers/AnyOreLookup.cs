namespace EveDashboard.Mining.Tests.Helpers;

using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;

/// <summary>
/// Minimal <see cref="ISdeLookup"/>: resolves every product name to one type id
/// so the real parser emits a Tick/Crit for every mining line. Shared by the
/// historical reader and inventory tests, which care about timestamps, file
/// selection and counts rather than ore identity.
/// </summary>
internal sealed class AnyOreLookup : ISdeLookup
{
    public long? TryGetTypeId(string productName) => 1230L;
    public MiningProduct? TryGetProduct(long typeId) => null;
    public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId) => Array.Empty<TypeMaterial>();
    public IReadOnlyList<long> GetRefinedMaterialTypeIds() => Array.Empty<long>();
    public string? TryGetRefinedMaterialName(long typeId) => null;
    public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
}
