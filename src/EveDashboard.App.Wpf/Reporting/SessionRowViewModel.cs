namespace EveDashboard.App.Wpf.Reporting;

using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.Reporting;

/// <summary>
/// Sidebar-row wrapper around a <see cref="SessionSummaryRow"/> that adds an
/// <see cref="IsChecked"/> observable property bound to the DataGrid's
/// checkbox column. Compare-mode selection is driven by which wrappers are
/// checked, not by the WPF DataGrid's row-highlight selection (which is now
/// purely a visual affordance for right-click context).
/// </summary>
/// <remarks>
/// Display properties (<see cref="Name"/>, <see cref="StartTime"/>, etc.) are
/// pass-throughs to <see cref="Data"/> so the existing DataGrid XAML column
/// bindings continue to resolve unchanged. Only <see cref="IsChecked"/> is
/// genuinely observable.
/// </remarks>
public sealed partial class SessionRowViewModel : ObservableObject
{
    /// <summary>The wrapped data record. Immutable for the wrapper's lifetime.</summary>
    public SessionSummaryRow Data { get; }

    /// <summary>Whether this row is included in the compare-mode selection. Two-way bound to the sidebar checkbox column.</summary>
    [ObservableProperty]
    private bool _isChecked;

    /// <summary>Construct a wrapper around an existing data row.</summary>
    /// <param name="data">The data record to wrap. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="data"/> is null.</exception>
    public SessionRowViewModel(SessionSummaryRow data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
    }

    /// <summary>Pass-through to <see cref="SessionSummaryRow.SessionId"/>.</summary>
    public string SessionId => Data.SessionId;

    /// <summary>Pass-through to <see cref="SessionSummaryRow.Name"/>.</summary>
    public string Name => Data.Name;

    /// <summary>Pass-through to <see cref="SessionSummaryRow.StartTime"/>.</summary>
    public DateTimeOffset StartTime => Data.StartTime;

    /// <summary>Pass-through to <see cref="SessionSummaryRow.ActiveDuration"/>.</summary>
    public TimeSpan ActiveDuration => Data.ActiveDuration;

    /// <summary>Pass-through to <see cref="SessionSummaryRow.PilotCount"/>.</summary>
    public int PilotCount => Data.PilotCount;

    /// <summary>Pass-through to <see cref="SessionSummaryRow.TotalIskMined"/>.</summary>
    public double TotalIskMined => Data.TotalIskMined;
}
