namespace Marv.Core.Protocol;

/// <summary>
/// Parses raw IRC protocol lines into <see cref="IrcMessage"/> instances.
/// Handles IRCv3 message tags, source prefixes, and the trailing parameter.
/// Validated against the ircdocs/parser-tests test vectors.
/// </summary>
public static class IrcParser
{
    /// <summary>
    /// Parses a raw IRC line into an <see cref="IrcMessage"/>.
    /// </summary>
    /// <param name="line">The raw line from the server, without the trailing CR-LF.</param>
    /// <returns>The parsed message, or null if the line is empty or malformed.</returns>
    public static IrcMessage? Parse(string line)
    {
        if (string.IsNullOrEmpty(line))
            return null;

        var span = line.AsSpan();
        int pos = 0;

        // Parse tags (starts with '@')
        Dictionary<string, string?>? tags = null;
        if (span[pos] == '@')
        {
            pos++;
            var tagEnd = span[pos..].IndexOf(' ');
            if (tagEnd < 0)
                return null;

            tags = ParseTags(span[pos..(pos + tagEnd)]);
            pos += tagEnd;
            pos = SkipSpaces(span, pos);
        }

        if (pos >= span.Length)
            return null;

        // Parse source (starts with ':')
        MessageSource? source = null;
        if (span[pos] == ':')
        {
            pos++;
            var sourceEnd = span[pos..].IndexOf(' ');
            if (sourceEnd < 0)
            {
                // Source with no command — malformed
                return null;
            }

            source = ParseSource(span[pos..(pos + sourceEnd)]);
            pos += sourceEnd;
            pos = SkipSpaces(span, pos);
        }

        if (pos >= span.Length)
            return null;

        // Parse command (verb)
        var cmdEnd = span[pos..].IndexOf(' ');
        string command;
        if (cmdEnd < 0)
        {
            command = span[pos..].ToString();
            return new IrcMessage(tags?.AsReadOnly(), source, command, Array.Empty<string>());
        }

        command = span[pos..(pos + cmdEnd)].ToString();
        pos += cmdEnd;
        pos = SkipSpaces(span, pos);

        // Parse parameters
        var parameters = new List<string>();
        while (pos < span.Length)
        {
            if (span[pos] == ':')
            {
                // Trailing parameter — everything after the colon
                parameters.Add(span[(pos + 1)..].ToString());
                break;
            }

            var paramEnd = span[pos..].IndexOf(' ');
            if (paramEnd < 0)
            {
                parameters.Add(span[pos..].ToString());
                break;
            }

            parameters.Add(span[pos..(pos + paramEnd)].ToString());
            pos += paramEnd;
            pos = SkipSpaces(span, pos);
        }

        return new IrcMessage(tags?.AsReadOnly(), source, command, parameters);
    }

    /// <summary>
    /// Parses the source prefix into its nick, user, and host components.
    /// Handles formats: "nick", "nick@host", "nick!user@host", and bare server names.
    /// </summary>
    internal static MessageSource ParseSource(ReadOnlySpan<char> source)
    {
        var str = source.ToString();
        string? nick = null, user = null, host = null;

        var bangIndex = str.IndexOf('!');
        var atIndex = str.IndexOf('@');

        if (bangIndex >= 0 && atIndex > bangIndex)
        {
            // nick!user@host
            nick = str[..bangIndex];
            user = str[(bangIndex + 1)..atIndex];
            host = str[(atIndex + 1)..];
        }
        else if (atIndex >= 0)
        {
            // nick@host (no user)
            nick = str[..atIndex];
            host = str[(atIndex + 1)..];
        }
        else if (str.Contains('.'))
        {
            // Bare server name (contains dots, no nick separators)
            host = str;
        }
        else
        {
            // Just a nick
            nick = str;
        }

        return new MessageSource(nick, user, host);
    }

    /// <summary>
    /// Parses IRCv3 message tags. Tag values are unescaped per the spec:
    /// \: → ;  \s → space  \\ → \  \r → CR  \n → LF
    /// A trailing backslash produces no output character.
    /// An unrecognized escape (e.g. \b) drops the backslash.
    /// </summary>
    internal static Dictionary<string, string?> ParseTags(ReadOnlySpan<char> tagString)
    {
        var tags = new Dictionary<string, string?>();

        foreach (var tagRange in tagString.Split(';'))
        {
            var tag = tagString[tagRange];
            if (tag.IsEmpty)
                continue;

            var eqIndex = tag.IndexOf('=');
            if (eqIndex < 0)
            {
                tags[tag.ToString()] = "";
            }
            else
            {
                var key = tag[..eqIndex].ToString();
                var rawValue = tag[(eqIndex + 1)..];
                tags[key] = UnescapeTagValue(rawValue);
            }
        }

        return tags;
    }

    /// <summary>
    /// Unescapes an IRC tag value according to the IRCv3 message-tags spec.
    /// </summary>
    internal static string UnescapeTagValue(ReadOnlySpan<char> value)
    {
        // Fast path: no backslash means no escaping needed
        if (value.IndexOf('\\') < 0)
            return value.ToString();

        var result = new char[value.Length];
        int resultLen = 0;

        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\')
            {
                i++;
                if (i >= value.Length)
                    break; // Trailing backslash — drop it

                result[resultLen++] = value[i] switch
                {
                    ':' => ';',
                    's' => ' ',
                    '\\' => '\\',
                    'r' => '\r',
                    'n' => '\n',
                    _ => value[i] // Unknown escape — drop the backslash, keep the char
                };
            }
            else
            {
                result[resultLen++] = value[i];
            }
        }

        return new string(result, 0, resultLen);
    }

    private static int SkipSpaces(ReadOnlySpan<char> span, int pos)
    {
        while (pos < span.Length && span[pos] == ' ')
            pos++;
        return pos;
    }
}

internal static class ReadOnlyDictionaryExtensions
{
    public static IReadOnlyDictionary<TKey, TValue> AsReadOnly<TKey, TValue>(
        this Dictionary<TKey, TValue> dict) where TKey : notnull
        => dict;
}
