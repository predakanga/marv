using Marv.Core.Platform;

namespace Marv.Core.Plugin;

/// <summary>
/// Typed base class for filter evaluators. Provides a type-safe bridge
/// so evaluators receive their specific attribute type without casting.
/// </summary>
/// <typeparam name="TAttribute">
/// The concrete attribute type this evaluator handles.
/// </typeparam>
public abstract class FilterEvaluator<TAttribute> : IFilterEvaluator
    where TAttribute : Attribute, IFilteringAttribute
{
    /// <inheritdoc />
    ValueTask<FilterResult> IFilterEvaluator.EvaluateAsync(
        IFilteringAttribute attribute,
        HandlerInvocation invocation,
        IBot bot,
        CancellationToken ct)
        => EvaluateAsync((TAttribute)attribute, invocation, bot, ct);

    /// <summary>
    /// Evaluates whether the handler should proceed, given the typed attribute
    /// and the invocation context. Use <paramref name="bot"/> to send replies
    /// or take other IRC actions when denying.
    /// </summary>
    protected abstract ValueTask<FilterResult> EvaluateAsync(
        TAttribute attribute,
        HandlerInvocation invocation,
        IBot bot,
        CancellationToken ct);
}
