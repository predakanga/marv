using Marv.Core.Protocol;

namespace Marv.Core.Platform;

/// <summary>
/// The primary interface plugins interact with. Provides methods for sending messages,
/// querying channel/user state, and checking capability availability.
/// All Send*Async methods are thread-safe and can be called from any context.
/// </summary>
public interface IBot
{
    /// <summary>The bot's own user identity.</summary>
    IUser Self { get; }

    /// <summary>Sends a PRIVMSG to the specified target (channel or nick).</summary>
    Task SendMessageAsync(string target, string text, CancellationToken ct);

    /// <summary>Sends a NOTICE to the specified target (channel or nick).</summary>
    Task SendNoticeAsync(string target, string text, CancellationToken ct);

    /// <summary>Sends a CTCP ACTION to the specified target.</summary>
    Task SendActionAsync(string target, string text, CancellationToken ct);

    /// <summary>Sends a raw IRC message through the connection.</summary>
    Task SendRawAsync(IrcMessage message, CancellationToken ct);

    /// <summary>Joins a channel, optionally with a key.</summary>
    Task JoinAsync(string channel, string? key, CancellationToken ct);

    /// <summary>Parts (leaves) a channel, optionally with a reason.</summary>
    Task PartAsync(string channel, string? reason, CancellationToken ct);

    /// <summary>
    /// Dictionary of channels the bot is in, keyed by case-mapped channel name.
    /// </summary>
    IReadOnlyDictionary<string, IChannel> Channels { get; }

    /// <summary>
    /// Dictionary of known users, keyed by case-mapped nick.
    /// </summary>
    IReadOnlyDictionary<string, IUser> Users { get; }

    /// <summary>Server configuration from ISUPPORT.</summary>
    IServerInfo ServerInfo { get; }

    /// <summary>IRCv3 capability negotiation state.</summary>
    ICapabilityManager Capabilities { get; }

    /// <summary>
    /// Sends an IRC command and waits for the server's correlated response.
    /// Uses labeled-response to correlate, or falls back to timeout-based correlation.
    /// </summary>
    Task<IReadOnlyList<IrcMessage>> SendAndAwaitAsync(IrcMessage message, CancellationToken ct);
}
