namespace EveDashboard.App.Wpf.Dialogs;

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using EveDashboard.Core.Sessions.Historical;

/// <summary>
/// One overlapping saved session in the "Build session from history" dialog:
/// what it is, how its fleet relates to the picked roster, and whether the user
/// wants it combined into the new session.
/// </summary>
public sealed partial class OverlapRowViewModel : ObservableObject
{
    /// <summary>The analysed overlap this row presents.</summary>
    public SessionOverlap Overlap { get; }

    /// <summary>Whether the user ticked this session to be combined (otherwise its pilots' time is excluded).</summary>
    [ObservableProperty]
    private bool _isChecked;

    /// <summary>Construct the row.</summary>
    /// <param name="overlap">The analysed overlap.</param>
    /// <param name="isChecked">Initial tick state (pre-selected sessions open ticked).</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="overlap"/> is null.</exception>
    public OverlapRowViewModel(SessionOverlap overlap, bool isChecked)
    {
        ArgumentNullException.ThrowIfNull(overlap);
        Overlap = overlap;
        _isChecked = isChecked;
    }

    /// <summary>Session id.</summary>
    public string SessionId => Overlap.Session.SessionId;

    /// <summary>Session name as stored.</summary>
    public string Name => Overlap.Session.Name;

    /// <summary>Wall-clock span, EVE time: <c>yyyy-MM-dd HH:mm - HH:mm</c> (or a full second date when it crosses midnight).</summary>
    public string SpanText
    {
        get
        {
            var s = Overlap.Session.Start;
            var e = Overlap.Session.Stop;
            var stopFormat = s.Date == e.Date ? "HH:mm" : "yyyy-MM-dd HH:mm";
            return string.Create(CultureInfo.InvariantCulture, $"{s:yyyy-MM-dd HH:mm} - {e.ToString(stopFormat, CultureInfo.InvariantCulture)}");
        }
    }

    /// <summary>True when combining this session needs a union fleet (it has pilots the roster lacks).</summary>
    public bool IsPartial => Overlap.Kind == FleetMatchKind.Partial;

    /// <summary>Names of the pilots shared with the roster, comma-separated.</summary>
    public string SharedPilotsText => string.Join(", ", Overlap.Session.Pilots
        .Where(p => Overlap.SharedPilotIds.Contains(p.PilotId))
        .Select(p => p.Name));

    /// <summary>How the session's fleet relates to the roster, for the list.</summary>
    public string KindText => IsPartial
        ? "Adds " + string.Join(", ", Overlap.ExtraPilots.Select(p => p.Name)) + " - combining it creates a union fleet"
        : "Same pilots as the fleet";
}
