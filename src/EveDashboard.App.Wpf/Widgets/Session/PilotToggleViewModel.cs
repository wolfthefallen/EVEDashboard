namespace EveDashboard.App.Wpf.Widgets.Session;

using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.Core.Domain;

/// <summary>
/// One row of the Config-mode pilot list — a <see cref="Character"/>
/// plus a togglable <see cref="IsSelected"/> bool.
/// </summary>
public sealed partial class PilotToggleViewModel : ObservableObject
{
    /// <summary>The discovered EVE character this row represents.</summary>
    public Character Character { get; }

    /// <summary>Whether the user has toggled this pilot on for the next Start.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Construct a row for one pilot, initially unselected.</summary>
    /// <param name="character">The EVE character — must be non-null.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="character"/> is null.</exception>
    public PilotToggleViewModel(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);
        Character = character;
    }
}
