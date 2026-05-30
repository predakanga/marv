namespace Marv.Core.Platform;

/// <summary>
/// Represents a channel the bot is currently a member of.
/// Properties are mutable — the message processor updates them in place.
/// Collection properties use concurrent-safe data structures for safe enumeration.
/// </summary>
public interface IChannel
{
    /// <summary>The channel name (e.g. "#channel").</summary>
    string Name { get; }

    /// <summary>The channel topic, if set.</summary>
    string? Topic { get; }

    /// <summary>The nick or mask that set the current topic.</summary>
    string? TopicSetBy { get; }

    /// <summary>When the current topic was set.</summary>
    DateTimeOffset? TopicSetAt { get; }

    /// <summary>The channel's modes and their parameters (mode char → parameter or null).</summary>
    IReadOnlyDictionary<char, string?> Modes { get; }

    /// <summary>The users currently in this channel.</summary>
    IReadOnlyCollection<IUser> Members { get; }

    /// <summary>Gets the prefix modes (e.g. '@', '+') for a user in this channel.</summary>
    IReadOnlySet<char> GetPrefixes(string nick);

    /// <summary>Gets when a user joined this channel, if known.</summary>
    DateTimeOffset? GetJoinTime(string nick);

    /// <summary>Returns whether the specified nick is a member of this channel.</summary>
    bool HasMember(string nick);

    /// <summary>Returns whether the specified nick has operator status ('@') in this channel.</summary>
    bool IsOp(string nick);

    /// <summary>Returns whether the specified nick has voice status ('+') in this channel.</summary>
    bool IsVoiced(string nick);

    /// <summary>When this channel was created, if known (from RPL_CREATIONTIME).</summary>
    DateTimeOffset? CreatedAt { get; }
}
