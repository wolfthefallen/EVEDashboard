using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Core.Logging;

/// <summary>
/// Static accessor for the application's <see cref="ILoggerFactory"/>. Set once
/// at application startup by <c>EveDashboard.Persistence.Logging.LoggingSetup.Configure</c>;
/// before that point, the default <see cref="NullLoggerFactory"/> is in effect
/// (any logging is a no-op).
/// </summary>
/// <remarks>
/// Static accessor pattern is intentional — it keeps consumers
/// to a one-line declaration without a DI container in the loop. A later change
/// may migrate to <c>Microsoft.Extensions.DependencyInjection</c>; the migration
/// is mechanical because every consumer goes through this single accessor.
/// </remarks>
public static class LogProvider
{
    /// <summary>
    /// The active logger factory. Defaults to <see cref="NullLoggerFactory.Instance"/>.
    /// </summary>
    public static ILoggerFactory Factory { get; set; } = NullLoggerFactory.Instance;
}
