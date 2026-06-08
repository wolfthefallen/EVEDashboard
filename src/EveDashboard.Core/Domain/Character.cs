namespace EveDashboard.Core.Domain;

/// <summary>
/// An EVE character whose gamelog this app is following.
/// </summary>
/// <param name="CharacterId">EVE character ID (parsed from the gamelog filename).</param>
/// <param name="Name">Display name (read from the <c>Listener:</c> header line).</param>
/// <param name="LogFilePath">Absolute path to the gamelog file currently being followed for this character.</param>
public sealed record Character(
    long CharacterId,
    string Name,
    string LogFilePath);
