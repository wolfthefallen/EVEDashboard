using Microsoft.Extensions.Logging;

namespace EveDashboard.Tests.Shared;

/// <summary>
/// Test-only <see cref="ILogger{T}"/> implementation that captures log entries
/// into an in-memory list for later assertion. Thread-safe via lock.
/// </summary>
/// <typeparam name="T">Logger category type, same as <see cref="ILogger{T}"/>.</typeparam>
/// <remarks>
/// Lives under <c>tests/_shared/</c> and is linked into individual test projects
/// via <c>&lt;Compile Include="..\_shared\CapturingLogger.cs" /&gt;</c> rather than
/// referenced via a project dependency — test projects do not reference each other.
/// </remarks>
public sealed class CapturingLogger<T> : ILogger<T>
{
    /// <summary>One captured log entry.</summary>
    /// <param name="Level">The <see cref="LogLevel"/> the entry was logged at.</param>
    /// <param name="Message">The formatted message string.</param>
    /// <param name="Exception">The exception attached to the log call, if any.</param>
    public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

    private readonly List<Entry> _entries = new();
    private readonly object _gate = new();

    /// <summary>All captured entries, in order, as a snapshot.</summary>
    public IReadOnlyList<Entry> Entries
    {
        get
        {
            lock (_gate) return _entries.ToList();
        }
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        lock (_gate)
        {
            _entries.Add(new Entry(logLevel, message, exception));
        }
    }
}
