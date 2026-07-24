namespace EveDashboard.App.Wpf.Diagnostics;

using Microsoft.Extensions.Logging;

/// <summary>
/// Top-level unhandled-exception policy. Logs the full exception (stack trace) via
/// <see cref="ILogger"/> and surfaces a graceful dialog, so an unexpected throw
/// leaves diagnostics and a message instead of the process vanishing silently.
/// </summary>
/// <remarks>
/// Wired in <c>App.OnStartup</c> to <c>Application.DispatcherUnhandledException</c>
/// (UI thread), <c>AppDomain.CurrentDomain.UnhandledException</c> (background), and
/// <c>TaskScheduler.UnobservedTaskException</c>. The policy is deliberately
/// permissive on the UI thread — a widget glitch must not kill an active mining
/// session, whose data lives safely in SQLite — so
/// <see cref="HandleDispatcherException"/> returns <c>true</c> (handled) to keep the
/// app running. Every exception is logged; the dialog is shown at most once per
/// unique exception signature per session so a recurring throw can't spam dialogs.
/// </remarks>
public sealed class GlobalExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _log;
    private readonly IUnhandledErrorDialog _dialog;
    private readonly string _logHint;
    private readonly HashSet<string> _shownSignatures = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Construct the handler.</summary>
    /// <param name="log">Logger the exceptions are written to (at Error/Critical/Warning).</param>
    /// <param name="dialog">The user-facing error dialog seam.</param>
    /// <param name="logHint">Human-readable pointer to the log location, shown in the dialog.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log, IUnhandledErrorDialog dialog, string logHint)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(logHint);
        _log = log;
        _dialog = dialog;
        _logHint = logHint;
    }

    /// <summary>
    /// Handle an unhandled UI-thread (dispatcher) exception: log it and show a
    /// dialog. Returns <c>true</c> so the caller marks it handled and the app keeps
    /// running.
    /// </summary>
    /// <param name="exception">The unhandled exception.</param>
    /// <returns><c>true</c> — the app should keep running.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="exception"/> is null.</exception>
    public bool HandleDispatcherException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _log.LogError(exception, "Unhandled UI-thread (dispatcher) exception; the app was kept running.");
        ShowOncePerSignature(exception, "EVE Dashboard hit an unexpected error but is still running.");
        return true;
    }

    /// <summary>
    /// Handle an unhandled exception surfaced by <c>AppDomain.UnhandledException</c>
    /// (typically a background thread). Logs at Critical when the runtime is
    /// terminating, Error otherwise; shows a best-effort dialog.
    /// </summary>
    /// <param name="exception">The unhandled exception (may be <c>null</c> for non-<see cref="Exception"/> throws).</param>
    /// <param name="isTerminating">Whether the CLR is tearing the process down.</param>
    public void HandleDomainException(Exception? exception, bool isTerminating)
    {
        if (isTerminating)
        {
            _log.LogCritical(exception, "Unhandled exception on a background thread; the process is terminating.");
        }
        else
        {
            _log.LogError(exception, "Unhandled exception on a background thread.");
        }

        if (exception is not null)
        {
            ShowOncePerSignature(exception, isTerminating
                ? "EVE Dashboard hit a fatal error and has to close."
                : "EVE Dashboard hit an unexpected background error.");
        }
    }

    /// <summary>
    /// Handle an unobserved task exception (already escaped a fire-and-forget task).
    /// Logged at Warning; the caller marks it observed so it does not escalate.
    /// </summary>
    /// <param name="exception">The unobserved (usually aggregate) exception.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="exception"/> is null.</exception>
    public void HandleUnobservedTaskException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _log.LogWarning(exception, "Unobserved task exception (logged and marked observed).");
    }

    /// <summary>Build the user-facing dialog text (exception + log pointer + copy hint).</summary>
    internal string BuildUserMessage(Exception exception, string lead) =>
        lead + "\n\n" +
        exception.GetType().Name + ": " + exception.Message + "\n\n" +
        "Full details (stack trace) are saved in the log:\n" + _logHint + "\n\n" +
        "(Press Ctrl+C to copy this message.)";

    /// <summary>Show the dialog only the first time a given (type + message) signature is seen this session.</summary>
    private void ShowOncePerSignature(Exception exception, string lead)
    {
        var signature = exception.GetType().FullName + "|" + exception.Message;
        lock (_gate)
        {
            if (!_shownSignatures.Add(signature))
            {
                return; // already shown this exact error this session — logged above, don't re-pop the dialog
            }
        }

        try
        {
            _dialog.Show(BuildUserMessage(exception, lead));
        }
        catch (Exception dialogEx)
        {
            _log.LogWarning(dialogEx, "Failed to show the unhandled-error dialog.");
        }
    }
}
