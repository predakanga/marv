using System.Reflection;

namespace Marv.Core.Plugin;

/// <summary>
/// Describes a handler about to be invoked. Passed to
/// <see cref="MarvPlugin.FilterHandlerAsync"/> for pre-invocation filtering.
/// </summary>
public readonly struct HandlerInvocation
{
    /// <summary>The handler method that will be invoked.</summary>
    public required MethodInfo Method { get; init; }

    /// <summary>The target object (plugin instance or handler group instance).</summary>
    public required object Target { get; init; }

    /// <summary>The handler type.</summary>
    public required HandlerType Type { get; init; }

    /// <summary>
    /// The context object passed to the handler. Cast to the appropriate type
    /// based on <see cref="Type"/>: <see cref="CommandContext"/>,
    /// <see cref="RegexMatchContext"/>, the event type,
    /// <see cref="Protocol.IrcMessage"/> (raw), or null (interval).
    /// </summary>
    public required object? Context { get; init; }

    /// <summary>
    /// Custom attributes on the handler method, pre-cached during discovery.
    /// </summary>
    public required IReadOnlyList<Attribute> Attributes { get; init; }
}
