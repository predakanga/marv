using System.Collections.Concurrent;
using Marv.Core.Platform;

namespace Marv.Core.Irc;

/// <summary>
/// Mutable implementation of <see cref="IUser"/>. Properties are updated in place by the
/// message processor. Individual property reads are atomic; cross-property consistency
/// is not guaranteed during a handler.
/// </summary>
public sealed class IrcUser : IUser
{
    private volatile string _nick;
    private volatile string? _user;
    private volatile string? _host;
    private volatile string? _account;
    private volatile string? _realName;
    private volatile bool _isAway;
    private volatile string? _awayMessage;
    private volatile bool _isBot;

    private readonly ConcurrentDictionary<string, IrcChannel> _channels;
    private readonly IEqualityComparer<string> _comparer;

    /// <summary>
    /// Creates a new <see cref="IrcUser"/> with the given nick and case mapping comparer.
    /// </summary>
    public IrcUser(string nick, IEqualityComparer<string> comparer)
    {
        _nick = nick;
        _comparer = comparer;
        _channels = new ConcurrentDictionary<string, IrcChannel>(comparer);
    }

    /// <inheritdoc />
    public string Nick
    {
        get => _nick;
        internal set => _nick = value;
    }

    /// <inheritdoc />
    public string? User
    {
        get => _user;
        internal set => _user = value;
    }

    /// <inheritdoc />
    public string? Host
    {
        get => _host;
        internal set => _host = value;
    }

    /// <inheritdoc />
    public string? Account
    {
        get => _account;
        internal set => _account = value;
    }

    /// <inheritdoc />
    public string? RealName
    {
        get => _realName;
        internal set => _realName = value;
    }

    /// <inheritdoc />
    public bool IsAway
    {
        get => _isAway;
        internal set => _isAway = value;
    }

    /// <inheritdoc />
    public string? AwayMessage
    {
        get => _awayMessage;
        internal set => _awayMessage = value;
    }

    /// <inheritdoc />
    public bool IsBot
    {
        get => _isBot;
        internal set => _isBot = value;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IChannel> Channels => _channels.Values.ToArray();

    /// <inheritdoc />
    public string Hostmask
    {
        get
        {
            var nick = _nick;
            var user = _user;
            var host = _host;
            var result = nick;
            if (user is not null) result += "!" + user;
            if (host is not null) result += "@" + host;
            return result;
        }
    }

    /// <summary>Adds a channel reference to this user's channel set.</summary>
    internal void AddChannel(IrcChannel channel) =>
        _channels.TryAdd(channel.Name, channel);

    /// <summary>Removes a channel reference from this user's channel set.</summary>
    internal void RemoveChannel(string channelName) =>
        _channels.TryRemove(channelName, out _);

    /// <summary>Updates the channel key after a channel name change (shouldn't happen, but for robustness).</summary>
    internal bool IsInChannel(string channelName) =>
        _channels.ContainsKey(channelName);
}
