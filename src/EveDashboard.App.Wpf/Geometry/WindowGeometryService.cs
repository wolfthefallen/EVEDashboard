namespace EveDashboard.App.Wpf.Geometry;

using System.ComponentModel;
using System.Windows;
using EveDashboard.Core.Settings;
using Microsoft.Extensions.Logging;

/// <summary>
/// Wraps a WPF <see cref="Window"/>'s lifecycle for window-geometry persistence.
/// Reads the saved geometry on startup (or applies the configured default when
/// no setting exists), debounces size/move events to a 500 ms save (per plan 16
/// decision 3.2 = B), and writes one final save on <see cref="Window.Closing"/>.
/// </summary>
/// <remarks>
/// <para>
/// Plan 25 refactor: the service is parameterized with read/write delegates and
/// a default size, instead of taking <see cref="ISettingsRepository"/> directly.
/// This lets a second instance manage the Reports child window with different
/// settings methods. Two DI factories register the MainWindow-flavoured and
/// Reports-flavoured services.
/// </para>
/// <para>
/// Per plan 16 decision 3.1 = B and decision 16.S: persisted shape is the
/// <see cref="WindowGeometry"/> record from <see cref="EveDashboard.Core.Settings"/>
/// (<c>Width</c>, <c>Height</c>, <c>Left</c>, <c>Top</c>, <c>IsMaximized</c>).
/// First-launch defaults come from the ctor parameters. Minimized window state
/// clamps to <c>IsMaximized = false</c> on save.
/// </para>
/// </remarks>
public sealed class WindowGeometryService : IDisposable
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(500);

    private readonly Func<CancellationToken, Task<WindowGeometry?>> _readGeometry;
    private readonly Func<WindowGeometry, Task> _writeGeometry;
    private readonly double _defaultWidth;
    private readonly double _defaultHeight;
    private readonly ILogger<WindowGeometryService> _log;

    private Window? _window;
    private CancellationTokenSource? _debounceCts;
    private bool _disposed;

    /// <summary>Construct the service.</summary>
    /// <param name="readGeometry">Delegate that reads the persisted geometry; returns <c>null</c> when missing.</param>
    /// <param name="writeGeometry">Delegate that persists a new geometry value.</param>
    /// <param name="defaultWidth">Default width applied when no persisted geometry exists.</param>
    /// <param name="defaultHeight">Default height applied when no persisted geometry exists.</param>
    /// <param name="log">Logger for save / restore diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="readGeometry"/>, <paramref name="writeGeometry"/>, or <paramref name="log"/> is null.</exception>
    public WindowGeometryService(
        Func<CancellationToken, Task<WindowGeometry?>> readGeometry,
        Func<WindowGeometry, Task> writeGeometry,
        double defaultWidth,
        double defaultHeight,
        ILogger<WindowGeometryService> log)
    {
        ArgumentNullException.ThrowIfNull(readGeometry);
        ArgumentNullException.ThrowIfNull(writeGeometry);
        ArgumentNullException.ThrowIfNull(log);
        _readGeometry = readGeometry;
        _writeGeometry = writeGeometry;
        _defaultWidth = defaultWidth;
        _defaultHeight = defaultHeight;
        _log = log;
    }

    /// <summary>
    /// Read persisted geometry and apply it to <paramref name="window"/>, or
    /// apply the configured default if no setting exists. Subscribes to
    /// the window's <c>SizeChanged</c>, <c>LocationChanged</c>, and
    /// <c>Closing</c> events for save coordination.
    /// </summary>
    /// <param name="window">The Window to manage. Must not have been shown yet (so applying StartupLocation has effect).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="window"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if called twice on the same instance.</exception>
    public async Task ApplyOnStartupAsync(Window window, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_window is not null)
        {
            throw new InvalidOperationException("ApplyOnStartupAsync can only be called once.");
        }
        _window = window;

        var saved = await _readGeometry(ct).ConfigureAwait(true);
        if (saved is null)
        {
            window.Width = _defaultWidth;
            window.Height = _defaultHeight;
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.WindowState = WindowState.Normal;
            _log.LogInformation(
                "WindowGeometryService applied default geometry ({Width}x{Height} centered Normal).",
                _defaultWidth, _defaultHeight);
        }
        else
        {
            window.Width = saved.Width;
            window.Height = saved.Height;
            window.Left = saved.Left;
            window.Top = saved.Top;
            window.WindowState = saved.IsMaximized ? WindowState.Maximized : WindowState.Normal;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            _log.LogInformation(
                "WindowGeometryService restored geometry: {Width}x{Height} at ({Left},{Top}) Maximized={IsMax}.",
                saved.Width, saved.Height, saved.Left, saved.Top, saved.IsMaximized);
        }

        window.SizeChanged += OnSizeOrLocationChanged;
        window.LocationChanged += OnSizeOrLocationChanged;
        window.Closing += OnClosing;
    }

    private void OnSizeOrLocationChanged(object? sender, EventArgs e)
    {
        // Cancel any in-flight debounce timer; start a new one. The Task.Delay
        // wakes 500 ms after the LAST size/move, then triggers SaveAsync.
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        _ = DebouncedSaveAsync(token);
    }

    private async Task DebouncedSaveAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(DebounceInterval, token).ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        try
        {
            await SaveAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "WindowGeometryService debounced save failed.");
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Final flush on close. Window.Closing is synchronous; an async-void
        // handler would let the close complete before the save commits and
        // race the dispose of the underlying SQLite connection. The blocking
        // call is the plan-16 risk #6 documented exception to CLAUDE.md's
        // "no .GetAwaiter().GetResult() in production paths" rule: at Closing
        // there is no remaining async path, the save is one tiny SQL UPDATE,
        // and the user has chosen to close.
        try
        {
            _debounceCts?.Cancel();
            SaveAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "WindowGeometryService final save on Closing failed.");
        }
    }

    private Task SaveAsync()
    {
        var window = _window;
        if (window is null) return Task.CompletedTask;

        // Snapshot RestoreBounds when maximized so re-restored width/height
        // are the pre-maximize values, not the maximized fullscreen ones.
        var bounds = window.WindowState == WindowState.Maximized
            ? window.RestoreBounds
            : new Rect(window.Left, window.Top, window.Width, window.Height);

        var geometry = new WindowGeometry(
            Width:       bounds.Width,
            Height:      bounds.Height,
            Left:        bounds.Left,
            Top:         bounds.Top,
            IsMaximized: window.WindowState == WindowState.Maximized);

        return _writeGeometry(geometry);
    }

    /// <summary>Dispose: cancel the in-flight debounce timer.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _disposed = true;
    }
}
