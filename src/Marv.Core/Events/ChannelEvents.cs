using Marv.Core.Platform;

namespace Marv.Core.Events;

/// <summary>Raised when a user joins a channel.</summary>
public sealed class UserJoinedEvent : MarvEvent
{
    /// <summary>The channel that was joined.</summary>
    public required IChannel Channel { get; init; }

    /// <summary>The user who joined.</summary>
    public required IUser User { get; init; }

    /// <summary>The user's services account, if available (from extended-join or account-tag).</summary>
    public string? Account { get; init; }
}

/// <summary>Raised when a user parts (leaves) a channel.</summary>
public sealed class UserPartedEvent : MarvEvent
{
    /// <summary>The channel that was parted.</summary>
    public required IChannel Channel { get; init; }

    /// <summary>The user who parted.</summary>
    public required IUser User { get; init; }

    /// <summary>The part message, if provided.</summary>
    public string? Reason { get; init; }
}

/// <summary>Raised when a user is kicked from a channel.</summary>
public sealed class UserKickedEvent : MarvEvent
{
    /// <summary>The channel from which the user was kicked.</summary>
    public required IChannel Channel { get; init; }

    /// <summary>The user who performed the kick.</summary>
    public required IUser Kicker { get; init; }

    /// <summary>The user who was kicked.</summary>
    public required IUser Kicked { get; init; }

    /// <summary>The kick reason, if provided.</summary>
    public string? Reason { get; init; }
}

/// <summary>Raised when the topic of a channel changes.</summary>
public sealed class TopicChangedEvent : MarvEvent
{
    /// <summary>The channel whose topic changed.</summary>
    public required IChannel Channel { get; init; }

    /// <summary>The user who changed the topic.</summary>
    public required IUser SetBy { get; init; }

    /// <summary>The new topic text.</summary>
    public required string NewTopic { get; init; }
}

/// <summary>Raised when channel modes change.</summary>
public sealed class ModeChangedEvent : MarvEvent
{
    /// <summary>The channel whose modes changed.</summary>
    public required IChannel Channel { get; init; }

    /// <summary>The user who changed the modes.</summary>
    public required IUser SetBy { get; init; }

    /// <summary>The mode changes that were applied.</summary>
    public required IReadOnlyList<ModeChange> Changes { get; init; }
}

/// <summary>Represents a single mode change within a MODE command.</summary>
public sealed class ModeChange
{
    /// <summary>True if the mode is being set (+), false if unset (-).</summary>
    public required bool IsSet { get; init; }

    /// <summary>The mode character.</summary>
    public required char Mode { get; init; }

    /// <summary>The mode parameter, if applicable.</summary>
    public string? Parameter { get; init; }
}

/// <summary>Raised when the bot receives a channel invitation.</summary>
public sealed class InviteReceivedEvent : MarvEvent
{
    /// <summary>The channel the bot was invited to.</summary>
    public required string Channel { get; init; }

    /// <summary>The user who sent the invitation.</summary>
    public required IUser InvitedBy { get; init; }
}
