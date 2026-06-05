using Marv.Core.Platform;

namespace Marv.Core.Plugin;

/// <summary>
/// Evaluates whether a handler should be invoked, based on the associated
/// <see cref="IFilteringAttribute"/> and the invocation context.
/// Receives the <see cref="IBot"/> instance so that evaluators can take
/// action (send replies, kick users, etc.) when denying a handler.
/// </summary>
public interface IFilterEvaluator
{
    /// <summary>
    /// Returns a <see cref="FilterResult"/> indicating whether the handler
    /// should proceed. The evaluator may use <paramref name="bot"/> to
    /// send denial messages or take other IRC actions directly.
    /// </summary>
    ValueTask<FilterResult> EvaluateAsync(
        IFilteringAttribute attribute,
        HandlerInvocation invocation,
        IBot bot,
        CancellationToken ct);
}
