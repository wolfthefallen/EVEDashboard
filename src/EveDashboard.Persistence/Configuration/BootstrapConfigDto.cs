namespace EveDashboard.Persistence.Configuration;

/// <summary>
/// YamlDotNet binding target for the bootstrap config file. One nullable
/// string field per supported key. Internal: this is an implementation
/// detail of <see cref="YamlBootstrapConfigLoader"/>.
/// </summary>
/// <remarks>
/// YamlDotNet's <c>UnderscoredNamingConvention</c> maps the YAML key
/// <c>gamelog_path</c> to the property <c>GamelogPath</c>. Unknown keys
/// in the YAML file are ignored (forward-compat).
/// </remarks>
internal sealed class BootstrapConfigDto
{
    /// <summary>The user's chosen gamelog directory path. Null if the YAML key was missing.</summary>
    public string? GamelogPath { get; set; }
}
