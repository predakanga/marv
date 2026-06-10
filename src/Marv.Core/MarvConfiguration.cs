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
    /// <summary>
    /// Normalizes empty strings to null. .NET's JSON configuration provider converts
    /// explicit JSON <c>null</c> values to empty strings; this ensures nullable string
    /// properties treat both representations as "not set".
    /// </summary>
    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

    /// <summary>The IRC server hostname.</summary>
    [Description("IRC server hostname.")]
    public string Server { get; init; } = "localhost";

    /// <summary>The IRC server port.</summary>
    [Description("IRC server port.")]
    public int Port { get; init; } = 6667;

    /// <summary>Whether to use TLS for the connection.</summary>
    [Description("Use TLS for the connection.")]
    public bool UseTls { get; init; }

    /// <summary>
    /// When true, disables all TLS certificate validation. Useful for servers
    /// with self-signed or expired certificates. Use with caution.
    /// </summary>
    [Description("Skip TLS certificate validation (insecure).")]
    public bool TlsSkipCertificateValidation { get; init; }

    /// <summary>
    /// Path to a PEM-encoded CA certificate file to trust in addition to the
    /// system trust store. Use this to connect to servers with certificates
    /// signed by a private CA.
    /// </summary>
    [Description("Path to a PEM CA certificate file for custom trust.")]
    public string? TlsCaCertFile { get => _tlsCaCertFile; init => _tlsCaCertFile = NullIfEmpty(value); }
    private readonly string? _tlsCaCertFile;

    /// <summary>Server password sent via the PASS command during registration.</summary>
    [Description("Server password (PASS command).")]
    public string? ServerPassword { get => _serverPassword; init => _serverPassword = NullIfEmpty(value); }
    private readonly string? _serverPassword;

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
    public string? SaslUser { get => _saslUser; init => _saslUser = NullIfEmpty(value); }
    private readonly string? _saslUser;

    /// <summary>SASL password for authentication.</summary>
    [Description("SASL password for authentication.")]
    public string? SaslPassword { get => _saslPassword; init => _saslPassword = NullIfEmpty(value); }
    private readonly string? _saslPassword;

    /// <summary>NickServ password for legacy authentication.</summary>
    [Description("NickServ password for legacy authentication.")]
    public string? NickServPassword { get => _nickServPassword; init => _nickServPassword = NullIfEmpty(value); }
    private readonly string? _nickServPassword;

    /// <summary>Oper username for IRC operator authentication.</summary>
    [Description("Oper username for IRC operator authentication.")]
    public string? OperName { get => _operName; init => _operName = NullIfEmpty(value); }
    private readonly string? _operName;

    /// <summary>Oper password for IRC operator authentication.</summary>
    [Description("Oper password for IRC operator authentication.")]
    public string? OperPassword { get => _operPassword; init => _operPassword = NullIfEmpty(value); }
    private readonly string? _operPassword;

    /// <summary>
    /// Additional user modes to set on the bot after authentication completes,
    /// specified as a standard mode string (e.g. "+ix"). Sent before the ready
    /// signal so plugins see the bot with its final mode state.
    /// </summary>
    [Description("User modes to set after auth (e.g. \"+ix\").")]
    public string? UserModes { get => _userModes; init => _userModes = NullIfEmpty(value); }
    private readonly string? _userModes;

    /// <summary>Channels to join on connect.</summary>
    [Description("Channels to join on connect.")]
    public string[] Channels { get; init; } = [];

    /// <summary>The command prefix for plugin commands.</summary>
    [Description("Command prefix for plugin commands.")]
    public string CommandPrefix { get; init; } = "!";

    /// <summary>
    /// Directories to scan for plugin assemblies.
    /// Defaults to a single "plugins" directory relative to the working directory.
    /// </summary>
    [Description("Directories to scan for plugin assemblies.")]
    public string[] PluginDirectories { get; init; } = ["plugins"];

    /// <summary>
    /// Plugin names to load. Only plugins whose name matches an entry in this list
    /// will be activated. If empty, no plugins are loaded.
    /// </summary>
    [Description("Plugin names to load.")]
    public string[] Plugins { get; init; } = [];

    /// <summary>
    /// Whether outbound message rate limiting is enabled.
    /// When false, messages are sent as fast as possible.
    /// </summary>
    [Description("Enable outbound message rate limiting.")]
    public bool RateLimitEnabled { get; init; } = true;

    /// <summary>
    /// Maximum number of messages that can be sent in a burst before
    /// rate limiting kicks in.
    /// </summary>
    [Description("Rate limiter burst size (max messages before throttling).")]
    public int RateLimitBurst { get; init; } = 5;

    /// <summary>
    /// Number of send tokens replenished per second. For example, 0.5 means
    /// one token is added every 2 seconds.
    /// </summary>
    [Description("Rate limiter refill rate (tokens per second).")]
    public double RateLimitRefillRate { get; init; } = 0.5;

    /// <summary>
    /// Timeout in seconds for post-registration authentication (NickServ, OPER)
    /// to complete before proceeding anyway. Set to 0 to wait indefinitely.
    /// </summary>
    [Description("Timeout (seconds) for NickServ/OPER auth; 0 = no timeout.")]
    public int AuthTimeoutSeconds { get; init; } = 15;

    /// <summary>
    /// Sentry DSN for error reporting. When empty or null, Sentry is disabled.
    /// </summary>
    [Description("Sentry DSN for error reporting (empty = disabled).")]
    public string? SentryDsn { get => _sentryDsn; init => _sentryDsn = NullIfEmpty(value); }
    private readonly string? _sentryDsn;

    /// <summary>
    /// Custom response to CTCP VERSION queries. If null, uses the default
    /// "Marv IRC Bot {version}" string. Set to empty string to suppress
    /// VERSION responses entirely.
    /// </summary>
    [Description("Custom CTCP VERSION response (empty = suppress).")]
    public string? CtcpVersionResponse { get; init; }

    /// <summary>
    /// Override for the minimum log level. When set, replaces the default log level
    /// from appsettings.json.
    /// </summary>
    [Description("Override for the default log level.")]
    public LogLevel? LogLevel { get; init; }
}
