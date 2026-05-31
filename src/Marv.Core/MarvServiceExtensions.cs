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
    /// <param name="pluginPaths">Paths to plugin assemblies to load.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMarv(
        this IServiceCollection services,
        IConfiguration configuration,
        IReadOnlyList<string>? pluginPaths = null)
    {
        // Bind IRC configuration
        services.Configure<IrcConfiguration>(configuration.GetSection("Irc"));

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

        // Discover plugins and register their services/configurations
        IReadOnlyList<PluginDescriptor> sortedPlugins = [];
        if (pluginPaths is { Count: > 0 })
        {
            using var bootstrapLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
            var bootstrapLogger = bootstrapLoggerFactory.CreateLogger("Marv.Bootstrap");

            sortedPlugins = PluginManager.DiscoverAndRegister(
                services, configuration, pluginPaths, bootstrapLogger);
        }

        // Store the sorted descriptors for later use during instantiation
        services.AddSingleton(sortedPlugins);

        return services;
    }
}
