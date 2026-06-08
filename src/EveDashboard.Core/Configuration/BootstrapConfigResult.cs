namespace EveDashboard.Core.Configuration;

/// <summary>
/// Outcome of <see cref="IBootstrapConfigLoader.LoadAsync"/>. Closed
/// discriminated-union: only the three nested case types defined below
/// are valid concrete subtypes.
/// </summary>
/// <remarks>
/// The private parameterless constructor is what makes the hierarchy closed:
/// only nested types within <see cref="BootstrapConfigResult"/> can call it,
/// so external assemblies cannot introduce a fourth case. Plan 16's startup
/// chain pattern-matches against the three cases exhaustively.
/// </remarks>
public abstract record BootstrapConfigResult
{
    private BootstrapConfigResult() { }

    /// <summary>
    /// <c>config.yaml</c> existed and parsed cleanly with a non-empty
    /// <c>gamelog_path</c> value. The path may or may not exist on disk;
    /// downstream code (<c>EveDashboard.Mining.Gamelogs.GamelogDiscoveryService</c>
    /// or plan 16's startup) validates and surfaces a popup if missing.
    /// </summary>
    /// <param name="GamelogPath">The path read from the file, after env-var expansion via <see cref="System.Environment.ExpandEnvironmentVariables(string)"/>.</param>
    public sealed record Loaded(string GamelogPath) : BootstrapConfigResult;

    /// <summary>
    /// <c>config.yaml</c> did not exist; the autodetect path was constructed
    /// AND the directory exists on disk. No file was written; one is written
    /// only when the user explicitly overrides via <see cref="IBootstrapConfigLoader.SaveAsync"/>.
    /// </summary>
    /// <param name="GamelogPath">The autodetect-resolved path.</param>
    public sealed record AutodetectUsed(string GamelogPath) : BootstrapConfigResult;

    /// <summary>
    /// <c>config.yaml</c> did not exist AND the autodetect directory does
    /// not exist on disk. Plan 16 surfaces a "where are your gamelogs?"
    /// popup. The loader has already logged this at <c>Error</c>.
    /// </summary>
    /// <param name="AttemptedPath">The path the loader tried to find. Useful in the popup so the user sees what was looked for.</param>
    public sealed record AutodetectFailed(string AttemptedPath) : BootstrapConfigResult;
}
