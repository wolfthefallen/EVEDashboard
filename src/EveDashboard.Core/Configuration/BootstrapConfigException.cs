namespace EveDashboard.Core.Configuration;

/// <summary>
/// Thrown by <see cref="IBootstrapConfigLoader.LoadAsync"/> when
/// <c>config.yaml</c> is present but unusable: malformed YAML, missing
/// <c>gamelog_path</c> key, or empty/whitespace-only value. Callers
/// (plan 16's startup) catch this and surface a "config.yaml is malformed
/// at &lt;path&gt;; please edit and confirm" popup.
/// </summary>
/// <remarks>
/// Distinct from the missing-file path (which returns
/// <see cref="BootstrapConfigResult.AutodetectUsed"/> or
/// <see cref="BootstrapConfigResult.AutodetectFailed"/>). A present-but-broken
/// file is a user error, not a missing-file fallback condition.
/// </remarks>
public sealed class BootstrapConfigException : Exception
{
    /// <summary>Construct with a message and optional inner exception.</summary>
    /// <param name="message">Human-readable diagnosis suitable for the popup body.</param>
    /// <param name="inner">Underlying exception (e.g. <c>YamlDotNet.Core.YamlException</c>) if any.</param>
    public BootstrapConfigException(string message, Exception? inner = null)
        : base(message, inner) { }
}
