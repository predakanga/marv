using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Marv.Core.Irc;
using Marv.Core.Platform;
using Marv.Core.Plugin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Marv.Core;

/// <summary>
/// Extension methods for registering Marv services into an <see cref="IServiceCollection"/>.
/// </summary>
public static class MarvServiceExtensions
{
    /// <summary>
    /// Registers all Marv core services, discovers and loads plugins, and configures
    /// the DI container for the bot. This is the single entry point called by the host application.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMarv(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration from the root (flat layout)
        services.Configure<MarvConfiguration>(configuration);

        // Register connection-scoped core services — fresh instances per connection
        services.AddScoped<ServerInfo>();
        services.AddScoped<IServerInfo>(sp => sp.GetRequiredService<ServerInfo>());
        services.AddScoped<CapabilityManager>();
        services.AddScoped<ICapabilityManager>(sp => sp.GetRequiredService<CapabilityManager>());
        services.AddScoped<IPluginActivator, PluginActivator>();
        services.AddScoped<IrcBot>();
        services.AddScoped<IBot>(sp => sp.GetRequiredService<IrcBot>());
        services.AddScoped<IBotStatistics>(sp => sp.GetRequiredService<IrcBot>().Statistics);

        // Register application-lifetime services
        services.AddSingleton<PluginManager>();

        // Register IHttpClientFactory so plugins can inject it without adding the package themselves
        services.AddHttpClient();

        // Register the hosted service
        services.AddHostedService<MarvBotService>();

        // Discover plugins from configured directories, filtered by name
        var config = configuration.Get<MarvConfiguration>() ?? new MarvConfiguration();

        // Deduplicate plugin directories
        var pluginDirs = PluginManager.DeduplicateDirectories(config.PluginDirectories);

        // Register assembly resolving handlers so plugin transitive dependencies
        // are found by probing the plugin directories and the app base directory.
        RegisterAssemblyResolvers(pluginDirs);

        IReadOnlyList<PluginDescriptor> sortedPlugins = [];
        if (config.Plugins.Length > 0)
        {
            var configuredLogLevel = configuration.GetValue<LogLevel?>("LogLevel");
            using var bootstrapLoggerFactory = LoggerFactory.Create(b =>
            {
                b.AddConsole();
                if (configuredLogLevel.HasValue)
                    b.SetMinimumLevel(configuredLogLevel.Value);
            });
            var bootstrapLogger = bootstrapLoggerFactory.CreateLogger("Marv.Bootstrap");

            // Phase 1: Metadata scan — identify plugins without loading assemblies
            var allPluginMetadata = PluginMetadataScanner.ScanDirectories(pluginDirs, bootstrapLogger);

            // Phase 2: Resolve requested plugin names to assembly paths
            var resolvedPaths = PluginManager.ResolveRequestedPlugins(
                config.Plugins, allPluginMetadata, bootstrapLogger);

            // Phase 3: Load and register
            sortedPlugins = PluginManager.DiscoverAndRegister(
                services, configuration, resolvedPaths, bootstrapLogger);
        }

        // Store the sorted descriptors for later use during instantiation
        services.AddSingleton(sortedPlugins);

        return services;
    }

    /// <summary>
    /// Registers handlers on the default <see cref="AssemblyLoadContext"/> to probe
    /// plugin directories and the application base directory for managed and native
    /// assemblies that are not in the host's dependency graph.
    /// </summary>
    private static void RegisterAssemblyResolvers(IReadOnlyList<string> pluginDirectories)
    {
        // Probe plugin directories + app base directory (for PublishSingleFile support)
        var probeDirs = pluginDirectories
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
        {
            foreach (var dir in probeDirs)
            {
                if (!Directory.Exists(dir))
                    continue;

                var candidate = Path.Combine(dir, assemblyName.Name + ".dll");
                if (File.Exists(candidate))
                    return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
            }

            return null;
        };

        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, unmanagedDllName) =>
        {
            foreach (var dir in probeDirs)
            {
                if (!Directory.Exists(dir))
                    continue;

                var candidate = Path.Combine(dir, unmanagedDllName);
                if (File.Exists(candidate))
                    return NativeLibrary.Load(candidate);
            }

            return IntPtr.Zero;
        };
    }
}
