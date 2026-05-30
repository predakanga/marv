using Marv.Core.Platform;
using Marv.Core.Protocol;

namespace Marv.Core.Plugin;

/// <summary>
/// Context passed to <see cref="OnCommandAttribute"/> handler methods,
/// providing the parsed command, arguments, and a convenience reply method.
/// </summary>
public sealed class CommandContext
{
    /// <summary>The matched command name (without the prefix).</summary>
    public required string Command { get; init; }

    /// <summary>The remaining words after the command, split by whitespace.</summary>
    public required IReadOnlyList<string> Args { get; init; }

    /// <summary>The remaining text after the command, unparsed.</summary>
    public required string ArgString { get; init; }

    /// <summary>The channel the command was sent in, or null for private messages.</summary>
    public IChannel? Channel { get; init; }

    /// <summary>The user who sent the command.</summary>
    public required IUser Sender { get; init; }

    /// <summary>True if this is a direct (private) message to the bot.</summary>
    public bool IsDirect => Channel is null;

    /// <summary>The underlying IRC message.</summary>
    public required IrcMessage RawMessage { get; init; }

    /// <summary>The bot instance, used for sending replies.</summary>
    public required IBot Bot { get; init; }

    /// <summary>
    /// Sends a reply in context — to the channel if the command was in a channel,
    /// or directly to the sender if it was a private message.
    /// </summary>
    public Task ReplyAsync(string text, CancellationToken ct = default)
    {
        var target = Channel?.Name ?? Sender.Nick;
        return Bot.SendMessageAsync(target, text, ct);
    }
}
