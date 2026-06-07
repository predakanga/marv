using System.Text.RegularExpressions;

namespace Marv.Core.Plugin;

/// <summary>
/// Context passed to <see cref="OnRegexAttribute"/> handler methods,
/// providing the regex match result and a convenience reply method.
/// </summary>
public sealed class RegexMatchContext : HandlerContext
{
    /// <summary>The regex match result.</summary>
    public required Match Match { get; init; }
}
