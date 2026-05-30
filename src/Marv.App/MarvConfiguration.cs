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

/// <summary>
/// IRC connection configuration.
/// </summary>
public record IrcConfiguration
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
}
