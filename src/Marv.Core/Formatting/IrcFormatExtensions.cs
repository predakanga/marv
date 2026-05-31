namespace Marv.Core.Formatting;

/// <summary>
/// String extension methods for IRC formatting. These are convenience wrappers
/// around <see cref="IrcFormat"/> for terser inline usage.
/// </summary>
public static class IrcFormatExtensions
{
    /// <summary>Wraps the string in bold toggle codes.</summary>
    public static string Bold(this string text) => IrcFormat.Bold(text);

    /// <summary>Wraps the string in italic toggle codes.</summary>
    public static string Italic(this string text) => IrcFormat.Italic(text);

    /// <summary>Wraps the string in underline toggle codes.</summary>
    public static string Underline(this string text) => IrcFormat.Underline(text);

    /// <summary>Wraps the string in strikethrough toggle codes.</summary>
    public static string Strikethrough(this string text) => IrcFormat.Strikethrough(text);

    /// <summary>Wraps the string in monospace toggle codes.</summary>
    public static string Monospace(this string text) => IrcFormat.Monospace(text);

    /// <summary>Wraps the string in reverse toggle codes.</summary>
    public static string Reverse(this string text) => IrcFormat.Reverse(text);

    /// <summary>Wraps the string with the specified foreground color, followed by a color reset.</summary>
    public static string Color(this string text, IrcColor fg) => IrcFormat.Color(text, fg);

    /// <summary>Wraps the string with foreground and background colors, followed by a color reset.</summary>
    public static string Color(this string text, IrcColor fg, IrcColor bg) => IrcFormat.Color(text, fg, bg);
}
