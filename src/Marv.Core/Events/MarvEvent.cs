using Marv.Core.Protocol;

namespace Marv.Core.Events;

/// <summary>
/// Base class for all events dispatched to plugins. Carries the raw IRC message
/// and common metadata extracted from message tags.
/// </summary>
public abstract class MarvEvent
{
    /// <summary>
    /// Timestamp from the server-time tag, or the local clock if server-time
    /// is not negotiated.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The underlying IRC message that triggered this event.</summary>
    public required IrcMessage RawMessage { get; init; }

    /// <summary>The unique message ID from the msgid tag, if present.</summary>
    public string? MessageId { get; init; }

    /// <summary>The batch identifier, if this message is part of a batch.</summary>
    public string? BatchId { get; init; }
}
