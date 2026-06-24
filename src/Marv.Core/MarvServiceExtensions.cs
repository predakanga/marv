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

        // Discover, resolve, and register plugins
        var config = configuration.Get<MarvConfiguration>() ?? new MarvConfiguration();
        var configuredLogLevel = configuration.GetValue<LogLevel?>("LogLevel");
        using var bootstrapLoggerFactory = LoggerFactory.Create(b =>
        {
            b.AddConsole();
            if (configuredLogLevel.HasValue)
                b.SetMinimumLevel(configuredLogLevel.Value);
        });
        var bootstrapLogger = bootstrapLoggerFactory.CreateLogger("Marv.Bootstrap");

        var sortedPlugins = PluginManager.ResolveAndRegister(
            services, configuration, config, bootstrapLogger);

        services.AddSingleton(sortedPlugins);

        return services;
    }
}
