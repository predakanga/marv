using Marv.Core.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Marv.Core.Plugin;

/// <summary>
/// Interface defining the full plugin contract. All plugins must implement this,
/// either directly or via the <see cref="MarvPlugin"/> convenience base class.
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// Human-readable name for this plugin. Used in log messages, configuration,
    /// and diagnostics.
    /// </summary>
    string PluginName { get; }

    /// <summary>
    /// Called once after the plugin is constructed and all services are available.
    /// Use for one-time initialization.
    /// </summary>
    Task OnLoadAsync(CancellationToken ct);

    /// <summary>
    /// Called each time the bot establishes an IRC connection.
    /// </summary>
    Task OnConnectedAsync(CancellationToken ct);

    /// <summary>
    /// Called when the IRC connection is lost. Any cached IChannel/IUser references
    /// are stale after this call.
    /// </summary>
    Task OnDisconnectedAsync();

    /// <summary>
    /// Called once during shutdown, before the DI container is disposed.
    /// </summary>
    Task OnUnloadAsync();

    /// <summary>
    /// Called by the core's per-plugin event loop to deliver an event.
    /// The core calls this method once per event, sequentially — never
    /// concurrently with itself for the same plugin.
    /// </summary>
    Task HandleEventAsync(MarvEvent evt, CancellationToken ct);

    /// <summary>
    /// Called during DI container setup to register services this plugin provides.
    /// Only plugins that provide services to other plugins need to override this.
    /// Default implementation is a no-op.
    /// </summary>
    static virtual void ConfigureServices(IServiceCollection services) { }
}
