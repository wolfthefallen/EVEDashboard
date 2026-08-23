namespace EveDashboard.App.Wpf.Dialogs;

using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.Core.Sessions;

/// <summary>
/// One row in the New Session dialog's fleet list: a saved fleet with
/// a togglable <see cref="IsSelected"/> (single-select enforced by the parent
/// VM), an <see cref="IsExpanded"/> caret, and an <see cref="IsDefault"/> flag.
/// </summary>
public sealed partial class FleetRowViewModel : ObservableObject
{
    /// <summary>Content-addressed id of the fleet this row represents.</summary>
    public string FleetId { get; }

    /// <summary>Display name of the fleet.</summary>
    public string Name { get; }

    /// <summary>The fleet's members + roles (source of truth at Start).</summary>
    public IReadOnlyList<FleetMember> Members { get; }

    /// <summary>One "Name — Role" line per member, for the caret expander.</summary>
    public IReadOnlyList<string> MemberLines { get; }

    /// <summary>Whether this fleet is the persisted default (drives a marker in the row).</summary>
    [ObservableProperty]
    private bool _isDefault;

    /// <summary>Whether this fleet is the one selected to Start with. Single-select is enforced by the parent VM.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Whether the caret expander is open, showing this fleet's pilots + roles.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Build a row from a saved fleet.</summary>
    /// <param name="fleet">The fleet to represent — must be non-null.</param>
    /// <param name="isDefault">Whether this fleet is the persisted default.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="fleet"/> is null.</exception>
    public FleetRowViewModel(Fleet fleet, bool isDefault)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        FleetId = fleet.FleetId;
        Name = fleet.Name;
        Members = fleet.Members;
        MemberLines = fleet.Members
            .Select(m => $"{m.Name}  —  {PilotRoleExtensions.ToCanonicalString(m.Role)}")
            .ToList();
        IsDefault = isDefault;
    }
}
