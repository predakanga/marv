using Marv.Core.Platform;
using Marv.Core.Protocol;

namespace Marv.Core.Plugin;

/// <summary>
/// Base class for handler contexts that carry sender, channel, and message
/// information. Shared by <see cref="CommandContext"/> and
/// <see cref="RegexMatchContext"/>.
/// </summary>
public abstract class HandlerContext
{
    /// <summary>The channel the message was sent in, or null for DMs.</summary>
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
    /// Sends a reply in context — to the channel if the message was in a
    /// channel, or directly to the sender if it was a private message.
    /// </summary>
    public Task ReplyAsync(string text, CancellationToken ct = default)
    {
        var target = Channel?.Name ?? Sender.Nick;
        return Bot.SendMessageAsync(target, text, ct);
    }
}
