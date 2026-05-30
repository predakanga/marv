using Marv.Core.Platform;

namespace Marv.Core.Events;

/// <summary>
/// Raised when a PRIVMSG is received. Uses <see cref="IsDirect"/> to distinguish
/// between channel messages and direct (private) messages.
/// </summary>
public sealed class MessageEvent : MarvEvent
{
    /// <summary>The channel the message was sent to, or null for direct messages.</summary>
    public IChannel? Channel { get; init; }

    /// <summary>The user who sent the message.</summary>
    public required IUser Sender { get; init; }

    /// <summary>The message text.</summary>
    public required string Text { get; init; }

    /// <summary>True if this is a direct (private) message to the bot.</summary>
    public bool IsDirect => Channel is null;

    /// <summary>The msgid of the message being replied to, if this is a reply (from +reply tag).</summary>
    public string? ReplyTo { get; init; }
}

/// <summary>Raised when a NOTICE is received.</summary>
public sealed class NoticeEvent : MarvEvent
{
    /// <summary>The channel the notice was sent to, or null for direct notices.</summary>
    public IChannel? Channel { get; init; }

    /// <summary>The user who sent the notice.</summary>
    public required IUser Sender { get; init; }

    /// <summary>The notice text.</summary>
    public required string Text { get; init; }

    /// <summary>True if this is a direct (private) notice to the bot.</summary>
    public bool IsDirect => Channel is null;
}

/// <summary>Raised when a CTCP ACTION (/me) is received.</summary>
public sealed class ActionEvent : MarvEvent
{
    /// <summary>The channel the action was sent to, or null for direct actions.</summary>
    public IChannel? Channel { get; init; }

    /// <summary>The user who performed the action.</summary>
    public required IUser Sender { get; init; }

    /// <summary>The action text.</summary>
    public required string Text { get; init; }

    /// <summary>True if this is a direct (private) action.</summary>
    public bool IsDirect => Channel is null;
}

/// <summary>
/// Raised for CTCP queries not handled by the core (VERSION, PING, TIME are handled internally).
/// ACTION is dispatched as <see cref="ActionEvent"/> instead.
/// </summary>
public sealed class CtcpEvent : MarvEvent
{
    /// <summary>The user who sent the CTCP query.</summary>
    public required IUser Sender { get; init; }

    /// <summary>The CTCP command name (e.g. "SOURCE").</summary>
    public required string Command { get; init; }

    /// <summary>Arguments after the CTCP command, if any.</summary>
    public string? Args { get; init; }

    /// <summary>True if this was sent directly to the bot (not to a channel).</summary>
    public bool IsDirect { get; init; }
}
