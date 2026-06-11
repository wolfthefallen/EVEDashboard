namespace EveDashboard.App.Wpf.Widgets.Session;

using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Sessions;

/// <summary>
/// One row of the fleet-builder pilot picker — a <see cref="Character"/> plus a
/// togglable <see cref="IsSelected"/> bool and a selectable <see cref="Role"/>
/// (plan 42; the picker lives in the New Session dialog).
/// </summary>
public sealed partial class PilotToggleViewModel : ObservableObject
{
    /// <summary>The discovered EVE character this row represents.</summary>
    public Character Character { get; }

    /// <summary>Whether the user has toggled this pilot on for the fleet being built.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>The role the user has assigned this pilot in the fleet (defaults to <see cref="PilotRole.Miner"/>). Used only by the picker.</summary>
    [ObservableProperty]
    private PilotRole _role = PilotRole.Miner;

    /// <summary>Construct a row for one pilot, initially unselected.</summary>
    /// <param name="character">The EVE character — must be non-null.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="character"/> is null.</exception>
    public PilotToggleViewModel(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);
        Character = character;
    }
}
