using System.ComponentModel;
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
    [Description("IRC server hostname.")]
    public string Server { get; init; } = "localhost";

    /// <summary>The IRC server port.</summary>
    [Description("IRC server port.")]
    public int Port { get; init; } = 6667;

    /// <summary>Whether to use TLS for the connection.</summary>
    [Description("Use TLS for the connection.")]
    public bool UseTls { get; init; }

    /// <summary>The bot's nickname.</summary>
    [Description("Bot nickname.")]
    public string Nick { get; init; } = "Marv";

    /// <summary>The bot's username (ident).</summary>
    [Description("Bot username (ident).")]
    public string User { get; init; } = "marv";

    /// <summary>The bot's real name (GECOS).</summary>
    [Description("Bot real name (GECOS).")]
    public string RealName { get; init; } = "Marv IRC Bot";

    /// <summary>SASL username for authentication.</summary>
    [Description("SASL username for authentication.")]
    public string? SaslUser { get; init; }

    /// <summary>SASL password for authentication.</summary>
    [Description("SASL password for authentication.")]
    public string? SaslPassword { get; init; }

    /// <summary>NickServ password for legacy authentication.</summary>
    [Description("NickServ password for legacy authentication.")]
    public string? NickServPassword { get; init; }

    /// <summary>Channels to join on connect.</summary>
    [Description("Channels to join on connect.")]
    public List<string> Channels { get; init; } = [];

    /// <summary>The command prefix for plugin commands.</summary>
    [Description("Command prefix for plugin commands.")]
    public string CommandPrefix { get; init; } = "!";

    /// <summary>
    /// Directories to scan for plugin assemblies.
    /// Defaults to a single "plugins" directory relative to the working directory.
    /// </summary>
    [Description("Directories to scan for plugin assemblies.")]
    public List<string> PluginDirectories { get; init; } = ["plugins"];

    /// <summary>
    /// Plugin names to load. Only plugins whose name matches an entry in this list
    /// will be activated. If empty, no plugins are loaded.
    /// </summary>
    [Description("Plugin names to load.")]
    public List<string> Plugins { get; init; } = [];

    /// <summary>
    /// Override for the default log level. When set, the effective log level is the
    /// more restrictive of this value and the level configured in appsettings.json.
    /// </summary>
    [Description("Override for the default log level.")]
    public LogLevel? LogLevel { get; init; }
}
