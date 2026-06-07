namespace Marv.Core.Plugin;

/// <summary>
/// Context passed to <see cref="OnCommandAttribute"/> handler methods,
/// providing the parsed command, arguments, and a convenience reply method.
/// </summary>
public sealed class CommandContext : HandlerContext
{
    /// <summary>The matched command name (without the prefix).</summary>
    public required string Command { get; init; }

    /// <summary>The remaining words after the command, split by whitespace.</summary>
    public required IReadOnlyList<string> Args { get; init; }

    /// <summary>The remaining text after the command, unparsed.</summary>
    public required string ArgString { get; init; }
}
