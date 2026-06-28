namespace Marv.Core;

/// <summary>
/// The bot's public identity — name, version, and optional source URL.
/// Used in CTCP VERSION responses, the !version command, Sentry reports,
/// and anywhere else the bot identifies itself.
/// </summary>
public record BotIdentity(string Name, string Version, string? SourceUrl = null)
{
    /// <summary>
    /// Combined name and version string (e.g. "Marv IRC Bot 0.8.0").
    /// </summary>
    public string FullIdentity => $"{Name} {Version}";
}
