namespace Marv.Core.Plugin;

/// <summary>
/// Metadata about a discovered plugin, extracted from assembly scanning.
/// Used during the bootstrap phase for dependency sorting and service registration.
/// </summary>
internal sealed class PluginDescriptor
{
    /// <summary>The plugin implementation type.</summary>
    public required Type PluginType { get; init; }

    /// <summary>The human-readable plugin name, from [PluginName] or derived from the class name.</summary>
    public required string Name { get; init; }

    /// <summary>Service types this plugin provides (from [ProvidesService] attributes).</summary>
    public required IReadOnlyList<Type> ProvidedServices { get; init; }

    /// <summary>Plugin types this plugin explicitly depends on (from [DependsOn] attributes).</summary>
    public required IReadOnlyList<Type> ExplicitDependencies { get; init; }

    /// <summary>
    /// Required service types consumed by this plugin (non-nullable constructor parameters
    /// that are not core services).
    /// </summary>
    public required IReadOnlyList<Type> RequiredServices { get; init; }

    /// <summary>
    /// Optional service types consumed by this plugin (nullable constructor parameters
    /// with default null).
    /// </summary>
    public required IReadOnlyList<Type> OptionalServices { get; init; }

    /// <summary>Configuration types found in this plugin's assembly (tagged with [PluginConfig]).</summary>
    public required IReadOnlyList<(Type ConfigType, string Section)> Configurations { get; init; }

    /// <summary>The assembly from which this plugin was loaded.</summary>
    public required System.Reflection.Assembly Assembly { get; init; }
}
