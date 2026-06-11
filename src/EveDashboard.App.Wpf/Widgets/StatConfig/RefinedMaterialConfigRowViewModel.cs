namespace EveDashboard.App.Wpf.Widgets.StatConfig;

using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>
/// Plan 43 (decision 2.1 = C): one row in the stat-config cog popup's "Refined
/// materials" subsection — a seen material's name and an editable
/// <see cref="Visible"/> checkbox. On Apply the popup turns the checkbox into a
/// per-widget visibility override (or drops the override when it matches the
/// auto-show default).
/// </summary>
public sealed partial class RefinedMaterialConfigRowViewModel : ObservableObject
{
    /// <summary>EVE refined-material type id this row controls.</summary>
    public long TypeId { get; }

    /// <summary>Display name of the material.</summary>
    public string Name { get; }

    /// <summary>Whether this material is shown in the widget's "Refined output" section.</summary>
    [ObservableProperty]
    private bool _visible;

    /// <summary>Construct a cog-popup refined-material row.</summary>
    /// <param name="typeId">Material type id.</param>
    /// <param name="name">Material display name.</param>
    /// <param name="visible">Current resolved visibility.</param>
    public RefinedMaterialConfigRowViewModel(long typeId, string name, bool visible)
    {
        TypeId = typeId;
        Name = name;
        _visible = visible;
    }
}
