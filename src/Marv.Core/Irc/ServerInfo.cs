using System.Collections.Concurrent;
using Marv.Core.Platform;
using Marv.Core.Protocol;

namespace Marv.Core.Irc;

/// <summary>
/// Mutable implementation of <see cref="IServerInfo"/>. Updated as the server sends
/// ISUPPORT (005) messages.
/// </summary>
public sealed class ServerInfo : IServerInfo
{
    private readonly ConcurrentDictionary<string, string?> _tokens = new(StringComparer.OrdinalIgnoreCase);

    private volatile string? _networkName;
    private volatile CaseMappingType _caseMapping = CaseMappingType.Rfc1459;
    private volatile ChannelModeTypes _channelModes = ChannelModeTypes.Default;
    private volatile PrefixMapping _prefix = PrefixMapping.Default;
    private int? _maxChannels;
    private int? _maxNickLength;
    private int? _maxTopicLength;
    private int _maxMessageLength = 510; // 512 minus CR-LF
    private volatile HashSet<char> _channelTypes = ['#', '&'];

    /// <inheritdoc />
    public string? NetworkName => _networkName;

    /// <inheritdoc />
    public CaseMappingType CaseMapping => _caseMapping;

    /// <inheritdoc />
    public ChannelModeTypes ChannelModes => _channelModes;

    /// <inheritdoc />
    public PrefixMapping Prefix => _prefix;

    /// <inheritdoc />
    public int? MaxChannels => _maxChannels;

    /// <inheritdoc />
    public int? MaxNickLength => _maxNickLength;

    /// <inheritdoc />
    public int? MaxTopicLength => _maxTopicLength;

    /// <inheritdoc />
    public int MaxMessageLength => _maxMessageLength;

    /// <inheritdoc />
    public IReadOnlySet<char> ChannelTypes => _channelTypes;

    /// <inheritdoc />
    public bool Supports(string token) => _tokens.ContainsKey(token);

    /// <inheritdoc />
    public string? GetValue(string token) =>
        _tokens.TryGetValue(token, out var value) ? value : null;

    /// <summary>
    /// Processes an ISUPPORT token (key=value or key).
    /// Called by the message processor when 005 numerics arrive.
    /// </summary>
    internal void SetToken(string key, string? value)
    {
        _tokens[key] = value;

        switch (key.ToUpperInvariant())
        {
            case "NETWORK":
                _networkName = value;
                break;
            case "CASEMAPPING":
                _caseMapping = value?.ToLowerInvariant() switch
                {
                    "ascii" => CaseMappingType.Ascii,
                    "strict-rfc1459" => CaseMappingType.StrictRfc1459,
                    _ => CaseMappingType.Rfc1459
                };
                break;
            case "CHANMODES":
                if (value is not null)
                    _channelModes = ChannelModeTypes.Parse(value);
                break;
            case "PREFIX":
                if (value is not null)
                    _prefix = PrefixMapping.Parse(value);
                break;
            case "CHANLIMIT":
            case "MAXCHANNELS":
                if (value is not null && int.TryParse(value.Split(':').Last(), out var maxChan))
                    _maxChannels = maxChan;
                break;
            case "NICKLEN":
                if (value is not null && int.TryParse(value, out var nickLen))
                    _maxNickLength = nickLen;
                break;
            case "TOPICLEN":
                if (value is not null && int.TryParse(value, out var topicLen))
                    _maxTopicLength = topicLen;
                break;
            case "LINELEN":
                if (value is not null && int.TryParse(value, out var lineLen))
                    _maxMessageLength = lineLen - 2; // Subtract CR-LF
                break;
            case "CHANTYPES":
                if (value is not null)
                    _channelTypes = new HashSet<char>(value);
                break;
        }
    }

    /// <summary>Resets all state, typically on disconnection.</summary>
    internal void Reset()
    {
        _tokens.Clear();
        _networkName = null;
        _caseMapping = CaseMappingType.Rfc1459;
        _channelModes = ChannelModeTypes.Default;
        _prefix = PrefixMapping.Default;
        _maxChannels = null;
        _maxNickLength = null;
        _maxTopicLength = null;
        _maxMessageLength = 510;
        _channelTypes = ['#', '&'];
    }
}
