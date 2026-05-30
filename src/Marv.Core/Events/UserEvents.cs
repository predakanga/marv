using Marv.Core.Platform;

namespace Marv.Core.Events;

/// <summary>Raised when a user disconnects from the IRC server.</summary>
public sealed class UserQuitEvent : MarvEvent
{
    /// <summary>The user who quit.</summary>
    public required IUser User { get; init; }

    /// <summary>The quit message, if provided.</summary>
    public string? Reason { get; init; }

    /// <summary>The channels the user was in when they quit.</summary>
    public required IReadOnlyList<IChannel> AffectedChannels { get; init; }
}

/// <summary>Raised when a user changes their nickname.</summary>
public sealed class NickChangedEvent : MarvEvent
{
    /// <summary>The user whose nick changed (already updated to the new nick).</summary>
    public required IUser User { get; init; }

    /// <summary>The previous nickname.</summary>
    public required string OldNick { get; init; }

    /// <summary>The new nickname.</summary>
    public required string NewNick { get; init; }
}

/// <summary>Raised when a user's services account changes (login/logout).</summary>
public sealed class AccountChangedEvent : MarvEvent
{
    /// <summary>The user whose account changed.</summary>
    public required IUser User { get; init; }

    /// <summary>The previous account name, or null if they were not logged in.</summary>
    public string? OldAccount { get; init; }

    /// <summary>The new account name, or null if they logged out.</summary>
    public string? NewAccount { get; init; }
}

/// <summary>Raised when a user's away status changes.</summary>
public sealed class AwayChangedEvent : MarvEvent
{
    /// <summary>The user whose away status changed.</summary>
    public required IUser User { get; init; }

    /// <summary>Whether the user is now away.</summary>
    public required bool IsAway { get; init; }

    /// <summary>The away message, if the user is going away.</summary>
    public string? Message { get; init; }
}

/// <summary>Raised when a user's hostname changes (e.g. after cloaking).</summary>
public sealed class HostChangedEvent : MarvEvent
{
    /// <summary>The user whose host changed.</summary>
    public required IUser User { get; init; }

    /// <summary>The previous hostname.</summary>
    public required string OldHost { get; init; }

    /// <summary>The new hostname.</summary>
    public required string NewHost { get; init; }
}
