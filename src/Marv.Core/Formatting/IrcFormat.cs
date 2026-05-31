using System.Text.RegularExpressions;

namespace Marv.Core.Formatting;

/// <summary>
/// Provides IRC formatting helpers at two levels:
/// <list type="bullet">
///   <item>
///     <b>Wrap-and-reset</b> — <c>IrcFormat.Bold("text")</c> wraps the text with
///     the appropriate toggle codes so formatting is self-contained.
///   </item>
///   <item>
///     <b>Raw codes</b> — constants like <see cref="BoldCode"/> for building
///     stateful formatting in string interpolation.
///   </item>
/// </list>
/// </summary>
public static partial class IrcFormat
{
    /// <summary>Bold toggle character (<c>\x02</c>).</summary>
    public const string BoldCode = "\x02";

    /// <summary>Italic toggle character (<c>\x1D</c>).</summary>
    public const string ItalicCode = "\x1D";

    /// <summary>Underline toggle character (<c>\x1F</c>).</summary>
    public const string UnderlineCode = "\x1F";

    /// <summary>Strikethrough toggle character (<c>\x1E</c>).</summary>
    public const string StrikethroughCode = "\x1E";

    /// <summary>Monospace toggle character (<c>\x11</c>).</summary>
    public const string MonospaceCode = "\x11";

    /// <summary>Reverse (swap foreground/background) toggle character (<c>\x16</c>).</summary>
    public const string ReverseCode = "\x16";

    /// <summary>Reset character (<c>\x0F</c>). Clears all formatting.</summary>
    public const string Reset = "\x0F";

    /// <summary>Bare color code (<c>\x03</c>). Resets color when not followed by digits.</summary>
    public const string ColorReset = "\x03";

    /// <summary>Wraps <paramref name="text"/> in bold toggle codes.</summary>
    public static string Bold(string text) => $"\x02{text}\x02";

    /// <summary>Wraps <paramref name="text"/> in italic toggle codes.</summary>
    public static string Italic(string text) => $"\x1D{text}\x1D";

    /// <summary>Wraps <paramref name="text"/> in underline toggle codes.</summary>
    public static string Underline(string text) => $"\x1F{text}\x1F";

    /// <summary>Wraps <paramref name="text"/> in strikethrough toggle codes.</summary>
    public static string Strikethrough(string text) => $"\x1E{text}\x1E";

    /// <summary>Wraps <paramref name="text"/> in monospace toggle codes.</summary>
    public static string Monospace(string text) => $"\x11{text}\x11";

    /// <summary>Wraps <paramref name="text"/> in reverse toggle codes.</summary>
    public static string Reverse(string text) => $"\x16{text}\x16";

    /// <summary>
    /// Wraps <paramref name="text"/> with the specified foreground color,
    /// followed by a color reset.
    /// </summary>
    public static string Color(string text, IrcColor fg) =>
        $"{fg}{text}\x03";

    /// <summary>
    /// Wraps <paramref name="text"/> with the specified foreground and background colors,
    /// followed by a color reset.
    /// </summary>
    public static string Color(string text, IrcColor fg, IrcColor bg) =>
        $"{fg.On(bg)}{text}\x03";

    /// <summary>
    /// Removes all IRC formatting codes from <paramref name="text"/>, returning
    /// the plain text content.
    /// </summary>
    /// <remarks>
    /// Strips: bold, italic, underline, strikethrough, monospace, reverse, reset,
    /// color codes (including foreground/background digit arguments), and hex color codes.
    /// </remarks>
    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return StripPattern().Replace(text, "");
    }

    // \x03 optionally followed by up to 2 digits, optionally followed by comma and up to 2 digits
    // \x04 optionally followed by 6 hex digits, optionally followed by comma and 6 hex digits
    // Single-char formatting codes: \x02 \x0F \x11 \x16 \x1D \x1E \x1F
    [GeneratedRegex(@"\x03(\d{1,2}(,\d{1,2})?)?|\x04([0-9A-Fa-f]{6}(,[0-9A-Fa-f]{6})?)?|[\x02\x0F\x11\x16\x1D\x1E\x1F]")]
    private static partial Regex StripPattern();
}
