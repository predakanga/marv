namespace Marv.Core.Plugin;

/// <summary>
/// Marker interface for attributes that participate in handler filtering.
/// Attributes implementing this interface carry a reference to their evaluator
/// type, which is resolved from the DI container to perform the filter check.
/// </summary>
public interface IFilteringAttribute
{
    /// <summary>
    /// The evaluator type that implements <see cref="IFilterEvaluator"/>.
    /// Must be a concrete class resolvable from the DI container or via
    /// <see cref="IPluginActivator"/>.
    /// </summary>
    Type EvaluatorType { get; }
}
