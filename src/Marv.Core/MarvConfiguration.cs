using Microsoft.Extensions.Logging;

namespace Marv.Core;

/// <summary>
/// Root configuration model for the Marv bot.
/// Bound from the configuration file, environment variables, and CLI arguments.
/// All properties are at the top level — there is no nesting.
/// </summary>
public record MarvConfiguration
{
    /// <summary>The IRC server hostname.</summary>
    public string Server { get; init; } = "localhost";

    /// <summary>The IRC server port.</summary>
    public int Port { get; init; } = 6667;

    /// <summary>Whether to use TLS for the connection.</summary>
    public bool UseTls { get; init; }

    /// <summary>The bot's nickname.</summary>
    public string Nick { get; init; } = "Marv";

    /// <summary>The bot's username (ident).</summary>
    public string User { get; init; } = "marv";

    /// <summary>The bot's real name (GECOS).</summary>
    public string RealName { get; init; } = "Marv IRC Bot";

    /// <summary>SASL username for authentication.</summary>
    public string? SaslUser { get; init; }

    /// <summary>SASL password for authentication.</summary>
    public string? SaslPassword { get; init; }

    /// <summary>NickServ password for legacy authentication.</summary>
    public string? NickServPassword { get; init; }

    /// <summary>Channels to join on connect.</summary>
    public List<string> Channels { get; init; } = [];

    /// <summary>The command prefix for plugin commands.</summary>
    public string CommandPrefix { get; init; } = "!";

    /// <summary>
    /// Directories to scan for plugin assemblies.
    /// Defaults to a single "plugins" directory relative to the working directory.
    /// </summary>
    public List<string> PluginDirectories { get; init; } = ["plugins"];

    /// <summary>
    /// Plugin names to load. Only plugins whose name matches an entry in this list
    /// will be activated. If empty, no plugins are loaded.
    /// </summary>
    public List<string> Plugins { get; init; } = [];

    /// <summary>
    /// Override for the default log level. When set, the effective log level is the
    /// more restrictive of this value and the level configured in appsettings.json.
    /// </summary>
    public LogLevel? LogLevel { get; init; }
}
