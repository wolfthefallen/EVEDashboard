namespace EveDashboard.App.Wpf.Widgets.StatConfig;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using EveDashboard.Core.Settings.WidgetStats;

/// <summary>
/// Projects the reorderable subset of a <see cref="StatRowsResolution"/> onto
/// an <see cref="ObservableCollection{T}"/> of <see cref="StatRowViewModel"/>
/// (plan 20). Subscribes to the source VM's <see cref="INotifyPropertyChanged"/>
/// so individual stat property changes update the matching row's
/// <see cref="StatRowViewModel.FormattedValue"/> in place — no full
/// <c>Stats</c> rebuild on every refresh.
/// </summary>
/// <remarks>
/// <para>
/// Hidden Reorderable descriptors (<see cref="StatRowDescriptor.Hidden"/> = true)
/// are filtered out — they don't appear in the output collection at all.
/// HideOnly descriptors are a programming error to pass here (the body's
/// <c>ItemsControl</c> would render them twice); the constructor throws.
/// </para>
/// <para>
/// Lifecycle: the widget VM constructs ONE renderer instance and rebuilds the
/// underlying descriptors / collection whenever the
/// <c>ISettingsRepository.SettingChanged</c> event fires for its key. Each
/// rebuild calls <see cref="ApplyDescriptors"/>, which produces a fresh
/// <see cref="StatRowViewModel"/> per visible descriptor.
/// </para>
/// </remarks>
public sealed class StatRowsRenderer : IDisposable
{
    private readonly INotifyPropertyChanged _source;

    /// <summary>The bound collection rendered by the widget body's ItemsControl.</summary>
    public ObservableCollection<StatRowViewModel> Stats { get; } = new();

    /// <summary>Currently-applied descriptors (visible Reorderable subset). Indexed by stat key for INPC dispatch.</summary>
    private Dictionary<string, (StatRowDescriptor Descriptor, StatRowViewModel Row)> _byKey =
        new(StringComparer.Ordinal);

    private bool _disposed;

    /// <summary>Construct a renderer subscribed to <paramref name="source"/>'s INPC.</summary>
    /// <param name="source">The widget VM that owns the stat properties (must implement <see cref="INotifyPropertyChanged"/>).</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    public StatRowsRenderer(INotifyPropertyChanged source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _source.PropertyChanged += OnSourcePropertyChanged;
    }

    /// <summary>
    /// Replace the current rendered descriptors with <paramref name="reorderable"/>,
    /// filtering out hidden rows. Resets <see cref="Stats"/> and re-reads each
    /// visible property's current value through its <see cref="StatRowDescriptor.Format"/>.
    /// </summary>
    /// <param name="reorderable">Reorderable descriptors from <see cref="StatRowsBuilder.Resolve"/>. HideOnly descriptors are rejected.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="reorderable"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if any descriptor has <see cref="StatRowDescriptor.Reorderable"/> = false.</exception>
    public void ApplyDescriptors(IReadOnlyList<StatRowDescriptor> reorderable)
    {
        ArgumentNullException.ThrowIfNull(reorderable);

        if (reorderable.Any(d => !d.Reorderable))
        {
            throw new ArgumentException(
                "StatRowsRenderer.ApplyDescriptors received a HideOnly descriptor; only Reorderable descriptors flow into the body's Stats collection.",
                nameof(reorderable));
        }

        // Rebuild the lookup + Stats from scratch. Hidden rows are filtered out.
        var newByKey = new Dictionary<string, (StatRowDescriptor, StatRowViewModel)>(StringComparer.Ordinal);
        Stats.Clear();
        foreach (var d in reorderable)
        {
            if (d.Hidden) continue;
            var row = new StatRowViewModel(
                key: d.Key,
                label: d.Label,
                formattedValue: ReadFormattedValue(d),
                emphasis: d.Emphasis);
            Stats.Add(row);
            newByKey[d.Key] = (d, row);
        }
        _byKey = newByKey;
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null) return;
        if (!_byKey.TryGetValue(e.PropertyName, out var entry)) return;
        entry.Row.FormattedValue = ReadFormattedValue(entry.Descriptor);
    }

    /// <summary>
    /// Read the source VM's property value via <see cref="StatRowDescriptor.PropInfo"/>
    /// and apply the descriptor's format. Null values render as empty
    /// string (matches the existing widget body behaviour for null
    /// <c>LastEventAt</c> etc).
    /// </summary>
    private string ReadFormattedValue(StatRowDescriptor d)
    {
        var raw = d.PropInfo.GetValue(_source);
        if (raw is null) return string.Empty;
        if (d.Format is null) return raw.ToString() ?? string.Empty;
        // Use IFormattable when available so we can pass the format string +
        // invariant culture; fall back to ToString() for non-formattable types.
        if (raw is IFormattable formattable)
        {
            return formattable.ToString(d.Format, CultureInfo.CurrentCulture);
        }
        return raw.ToString() ?? string.Empty;
    }

    /// <summary>Unsubscribe from the source VM's PropertyChanged. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _source.PropertyChanged -= OnSourcePropertyChanged; } catch { /* defensive */ }
    }
}
