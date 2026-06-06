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

    /// <summary>The configured command prefix (e.g. "!").</summary>
    string CommandPrefix { get; }

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

    /// <summary>
    /// Joins multiple channels in a single IRC JOIN command.
    /// Channel names are comma-separated per RFC 2812. Automatically
    /// batches into multiple commands if the channel list exceeds the
    /// 512-byte IRC line length limit. Channels with keys should use
    /// <see cref="JoinAsync"/> individually.
    /// </summary>
    Task JoinMultipleAsync(IReadOnlyList<string> channels, CancellationToken ct);

    /// <summary>Parts (leaves) a channel, optionally with a reason.</summary>
    Task PartAsync(string channel, string? reason, CancellationToken ct);

    /// <summary>Kicks a user from a channel, optionally with a reason.</summary>
    Task KickAsync(string channel, string nick, string? reason, CancellationToken ct);

    /// <summary>Sets or changes the topic of a channel.</summary>
    Task SetTopicAsync(string channel, string topic, CancellationToken ct);

    /// <summary>Invites a user to a channel.</summary>
    Task InviteAsync(string nick, string channel, CancellationToken ct);

    /// <summary>
    /// Sets a mode on a channel or user. The mode string should include the
    /// +/- prefix (e.g. "+i", "-b"). For user modes, pass the bot's own nick
    /// as the target.
    /// </summary>
    Task SetModeAsync(string target, string modeString, CancellationToken ct);

    /// <summary>
    /// Sets a mode on a channel or user with a parameter (e.g. "+b", "nick!*@*").
    /// </summary>
    Task SetModeAsync(string target, string modeString, string parameter, CancellationToken ct);

    /// <summary>Gives operator status (+o) to a user in a channel.</summary>
    Task GiveOpAsync(string channel, string nick, CancellationToken ct);

    /// <summary>Removes operator status (-o) from a user in a channel.</summary>
    Task RemoveOpAsync(string channel, string nick, CancellationToken ct);

    /// <summary>Gives voice status (+v) to a user in a channel.</summary>
    Task GiveVoiceAsync(string channel, string nick, CancellationToken ct);

    /// <summary>Removes voice status (-v) from a user in a channel.</summary>
    Task RemoveVoiceAsync(string channel, string nick, CancellationToken ct);

    /// <summary>Changes the bot's nickname.</summary>
    Task ChangeNickAsync(string newNick, CancellationToken ct);

    /// <summary>
    /// Dictionary of channels the bot is in, keyed by case-mapped channel name.
    /// </summary>
    IReadOnlyDictionary<string, IChannel> Channels { get; }

    /// <summary>
    /// Dictionary of known users, keyed by case-mapped nick.
    /// </summary>
    IReadOnlyDictionary<string, IUser> Users { get; }

    /// <summary>
    /// String comparer that uses the server's case mapping rules (rfc1459, ascii, etc.)
    /// for comparing nicks and channel names. Defaults to rfc1459 before connection.
    /// Plugins should treat collections built with this comparer as connection-scoped
    /// state — rebuild them in <see cref="Plugin.IPlugin.OnConnectedAsync"/> since the
    /// comparer instance may change between connections.
    /// </summary>
    IEqualityComparer<string> CaseComparer { get; }

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
