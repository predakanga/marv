using System.Collections.Concurrent;
using Marv.Core.Platform;

namespace Marv.Core.Irc;

/// <summary>
/// Mutable implementation of <see cref="IChannel"/>. Properties are updated in place by the
/// message processor. Collection properties use <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// for safe concurrent enumeration.
/// </summary>
public sealed class IrcChannel : IChannel
{
    private volatile string? _topic;
    private volatile string? _topicSetBy;
    private DateTimeOffset? _topicSetAt;
    private DateTimeOffset? _createdAt;

    private readonly ConcurrentDictionary<string, IrcUser> _members;
    private readonly ConcurrentDictionary<string, HashSet<char>> _prefixes;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _joinTimes;
    private readonly ConcurrentDictionary<char, string?> _modes = new();
    private readonly IEqualityComparer<string> _comparer;

    /// <summary>
    /// Creates a new <see cref="IrcChannel"/> with the given name and case mapping comparer.
    /// </summary>
    public IrcChannel(string name, IEqualityComparer<string> comparer)
    {
        Name = name;
        _comparer = comparer;
        _members = new ConcurrentDictionary<string, IrcUser>(comparer);
        _prefixes = new ConcurrentDictionary<string, HashSet<char>>(comparer);
        _joinTimes = new ConcurrentDictionary<string, DateTimeOffset>(comparer);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string? Topic
    {
        get => _topic;
        internal set => _topic = value;
    }

    /// <inheritdoc />
    public string? TopicSetBy
    {
        get => _topicSetBy;
        internal set => _topicSetBy = value;
    }

    /// <inheritdoc />
    public DateTimeOffset? TopicSetAt
    {
        get => _topicSetAt;
        internal set => _topicSetAt = value;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<char, string?> Modes => _modes;

    /// <inheritdoc />
    public IReadOnlyCollection<IUser> Members => _members.Values.ToArray();

    /// <inheritdoc />
    public IReadOnlySet<char> GetPrefixes(string nick)
    {
        if (_prefixes.TryGetValue(nick, out var prefixes))
            return prefixes;
        return new HashSet<char>();
    }

    /// <inheritdoc />
    public DateTimeOffset? GetJoinTime(string nick)
    {
        if (_joinTimes.TryGetValue(nick, out var time))
            return time;
        return null;
    }

    /// <inheritdoc />
    public bool HasMember(string nick) => _members.ContainsKey(nick);

    /// <inheritdoc />
    public bool IsOp(string nick)
    {
        if (_prefixes.TryGetValue(nick, out var prefixes))
            return prefixes.Contains('@');
        return false;
    }

    /// <inheritdoc />
    public bool IsVoiced(string nick)
    {
        if (_prefixes.TryGetValue(nick, out var prefixes))
            return prefixes.Contains('+');
        return false;
    }

    /// <inheritdoc />
    public DateTimeOffset? CreatedAt
    {
        get => _createdAt;
        internal set => _createdAt = value;
    }

    /// <summary>Adds a user to this channel's member list.</summary>
    internal void AddMember(IrcUser user, IEnumerable<char>? prefixes = null)
    {
        _members.TryAdd(user.Nick, user);
        _prefixes.TryAdd(user.Nick, prefixes is not null ? new HashSet<char>(prefixes) : []);
        _joinTimes.TryAdd(user.Nick, DateTimeOffset.UtcNow);
    }

    /// <summary>Removes a user from this channel's member list.</summary>
    internal void RemoveMember(string nick)
    {
        _members.TryRemove(nick, out _);
        _prefixes.TryRemove(nick, out _);
        _joinTimes.TryRemove(nick, out _);
    }

    /// <summary>Adds a prefix to a user in this channel.</summary>
    internal void AddPrefix(string nick, char prefix)
    {
        if (_prefixes.TryGetValue(nick, out var prefixes))
            prefixes.Add(prefix);
    }

    /// <summary>Removes a prefix from a user in this channel.</summary>
    internal void RemovePrefix(string nick, char prefix)
    {
        if (_prefixes.TryGetValue(nick, out var prefixes))
            prefixes.Remove(prefix);
    }

    /// <summary>Renames a member entry when a user changes nick.</summary>
    internal void RenameMember(string oldNick, string newNick, IrcUser user)
    {
        if (_members.TryRemove(oldNick, out _))
            _members.TryAdd(newNick, user);

        if (_prefixes.TryRemove(oldNick, out var prefixes))
            _prefixes.TryAdd(newNick, prefixes);

        if (_joinTimes.TryRemove(oldNick, out var joinTime))
            _joinTimes.TryAdd(newNick, joinTime);
    }

    /// <summary>Sets a channel mode.</summary>
    internal void SetMode(char mode, string? parameter) =>
        _modes[mode] = parameter;

    /// <summary>Unsets a channel mode.</summary>
    internal void UnsetMode(char mode) =>
        _modes.TryRemove(mode, out _);
}
