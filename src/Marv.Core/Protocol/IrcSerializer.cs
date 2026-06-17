using System.Text;

namespace Marv.Core.Protocol;

/// <summary>
/// Serializes <see cref="IrcMessage"/> instances to IRC wire format.
/// Handles tag value escaping per the IRCv3 message-tags spec.
/// </summary>
public static class IrcSerializer
{
    /// <summary>
    /// Serializes an <see cref="IrcMessage"/> to wire format (without trailing CR-LF).
    /// </summary>
    public static string Serialize(IrcMessage message)
    {
        var sb = new StringBuilder(512);

        // Tags
        if (message.Tags.Count > 0)
        {
            sb.Append('@');
            var first = true;
            foreach (var (key, value) in message.Tags)
            {
                if (!first) sb.Append(';');
                first = false;

                sb.Append(key);
                if (value is not null && value.Length > 0)
                {
                    sb.Append('=');
                    EscapeTagValue(sb, value);
                }
            }
            sb.Append(' ');
        }

        // Source
        if (message.Source is not null)
        {
            sb.Append(':');
            sb.Append(message.Source);
            sb.Append(' ');
        }

        // Command
        sb.Append(message.Command);

        // Parameters
        for (var i = 0; i < message.Parameters.Count; i++)
        {
            sb.Append(' ');
            var param = message.Parameters[i];

            if (i == message.Parameters.Count - 1)
            {
                sb.Append(':');
            }

            sb.Append(param);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Escapes a tag value per the IRCv3 message-tags spec:
    /// ; → \:  space → \s  \ → \\  CR → \r  LF → \n
    /// </summary>
    internal static void EscapeTagValue(StringBuilder sb, string value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case ';': sb.Append("\\:"); break;
                case ' ': sb.Append("\\s"); break;
                case '\\': sb.Append("\\\\"); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                default: sb.Append(c); break;
            }
        }
    }
}
