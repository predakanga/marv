using Marv.Core.Plugin;

namespace Marv.Plugins.Auth;

/// <summary>
/// Configuration for the Auth plugin. Bound to the "Plugins:Auth" configuration section.
/// </summary>
[PluginConfig(Section = "Auth")]
public record AuthPluginConfig
{
    /// <summary>
    /// List of services account names that are considered administrators.
    /// </summary>
    public string[] AdminAccounts { get; init; } = [];
}
