using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Marv.Core.Plugin;

/// <summary>
/// Discovers plugins from loaded assemblies and extracts metadata for dependency sorting
/// and service registration.
/// </summary>
internal static class PluginDiscovery
{
    /// <summary>
    /// Returns true if a constructor parameter type is a core/host service
    /// (not a plugin dependency). Checks the DI service collection for
    /// registered services rather than maintaining a static allowlist.
    /// </summary>
    internal static bool IsCoreService(Type paramType, IServiceCollection services)
    {
        // CancellationToken is passed at invocation time, not via DI
        if (paramType == typeof(CancellationToken))
            return true;

        // Check if the service is already registered in the DI container
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == paramType)
                return true;
        }

        // Check open generic registrations for generic parameter types
        if (paramType.IsGenericType)
        {
            var def = paramType.GetGenericTypeDefinition();
            foreach (var descriptor in services)
            {
                if (descriptor.ServiceType == def)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Scans an assembly for plugin types and extracts metadata.
    /// Each assembly must contain exactly one IPlugin implementation.
    /// </summary>
    public static PluginDescriptor? DiscoverPlugin(Assembly assembly, IServiceCollection services)
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

        var nameAttr = pluginType.GetCustomAttribute<PluginNameAttribute>();
        var name = nameAttr?.Name
            ?? (pluginType.Name.EndsWith("Plugin", StringComparison.Ordinal)
                ? pluginType.Name[..^6]
                : pluginType.Name);

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

            // Skip core services (registered in DI or CancellationToken)
            if (IsCoreService(paramType, services))
                continue;

            // Check for nullable reference type (T? with default null for optional deps)
            var isNullable = Nullable.GetUnderlyingType(paramType) is not null;
            var hasNullableAnnotation = new NullabilityInfoContext().Create(param).WriteState == NullabilityState.Nullable;
            var hasDefault = param.HasDefaultValue && param.DefaultValue is null;

            if ((isNullable || hasNullableAnnotation) && hasDefault)
            {
                var actualType = Nullable.GetUnderlyingType(paramType) ?? paramType;
                if (!IsCoreService(actualType, services))
                    optionalServices.Add(actualType);
            }
            else
            {
                if (!IsCoreService(paramType, services))
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
