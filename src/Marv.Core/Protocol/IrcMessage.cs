namespace Marv.Core.Protocol;

/// <summary>
/// Immutable representation of an IRC message, used for both inbound and outbound messages.
/// The trailing parameter (after ':') is always folded into <see cref="Parameters"/>
/// as the last element — there is no separate trailing property.
/// </summary>
public sealed class IrcMessage
{
    /// <summary>IRCv3 message tags, with values already unescaped.</summary>
    public IReadOnlyDictionary<string, string?> Tags { get; }

    /// <summary>
    /// The message source (nick!user@host prefix). Null for outbound messages.
    /// </summary>
    public MessageSource? Source { get; }

    /// <summary>The IRC command, always uppercase (e.g. "PRIVMSG", "001", "CAP").</summary>
    public string Command { get; }

    /// <summary>The message parameters, including any trailing parameter as the last element.</summary>
    public IReadOnlyList<string> Parameters { get; }

    /// <summary>
    /// Creates a new IRC message with all components.
    /// </summary>
    public IrcMessage(
        IReadOnlyDictionary<string, string?>? tags,
        MessageSource? source,
        string command,
        IReadOnlyList<string> parameters)
    {
        Tags = tags ?? EmptyTags;
        Source = source;
        Command = command.ToUpperInvariant();
        Parameters = parameters;
    }

    /// <summary>
    /// Creates a new outbound IRC message (no source, no tags).
    /// </summary>
    public IrcMessage(string command, IReadOnlyList<string> parameters)
        : this(null, null, command, parameters)
    {
    }

    /// <summary>
    /// Creates a new outbound IRC message with tags (no source).
    /// </summary>
    public IrcMessage(IReadOnlyDictionary<string, string?> tags, string command, IReadOnlyList<string> parameters)
        : this(tags, null, command, parameters)
    {
    }

    private static readonly IReadOnlyDictionary<string, string?> EmptyTags =
        new Dictionary<string, string?>().AsReadOnly();
}
