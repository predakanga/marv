namespace Marv.Core.Protocol;

/// <summary>
/// Represents the source (prefix) of an IRC message, parsed into its components.
/// Server-originated messages may have only a <see cref="Host"/> with no nick or user.
/// </summary>
public sealed class MessageSource
{
    /// <summary>The nickname component, if present.</summary>
    public string? Nick { get; }

    /// <summary>The username (ident) component, if present.</summary>
    public string? User { get; }

    /// <summary>The hostname component, if present.</summary>
    public string? Host { get; }

    /// <summary>
    /// Creates a new <see cref="MessageSource"/> with the specified components.
    /// </summary>
    public MessageSource(string? nick, string? user, string? host)
    {
        Nick = nick;
        User = user;
        Host = host;
    }

    /// <summary>
    /// Returns the full hostmask representation (nick!user@host),
    /// omitting missing components.
    /// </summary>
    public override string ToString()
    {
        if (Nick is null)
            return Host ?? "";

        var result = Nick;
        if (User is not null)
            result += "!" + User;
        if (Host is not null)
            result += "@" + Host;
        return result;
    }
}
