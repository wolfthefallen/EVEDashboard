namespace EveDashboard.App.Wpf.Widgets.StatConfig;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EveDashboard.Core.Settings.WidgetStats;
using Microsoft.Extensions.Logging;

/// <summary>
/// ViewModel behind the Reports "Text size" popup (the second chrome
/// cog, gated by <c>WidgetBaseViewModel.HasTextSizeConfig</c>). Lets the user
/// pick a <see cref="StatEmphasis"/> per named element of a table widget — each
/// label line individually, plus one size for the whole table. Mirrors
/// <see cref="StatConfigPopupViewModel"/>'s Load / Apply / Close lifecycle but is
/// simpler: no drag-reorder, no hide checkbox.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle inside one popup-open cycle:
/// <list type="number">
/// <item>The text-size cog flips <c>WidgetBaseViewModel.IsTextSizeConfigOpen</c>
///   to true; the chrome <c>Popup</c>'s <c>IsOpen</c> two-way binding opens it.</item>
/// <item><see cref="LoadAsync"/> reads the saved element-key -&gt; emphasis map
///   and fills <see cref="Rows"/>, defaulting any unlisted element to
///   <see cref="StatEmphasis.None"/>.</item>
/// <item>User picks emphasis levels — edits land on the working-copy rows.</item>
/// <item>On Apply: project rows into a fresh map (dropping
///   <see cref="StatEmphasis.None"/> entries to keep it small), persist via the
///   save delegate, close. The settings repository's <c>SettingChanged</c> event
///   then fires; the widget VM re-resolves its per-element emphasis live.</item>
/// <item>On Close / click-outside: working copy is discarded; saved map unchanged.</item>
/// </list>
/// </para>
/// <para>
/// On <see cref="ApplyAsync"/> failure (e.g. SQLite write error): logs Warning and
/// leaves the popup open at the user's edits, mirroring the stat-config popup.
/// </para>
/// </remarks>
public sealed partial class TextSizePopupViewModel : ObservableObject, IDisposable
{
    private readonly IReadOnlyList<(string Key, string Label, StatEmphasis Default)> _elements;
    private readonly Func<CancellationToken, Task<IReadOnlyDictionary<string, StatEmphasis>?>> _load;
    private readonly Func<IReadOnlyDictionary<string, StatEmphasis>, CancellationToken, Task> _save;
    private readonly ILogger? _log;
    private bool _disposed;

    /// <summary>The user's working copy — one row per configurable element, in declaration order.</summary>
    public ObservableCollection<TextSizeRowViewModel> Rows { get; } = new();

    /// <summary>True after a successful Apply; the chrome <c>Popup.IsOpen</c> reads this via the widget VM's <c>IsTextSizeConfigOpen</c> flag.</summary>
    [ObservableProperty]
    private bool _shouldClose;

    /// <summary>Construct a text-size popup VM around a widget type's element list and its load/save delegates.</summary>
    /// <param name="elements">The configurable elements (stable key + user label + default emphasis), in display order.</param>
    /// <param name="load">Delegate that reads the saved element-key -&gt; emphasis map (returns null when absent).</param>
    /// <param name="save">Delegate that persists a new element-key -&gt; emphasis map.</param>
    /// <param name="log">Optional logger for the Apply-failure Warning line.</param>
    /// <exception cref="ArgumentNullException">Thrown if any required argument is null.</exception>
    public TextSizePopupViewModel(
        IReadOnlyList<(string Key, string Label, StatEmphasis Default)> elements,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, StatEmphasis>?>> load,
        Func<IReadOnlyDictionary<string, StatEmphasis>, CancellationToken, Task> save,
        ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(save);
        _elements = elements;
        _load = load;
        _save = save;
        _log = log;
    }

    /// <summary>
    /// Read the saved map and (re)populate <see cref="Rows"/> with editable
    /// working copies. Idempotent — call on every popup open so the user always
    /// sees the latest persisted state.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var saved = await _load(ct).ConfigureAwait(true);
        Rows.Clear();
        foreach (var (key, label, def) in _elements)
        {
            // Effective emphasis = saved override if present, else this element's default.
            var emphasis = saved is not null && saved.TryGetValue(key, out var e) ? e : def;
            Rows.Add(new TextSizeRowViewModel(key, label, emphasis, def));
        }
        ShouldClose = false;
    }

    /// <summary>
    /// Project the working-copy rows into a fresh element-key -&gt; emphasis map
    /// (storing only the rows that DEVIATE from their default, so an element left
    /// at its default resolves back to that default rather than being pinned) and
    /// call the save delegate. On success, sets <see cref="ShouldClose"/> = true;
    /// on failure, logs Warning and leaves the popup open.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [RelayCommand]
    public async Task ApplyAsync(CancellationToken ct = default)
    {
        var map = new Dictionary<string, StatEmphasis>(Rows.Count);
        foreach (var row in Rows)
        {
            if (row.Emphasis != row.Default)
            {
                map[row.Key] = row.Emphasis;
            }
        }

        try
        {
            await _save(map, ct).ConfigureAwait(true);
            ShouldClose = true;
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex,
                "TextSizePopup Apply failed; leaving popup open at the user's edits.");
            // Leave ShouldClose=false so the popup stays open.
        }
    }

    /// <summary>Discard working-copy edits and close the popup.</summary>
    [RelayCommand]
    public void Close()
    {
        ShouldClose = true;
    }

    /// <summary>Dispose: idempotent. No external subscriptions to clear in this VM.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
