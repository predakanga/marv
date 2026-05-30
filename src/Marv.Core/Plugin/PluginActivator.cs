using Microsoft.Extensions.DependencyInjection;

namespace Marv.Core.Plugin;

/// <summary>
/// Default implementation of <see cref="IPluginActivator"/> that delegates to
/// <see cref="ActivatorUtilities.CreateInstance{T}"/>.
/// </summary>
internal sealed class PluginActivator(IServiceProvider serviceProvider) : IPluginActivator
{
    /// <inheritdoc />
    public T CreateInstance<T>(params object[] parameters) =>
        ActivatorUtilities.CreateInstance<T>(serviceProvider, parameters);
}
