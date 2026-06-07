using Marv.Core.Plugin;

namespace Marv.Plugins.Greet;

/// <summary>
/// Configuration for the Greet plugin. Bound to the "Greet" configuration section.
/// </summary>
[PluginConfig(Section = "Greet")]
public record GreetPluginConfig
{
    /// <summary>
    /// The message to send when a user joins a channel. Use {nick} as a placeholder
    /// for the joining user's nickname.
    /// </summary>
    public string GreetMessage { get; init; } = "Welcome, {nick}!";

    /// <summary>Whether to greet users when they join a channel.</summary>
    public bool GreetOnJoin { get; init; } = true;
}
