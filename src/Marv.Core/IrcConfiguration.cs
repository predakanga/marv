namespace Marv.Core;

/// <summary>
/// IRC connection configuration. Bound from the "Irc" section of the configuration file.
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
