using System.Text.RegularExpressions;
using Marv.Core.Platform;
using Marv.Core.Protocol;

namespace Marv.Core.Plugin;

/// <summary>
/// Context passed to <see cref="OnRegexAttribute"/> handler methods,
/// providing the regex match result and a convenience reply method.
/// </summary>
public sealed class RegexMatchContext
{
    /// <summary>The regex match result.</summary>
    public required Match Match { get; init; }

    /// <summary>The channel the message was sent in, or null for private messages.</summary>
    public IChannel? Channel { get; init; }

    /// <summary>The user who sent the message.</summary>
    public required IUser Sender { get; init; }

    /// <summary>True if this is a direct (private) message to the bot.</summary>
    public bool IsDirect => Channel is null;

    /// <summary>The underlying IRC message.</summary>
    public required IrcMessage RawMessage { get; init; }

    /// <summary>The bot instance, used for sending replies.</summary>
    public required IBot Bot { get; init; }

    /// <summary>
    /// Sends a reply in context — to the channel if the message was in a channel,
    /// or directly to the sender if it was a private message.
    /// </summary>
    public Task ReplyAsync(string text, CancellationToken ct = default)
    {
        var target = Channel?.Name ?? Sender.Nick;
        return Bot.SendMessageAsync(target, text, ct);
    }
}
