namespace EveDashboard.Core.Lookup;

/// <summary>
/// Resolves an arbitrary EVE type <em>display name</em> to its type id against
/// the full SDE <c>types</c> table (not just the ore/gas cache that
/// <see cref="ISdeLookup.TryGetTypeId"/> covers). Used by the Haul Value paste
/// box, where the user may paste ANY item (modules, ships, PLEX, ...), not only
/// mined ore/minerals.
/// </summary>
public interface ISdeTypeNameResolver
{
    /// <summary>
    /// Resolve <paramref name="name"/> (case-insensitive, exact match) to its EVE
    /// type id, or <c>null</c> when no type has that name.
    /// </summary>
    /// <param name="name">EVE item display name, e.g. <c>"Tritanium"</c> or <c>"Rifter"</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The type id, or <c>null</c> if unknown.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="name"/> is null.</exception>
    Task<long?> ResolveTypeIdAsync(string name, CancellationToken ct = default);
}
