namespace Marv.Core.Plugin;

/// <summary>
/// Creates instances of types using the DI container for constructor parameter resolution.
/// Used by <see cref="MarvPlugin"/> to create handler group instances.
/// </summary>
public interface IPluginActivator
{
    /// <summary>
    /// Creates an instance of <typeparamref name="T"/>, resolving constructor parameters
    /// from the DI container. Additional parameters can be passed to satisfy constructor
    /// arguments not registered in DI.
    /// </summary>
    T CreateInstance<T>(params object[] parameters);
}
