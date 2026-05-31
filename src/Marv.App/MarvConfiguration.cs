using Marv.Core;

namespace Marv.App;

/// <summary>
/// Root configuration model for the Marv bot.
/// Bound from the configuration file, environment variables, and CLI arguments.
/// </summary>
public record MarvConfiguration
{
    /// <summary>IRC connection settings.</summary>
    public IrcConfiguration Irc { get; init; } = new();

    /// <summary>List of plugin assembly paths to load.</summary>
    public List<string> Plugins { get; init; } = [];
}
