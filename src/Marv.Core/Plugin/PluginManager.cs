using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Channels;
using Marv.Core.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Marv.Core.Plugin;

/// <summary>
/// Manages the full plugin lifecycle: discovery, dependency sorting, DI registration,
/// instantiation, event dispatch, and teardown.
/// </summary>
public sealed class PluginManager
{
    private readonly ILogger<PluginManager> _logger;
    private readonly IServiceProvider _serviceProvider;
    private IReadOnlyList<PluginDescriptor> _descriptors = [];
    private List<PluginInstance> _instances = [];

    internal PluginManager(ILogger<PluginManager> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>The loaded and sorted plugin descriptors.</summary>
    internal IReadOnlyList<PluginDescriptor> Descriptors => _descriptors;

    /// <summary>
    /// Phase 1: Discovers plugins from assemblies, sorts them by dependency,
    /// and registers services and configurations into the service collection.
    /// Called during <c>AddMarv</c> before the container is built.
    /// </summary>
    internal static IReadOnlyList<PluginDescriptor> DiscoverAndRegister(
        IServiceCollection services,
        IConfiguration configuration,
        IReadOnlyList<string> pluginPaths,
        ILogger? bootstrapLogger = null)
    {
        var loadContext = AssemblyLoadContext.Default;
        var descriptors = new List<PluginDescriptor>();

        foreach (var path in pluginPaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                bootstrapLogger?.LogWarning("Plugin assembly not found: {Path}", fullPath);
                continue;
            }

            try
            {
                var assembly = loadContext.LoadFromAssemblyPath(fullPath);
                var descriptor = PluginDiscovery.DiscoverPlugin(assembly);
                if (descriptor is not null)
                {
                    descriptors.Add(descriptor);
                    bootstrapLogger?.LogInformation("Discovered plugin: {Name} from {Assembly}",
                        descriptor.Name, assembly.GetName().Name);
                }
            }
            catch (Exception ex)
            {
                bootstrapLogger?.LogError(ex, "Failed to load plugin assembly: {Path}", fullPath);
                throw;
            }
        }

        // Sort by dependencies
        var sorted = PluginDependencySorter.Sort(descriptors);

        // Register plugin configurations
        foreach (var descriptor in sorted)
        {
            foreach (var (configType, section) in descriptor.Configurations)
            {
                var configSection = configuration.GetSection($"Plugins:{section}");
                var method = typeof(OptionsConfigurationServiceCollectionExtensions)
                    .GetMethod("Configure", [typeof(IServiceCollection), typeof(IConfiguration)])!
                    .MakeGenericMethod(configType);
                method.Invoke(null, [services, configSection]);

                bootstrapLogger?.LogDebug("Registered configuration {Type} for section Plugins:{Section}",
                    configType.Name, section);
            }
        }

        // Call ConfigureServices on each plugin (in dependency order)
        foreach (var descriptor in sorted)
        {
            var method = descriptor.PluginType.GetMethod("ConfigureServices",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (method is not null)
            {
                method.Invoke(null, [services]);
                bootstrapLogger?.LogDebug("Called ConfigureServices on {Plugin}", descriptor.Name);
            }
        }

        return sorted;
    }

    /// <summary>
    /// Phase 2: Instantiates all plugins and their handler groups using
    /// ActivatorUtilities. Called after the DI container is built.
    /// </summary>
    internal void InstantiatePlugins(IReadOnlyList<PluginDescriptor> descriptors)
    {
        _descriptors = descriptors;
        _instances = [];

        var activator = _serviceProvider.GetRequiredService<IPluginActivator>();

        foreach (var descriptor in descriptors)
        {
            try
            {
                var plugin = (IPlugin)ActivatorUtilities.CreateInstance(
                    _serviceProvider, descriptor.PluginType);

                _instances.Add(new PluginInstance(descriptor, plugin));
                _logger.LogInformation("Instantiated plugin: {Name}", descriptor.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to instantiate plugin: {Name}", descriptor.Name);
                throw;
            }
        }
    }

    /// <summary>
    /// Calls OnLoadAsync on all plugins in dependency order.
    /// </summary>
    internal async Task LoadPluginsAsync(CancellationToken ct)
    {
        foreach (var instance in _instances)
        {
            try
            {
                await instance.Plugin.OnLoadAsync(ct);
                _logger.LogDebug("Loaded plugin: {Name}", instance.Descriptor.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plugin {Name} failed during OnLoadAsync", instance.Descriptor.Name);
                throw;
            }
        }
    }

    /// <summary>
    /// Calls OnConnectedAsync on all plugins in dependency order.
    /// </summary>
    internal async Task NotifyConnectedAsync(CancellationToken ct)
    {
        foreach (var instance in _instances)
        {
            try
            {
                await instance.Plugin.OnConnectedAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plugin {Name} failed during OnConnectedAsync", instance.Descriptor.Name);
            }
        }
    }

    /// <summary>
    /// Calls OnDisconnectedAsync on all plugins in reverse dependency order.
    /// </summary>
    internal async Task NotifyDisconnectedAsync()
    {
        for (var i = _instances.Count - 1; i >= 0; i--)
        {
            try
            {
                await _instances[i].Plugin.OnDisconnectedAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plugin {Name} failed during OnDisconnectedAsync",
                    _instances[i].Descriptor.Name);
            }
        }
    }

    /// <summary>
    /// Calls OnUnloadAsync on all plugins in reverse dependency order.
    /// </summary>
    internal async Task UnloadPluginsAsync()
    {
        for (var i = _instances.Count - 1; i >= 0; i--)
        {
            try
            {
                await _instances[i].Plugin.OnUnloadAsync();
                _logger.LogDebug("Unloaded plugin: {Name}", _instances[i].Descriptor.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plugin {Name} failed during OnUnloadAsync",
                    _instances[i].Descriptor.Name);
            }
        }
    }

    /// <summary>
    /// Creates per-plugin event channels and starts event dispatch tasks.
    /// Returns the list of channels for the message processor to fan out to.
    /// </summary>
    internal IReadOnlyList<ChannelWriter<MarvEvent>> StartEventDispatchers(CancellationToken ct)
    {
        var writers = new List<ChannelWriter<MarvEvent>>();

        foreach (var instance in _instances)
        {
            var channel = Channel.CreateBounded<MarvEvent>(new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

            writers.Add(channel.Writer);

            var pluginName = instance.Descriptor.Name;
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var evt in channel.Reader.ReadAllAsync(ct))
                    {
                        try
                        {
                            await instance.Plugin.HandleEventAsync(evt, ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Plugin {Name} threw in HandleEventAsync for {EventType}",
                                pluginName, evt.GetType().Name);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown
                }
            }, ct);
        }

        return writers;
    }

    /// <summary>
    /// Fans out an event to all plugin channels.
    /// </summary>
    internal async Task DispatchEventAsync(MarvEvent evt, IReadOnlyList<ChannelWriter<MarvEvent>> writers, CancellationToken ct)
    {
        foreach (var writer in writers)
        {
            try
            {
                await writer.WriteAsync(evt, ct);
            }
            catch (ChannelClosedException)
            {
                // Plugin task has ended
            }
        }
    }

    /// <summary>
    /// Logs diagnostic information about loaded plugins, their services, and dependencies.
    /// </summary>
    internal void LogDiagnostics()
    {
        foreach (var descriptor in _descriptors)
        {
            _logger.LogInformation("Plugin '{Name}' loaded", descriptor.Name);
            foreach (var svc in descriptor.ProvidedServices)
                _logger.LogInformation("  Provides: {Service}", svc.FullName);
            foreach (var svc in descriptor.RequiredServices)
                _logger.LogInformation("  Requires: {Service}", svc.FullName);
            foreach (var svc in descriptor.OptionalServices)
                _logger.LogInformation("  Optional: {Service}", svc.FullName);
        }
    }
}

/// <summary>
/// Pairs a plugin descriptor with its runtime instance.
/// </summary>
internal sealed class PluginInstance(PluginDescriptor descriptor, IPlugin plugin)
{
    public PluginDescriptor Descriptor => descriptor;
    public IPlugin Plugin => plugin;
}
