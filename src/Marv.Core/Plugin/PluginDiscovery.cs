using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Marv.Core.Events;
using Marv.Core.Platform;

namespace Marv.Core.Plugin;

/// <summary>
/// Discovers plugins from loaded assemblies and extracts metadata for dependency sorting
/// and service registration.
/// </summary>
internal static class PluginDiscovery
{
    /// <summary>
    /// Types that are provided by the core and should not be treated as plugin service dependencies.
    /// </summary>
    private static readonly HashSet<Type> CoreServiceTypes =
    [
        typeof(IBot),
        typeof(IPluginActivator),
        typeof(ICapabilityManager),
        typeof(IServerInfo),
        typeof(CancellationToken)
    ];

    /// <summary>
    /// Returns true if a constructor parameter type is a core service (not a plugin dependency).
    /// </summary>
    private static bool IsCoreService(Type paramType)
    {
        if (CoreServiceTypes.Contains(paramType))
            return true;

        // IOptions<T>, ILogger<T>, ILoggerFactory
        if (paramType.IsGenericType)
        {
            var def = paramType.GetGenericTypeDefinition();
            if (def == typeof(IOptions<>) || def == typeof(ILogger<>))
                return true;
        }

        if (paramType == typeof(ILoggerFactory))
            return true;

        return false;
    }

    /// <summary>
    /// Scans an assembly for plugin types and extracts metadata.
    /// Each assembly must contain exactly one IPlugin implementation.
    /// </summary>
    public static PluginDescriptor? DiscoverPlugin(Assembly assembly)
    {
        var pluginTypes = assembly.GetExportedTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        t.GetInterfaces().Any(i => i == typeof(IPlugin)))
            .ToList();

        if (pluginTypes.Count == 0)
            return null;

        if (pluginTypes.Count > 1)
            throw new InvalidOperationException(
                $"Assembly {assembly.GetName().Name} contains {pluginTypes.Count} IPlugin implementations, but exactly one is required.");

        var pluginType = pluginTypes[0];

        // PluginName is an instance property — we can't read it without instantiation,
        // so derive a name from the type name at discovery time. The actual value is
        // available after the plugin is constructed.
        var name = pluginType.Name.EndsWith("Plugin", StringComparison.Ordinal)
            ? pluginType.Name[..^6]
            : pluginType.Name;

        // Read [ProvidesService] attributes
        var providedServices = pluginType.GetCustomAttributes<ProvidesServiceAttribute>()
            .Select(a => a.ServiceType)
            .ToList();

        // Read [DependsOn] attributes
        var explicitDeps = pluginType.GetCustomAttributes<DependsOnAttribute>()
            .Select(a => a.PluginType)
            .ToList();

        // Inspect constructor parameters for service dependencies
        var ctor = pluginType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var requiredServices = new List<Type>();
        var optionalServices = new List<Type>();

        foreach (var param in ctor.GetParameters())
        {
            var paramType = param.ParameterType;

            // Skip core services
            if (IsCoreService(paramType))
                continue;

            // Check for nullable reference type (T? with default null for optional deps)
            var isNullable = Nullable.GetUnderlyingType(paramType) is not null;
            var hasNullableAnnotation = new NullabilityInfoContext().Create(param).WriteState == NullabilityState.Nullable;
            var hasDefault = param.HasDefaultValue && param.DefaultValue is null;

            if ((isNullable || hasNullableAnnotation) && hasDefault)
            {
                var actualType = Nullable.GetUnderlyingType(paramType) ?? paramType;
                if (!IsCoreService(actualType))
                    optionalServices.Add(actualType);
            }
            else
            {
                if (!IsCoreService(paramType))
                    requiredServices.Add(paramType);
            }
        }

        // Discover [PluginConfig] classes in the assembly
        var configs = assembly.GetExportedTypes()
            .Select(t => (Type: t, Attr: t.GetCustomAttribute<PluginConfigAttribute>()))
            .Where(x => x.Attr is not null)
            .Select(x => (x.Type, x.Attr!.Section))
            .ToList();

        return new PluginDescriptor
        {
            PluginType = pluginType,
            Name = name,
            ProvidedServices = providedServices,
            ExplicitDependencies = explicitDeps,
            RequiredServices = requiredServices,
            OptionalServices = optionalServices,
            Configurations = configs,
            Assembly = assembly
        };
    }
}
