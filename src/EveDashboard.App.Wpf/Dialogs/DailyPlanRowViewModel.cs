namespace EveDashboard.App.Wpf.Dialogs;

using System.Globalization;
using EveDashboard.Core.Sessions.Historical;

/// <summary>
/// One day in the "build a session per day" preview: either a session that will
/// be created, or a day that will be passed over and why.
/// </summary>
/// <remarks>
/// The preview exists so the user can see what a single press of the button is
/// about to create - dozens of sessions is a lot to undo one at a time - and so
/// a skipped day explains itself rather than silently going missing.
/// </remarks>
public sealed class DailyPlanRowViewModel
{
    private DailyPlanRowViewModel(DateOnly day, bool willBuild, string name, string detail)
    {
        Day = day;
        WillBuild = willBuild;
        Name = name;
        Detail = detail;
    }

    /// <summary>The UTC day this row is about.</summary>
    public DateOnly Day { get; }

    /// <summary>True when a session will be created for this day.</summary>
    public bool WillBuild { get; }

    /// <summary>The name the session will get, or the day on its own for a skipped day.</summary>
    public string Name { get; }

    /// <summary>What will happen, in one line.</summary>
    public string Detail { get; }

    /// <summary>The day as <c>yyyy-MM-dd</c>, for the list.</summary>
    public string DayText => Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Build a row for a day that will become a session.</summary>
    /// <param name="plan">The planned day.</param>
    /// <param name="name">The name its session will get, already template-expanded.</param>
    /// <returns>The row.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="plan"/> or <paramref name="name"/> is null.</exception>
    public static DailyPlanRowViewModel ForPlan(DailySessionPlan plan, string name)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(name);

        var parts = new List<string>
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"{plan.WindowStart:HH:mm} - {plan.WindowStop:HH:mm} EVE, {plan.Roster.Count} pilot(s): {string.Join(", ", plan.Roster.Select(m => m.Name))}"),
        };
        if (plan.Merge.Count > 0)
        {
            parts.Add("folding in " + string.Join(", ", plan.Merge.Select(s => "'" + s.Name + "'")));
        }
        if (plan.Exclusions.Count > 0)
        {
            var owners = plan.Exclusions.Select(x => x.SessionId).Distinct(StringComparer.Ordinal).Count();
            parts.Add($"leaving out time owned by {owners} saved session(s)");
        }
        if (plan.PilotsWithNoOre.Count > 0)
        {
            parts.Add($"{plan.PilotsWithNoOre.Count} pilot(s) mined no ore and are left off");
        }
        return new DailyPlanRowViewModel(plan.Day, willBuild: true, name, string.Join("; ", parts));
    }

    /// <summary>Build a row for a day that will be passed over.</summary>
    /// <param name="skip">The skipped day and its reason.</param>
    /// <returns>The row.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="skip"/> is null.</exception>
    public static DailyPlanRowViewModel ForSkip(DailySessionSkip skip)
    {
        ArgumentNullException.ThrowIfNull(skip);
        return new DailyPlanRowViewModel(skip.Day, willBuild: false, "No session", "Skipped: " + skip.Reason);
    }
}
