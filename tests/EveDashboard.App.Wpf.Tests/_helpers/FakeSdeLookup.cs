namespace EveDashboard.App.Wpf.Tests.Helpers;

using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;

/// <summary>Returns null for every lookup; sufficient for SessionController tests where no events flow.</summary>
internal sealed class FakeSdeLookup : ISdeLookup
{
    public long? TryGetTypeId(string productName) => null;
    public MiningProduct? TryGetProduct(long typeId) => null;
    public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId) => Array.Empty<TypeMaterial>();
    public IReadOnlyList<long> GetRefinedMaterialTypeIds() => Array.Empty<long>();

    /// <summary>Plan 43: optional refined-material names a test can register for the refined-output path.</summary>
    public Dictionary<long, string> RefinedNames { get; } = new();
    public string? TryGetRefinedMaterialName(long typeId) =>
        RefinedNames.TryGetValue(typeId, out var name) ? name : null;

    public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
}
