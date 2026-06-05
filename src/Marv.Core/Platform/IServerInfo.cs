using Marv.Core.Protocol;

namespace Marv.Core.Platform;

/// <summary>
/// Provides access to server configuration advertised through ISUPPORT (005) numerics.
/// Updated live as the server sends ISUPPORT messages.
/// </summary>
public interface IServerInfo
{
    /// <summary>The network name, if advertised.</summary>
    string? NetworkName { get; }

    /// <summary>The case mapping used by this server for nick/channel comparisons.</summary>
    CaseMappingType CaseMapping { get; }

    /// <summary>The channel mode type classifications (A/B/C/D) from CHANMODES.</summary>
    ChannelModeTypes ChannelModes { get; }

    /// <summary>The prefix-to-mode mapping (e.g. @ → o, + → v) from PREFIX.</summary>
    PrefixMapping Prefix { get; }

    /// <summary>The maximum number of channels the bot can join, if advertised.</summary>
    int? MaxChannels { get; }

    /// <summary>The maximum nick length, if advertised.</summary>
    int? MaxNickLength { get; }

    /// <summary>The maximum topic length, if advertised.</summary>
    int? MaxTopicLength { get; }

    /// <summary>
    /// The maximum message length (default 512 minus CRLF and overhead).
    /// </summary>
    int MaxMessageLength { get; }

    /// <summary>The channel type prefixes supported by this server (e.g. '#', '&amp;').</summary>
    IReadOnlySet<char> ChannelTypes { get; }

    /// <summary>
    /// The server's Message of the Day, or null if the server sent no MOTD.
    /// Each entry is one line of the MOTD text.
    /// </summary>
    IReadOnlyList<string>? Motd { get; }

    /// <summary>Returns whether the server advertises the specified ISUPPORT token.</summary>
    bool Supports(string token);

    /// <summary>Gets the value of the specified ISUPPORT token, or null if not present.</summary>
    string? GetValue(string token);
}
