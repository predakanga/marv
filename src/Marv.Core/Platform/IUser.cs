namespace Marv.Core.Platform;

/// <summary>
/// Represents a user the bot is aware of through shared channels or direct interaction.
/// Properties are mutable — the message processor updates them in place when state changes
/// occur (NICK, CHGHOST, AWAY, etc.). Individual property reads are atomic; cross-property
/// consistency is not guaranteed during a handler.
/// </summary>
public interface IUser
{
    /// <summary>The user's current nickname.</summary>
    string Nick { get; }

    /// <summary>The user's ident/username, if known.</summary>
    string? User { get; }

    /// <summary>The user's hostname, if known.</summary>
    string? Host { get; }

    /// <summary>The user's services account name, if known (from account-tag, extended-join, or WHOX).</summary>
    string? Account { get; }

    /// <summary>The user's real name (GECOS), if known (from extended-join or WHOIS).</summary>
    string? RealName { get; }

    /// <summary>Whether the user is currently marked as away (from away-notify).</summary>
    bool IsAway { get; }

    /// <summary>The user's away message, if away.</summary>
    string? AwayMessage { get; }

    /// <summary>Whether the user is flagged as a bot (from bot tag or bot mode).</summary>
    bool IsBot { get; }

    /// <summary>The channels this user shares with the bot.</summary>
    IReadOnlyCollection<IChannel> Channels { get; }

    /// <summary>The full hostmask in nick!user@host format.</summary>
    string Hostmask { get; }
}
