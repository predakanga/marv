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

        // Register core services
        var serverInfo = new ServerInfo();
        var capabilityManager = new CapabilityManager();

        services.AddSingleton<IServerInfo>(serverInfo);
        services.AddSingleton(serverInfo);
        services.AddSingleton<ICapabilityManager>(capabilityManager);
        services.AddSingleton(capabilityManager);
        services.AddSingleton<IPluginActivator, PluginActivator>();
        services.AddSingleton<PluginManager>();

        // Register the bot and hosted service
        services.AddSingleton<IrcBot>();
        services.AddSingleton<IBot>(sp => sp.GetRequiredService<IrcBot>());
        services.AddHostedService<MarvBotService>();

        // Discover plugins from configured directories, filtered by name
        var config = configuration.Get<MarvConfiguration>() ?? new MarvConfiguration();
        var pluginPaths = ResolvePluginPaths(config.PluginDirectories, config.Plugins);

        IReadOnlyList<PluginDescriptor> sortedPlugins = [];
        if (pluginPaths.Count > 0)
        {
            using var bootstrapLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
            var bootstrapLogger = bootstrapLoggerFactory.CreateLogger("Marv.Bootstrap");

            sortedPlugins = PluginManager.DiscoverAndRegister(
                services, configuration, pluginPaths, config.Plugins, bootstrapLogger);
        }

        // Store the sorted descriptors for later use during instantiation
        services.AddSingleton(sortedPlugins);

        return services;
    }

    /// <summary>
    /// Scans plugin directories for assemblies, discovers which ones contain plugins,
    /// and returns paths to assemblies whose plugin name matches the requested list.
    /// </summary>
    private static IReadOnlyList<string> ResolvePluginPaths(
        List<string> pluginDirectories,
        List<string> requestedPlugins)
    {
        if (requestedPlugins.Count == 0)
            return [];

        var paths = new List<string>();

        foreach (var dir in pluginDirectories)
        {
            var fullDir = Path.GetFullPath(dir);
            if (!Directory.Exists(fullDir))
                continue;

            foreach (var dll in Directory.GetFiles(fullDir, "*.dll", SearchOption.AllDirectories))
            {
                paths.Add(dll);
            }
        }

        return paths;
    }
}
