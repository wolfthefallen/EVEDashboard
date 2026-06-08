namespace EveDashboard.App.Wpf.Widgets.StatConfig;

using System.Reflection;
using EveDashboard.Core.Settings.WidgetStats;
using Microsoft.Extensions.Logging;

/// <summary>
/// Reflection helper that takes a widget-VM type plus the user's saved
/// <see cref="WidgetStatsConfig"/> and returns the two ordered descriptor
/// lists the renderer / popup need (plan 20).
/// </summary>
/// <remarks>
/// <para>
/// Algorithm summary:
/// <list type="bullet">
/// <item>Reflect over the VM type's public instance properties (including
///   inherited) and collect every <see cref="StatRowAttribute"/>-decorated
///   property into two buckets by <see cref="StatRowAttribute.Reorderable"/>.</item>
/// <item><b>Reorderable</b>: walk <see cref="WidgetStatsConfig.Entries"/> in
///   order; for each saved entry whose key matches a reorderable property,
///   emit a descriptor (saved emphasis + saved hidden flag). For each
///   reorderable property NOT mentioned in the saved entries, insert at its
///   <see cref="StatRowAttribute.DefaultOrder"/> position
///   (decision 7.1 = B — future-stat inheritance).</item>
/// <item><b>HideOnly</b>: emit all hide-only properties in
///   <see cref="StatRowAttribute.DefaultOrder"/> ascending order. Apply each
///   property's saved hidden flag if a matching saved entry exists.</item>
/// <item>Saved entries with no matching property on the current VM are
///   IGNORED (logged at Debug). Handles the "future plan removed a stat"
///   edge case without a config-file rewrite.</item>
/// </list>
/// </para>
/// <para>
/// When <c>saved</c> is null: reorderable rows in <see cref="StatRowAttribute.DefaultOrder"/>
/// ascending, all visible, all using each property's
/// <see cref="StatRowAttribute.DefaultEmphasis"/>. Tie-breaks on equal
/// <c>DefaultOrder</c> values are deterministic: ascending by property name.
/// </para>
/// </remarks>
public static class StatRowsBuilder
{
    /// <summary>
    /// Resolve the reorderable + hide-only descriptor lists for a VM type.
    /// </summary>
    /// <param name="vmType">Concrete widget-VM type. Public instance properties are reflected (including inherited).</param>
    /// <param name="saved">User's saved per-row state, or null = use defaults.</param>
    /// <param name="log">Logger for the "saved entry references unknown stat" Debug line. May be null (no logging).</param>
    /// <returns>Two lists in the order the views will consume them.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="vmType"/> is null.</exception>
    public static StatRowsResolution Resolve(Type vmType, WidgetStatsConfig? saved, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(vmType);

        // Map of property-name -> (PropertyInfo, [StatRow] attribute) for every attributed
        // public instance property on the VM type, including inherited.
        var attributedByName = new Dictionary<string, (PropertyInfo Prop, StatRowAttribute Attr)>(StringComparer.Ordinal);
        foreach (var prop in vmType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = (StatRowAttribute?)Attribute.GetCustomAttribute(prop, typeof(StatRowAttribute), inherit: true);
            if (attr is null) continue;
            attributedByName[prop.Name] = (prop, attr);
        }

        // Split into the two buckets by Reorderable.
        var reorderableProps = attributedByName.Values
            .Where(t => t.Attr.Reorderable)
            .ToList();
        var hideOnlyProps = attributedByName.Values
            .Where(t => !t.Attr.Reorderable)
            .OrderBy(t => t.Attr.DefaultOrder)
            .ThenBy(t => t.Prop.Name, StringComparer.Ordinal)
            .ToList();

        // Build saved-entries lookup. Entries whose key matches NO attributed
        // property are skipped; logged at Debug so a future regression in the
        // VM (renamed property) leaves a breadcrumb.
        var savedByKey = new Dictionary<string, StatEntry>(StringComparer.Ordinal);
        if (saved is not null)
        {
            foreach (var entry in saved.Entries)
            {
                if (!attributedByName.ContainsKey(entry.StatKey))
                {
                    log?.LogDebug(
                        "Saved StatEntry '{Key}' on VM type {VmType} has no matching [StatRow] property; ignoring.",
                        entry.StatKey, vmType.Name);
                    continue;
                }
                savedByKey[entry.StatKey] = entry;
            }
        }

        // ---- Reorderable list ---------------------------------------------------
        var reorderable = new List<StatRowDescriptor>(reorderableProps.Count);
        var emittedReorderableKeys = new HashSet<string>(StringComparer.Ordinal);

        // 1) Walk saved entries in order; emit reorderable matches.
        if (saved is not null)
        {
            foreach (var entry in saved.Entries)
            {
                if (!savedByKey.ContainsKey(entry.StatKey)) continue;
                if (!attributedByName.TryGetValue(entry.StatKey, out var meta)) continue;
                if (!meta.Attr.Reorderable) continue;

                // Saved emphasis IS the user's choice — even when they explicitly
                // chose StatEmphasis.None on a property whose DefaultEmphasis is
                // non-None. The popup writes the chosen value verbatim.
                reorderable.Add(BuildDescriptor(meta.Prop, meta.Attr, entry.Hidden, entry.Emphasis));
                emittedReorderableKeys.Add(entry.StatKey);
            }
        }

        // 2) Append any reorderable property NOT in the saved set, sorted by DefaultOrder.
        // Per decision 7.1 = B "Existing user customisation persists with no implicit
        // reordering of the user's already-positioned stats" — missing entries (which
        // in practice means future-plan-added stats) appear AFTER the user's saved
        // prefix, sorted among themselves by DefaultOrder. Interleaving into the saved
        // order would scramble the user's chosen positions the next time a release
        // adds a new stat with a small DefaultOrder.
        var missingReorderable = reorderableProps
            .Where(t => !emittedReorderableKeys.Contains(t.Prop.Name))
            .OrderBy(t => t.Attr.DefaultOrder)
            .ThenBy(t => t.Prop.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var (prop, attr) in missingReorderable)
        {
            reorderable.Add(BuildDescriptor(prop, attr, hidden: false, emphasis: attr.DefaultEmphasis));
        }

        // ---- HideOnly list ------------------------------------------------------
        var hideOnly = new List<StatRowDescriptor>(hideOnlyProps.Count);
        foreach (var (prop, attr) in hideOnlyProps)
        {
            var hidden = savedByKey.TryGetValue(prop.Name, out var savedEntry) && savedEntry.Hidden;
            // Hide-only rows render via dedicated TextBlocks; emphasis is N/A.
            // We still record the property's DefaultEmphasis for completeness
            // (e.g. unit tests may want to inspect it) but the renderer ignores it.
            hideOnly.Add(BuildDescriptor(prop, attr, hidden, attr.DefaultEmphasis));
        }

        return new StatRowsResolution(reorderable, hideOnly);
    }

    private static StatRowDescriptor BuildDescriptor(
        PropertyInfo prop, StatRowAttribute attr, bool hidden, StatEmphasis emphasis)
    {
        return new StatRowDescriptor(
            Key: prop.Name,
            Label: attr.Label,
            Format: attr.Format,
            Reorderable: attr.Reorderable,
            Hidden: hidden,
            Emphasis: emphasis,
            PropInfo: prop);
    }
}
