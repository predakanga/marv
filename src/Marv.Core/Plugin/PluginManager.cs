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
/// instantiation, event dispatch, and teardown. All plugin loading failures are fatal —
/// the bot will not start if any requested plugin cannot be loaded.
/// </summary>
public sealed class PluginManager
{
    private readonly ILogger<PluginManager> _logger;
    private readonly IServiceProvider _serviceProvider;
    private IReadOnlyList<PluginDescriptor> _descriptors = [];
    private List<PluginInstance> _instances = [];

    /// <summary>
    /// Creates a new <see cref="PluginManager"/>.
    /// </summary>
    public PluginManager(ILogger<PluginManager> logger, IServiceProvider serviceProvider)
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
                throw new InvalidOperationException(
                    $"Plugin assembly not found: {fullPath}");

            try
            {
                var assembly = loadContext.LoadFromAssemblyPath(fullPath);
                var descriptor = PluginDiscovery.DiscoverPlugin(assembly, services);
                if (descriptor is not null)
                {
                    descriptors.Add(descriptor);
                    bootstrapLogger?.LogDebug("Discovered plugin: {Name} from {Assembly}",
                        descriptor.Name, assembly.GetName().Name);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Assembly {fullPath} was identified as a plugin during metadata " +
                        $"scanning but contains no IPlugin implementation at runtime.");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to load plugin assembly: {fullPath}. " +
                    $"This usually means the plugin has a dependency that is not present " +
                    $"in the plugin directories. Ensure all required DLLs are placed " +
                    $"in one of the configured plugin directories.", ex);
            }
        }

        // Sort by dependencies
        var sorted = PluginDependencySorter.Sort(descriptors);

        // Register plugin configurations
        foreach (var descriptor in sorted)
        {
            foreach (var (configType, section) in descriptor.Configurations)
            {
                var configSection = configuration.GetSection(section);
                var method = typeof(OptionsConfigurationServiceCollectionExtensions)
                    .GetMethod("Configure", [typeof(IServiceCollection), typeof(IConfiguration)])!
                    .MakeGenericMethod(configType);
                method.Invoke(null, [services, configSection]);

                bootstrapLogger?.LogDebug("Registered configuration {Type} for section {Section}",
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
    /// All failures are fatal.
    /// </summary>
    internal void InstantiatePlugins(IReadOnlyList<PluginDescriptor> descriptors)
    {
        _descriptors = descriptors;
        _instances = [];

        foreach (var descriptor in descriptors)
        {
            try
            {
                var plugin = (IPlugin)ActivatorUtilities.CreateInstance(
                    _serviceProvider, descriptor.PluginType);

                _instances.Add(new PluginInstance(descriptor, plugin));
                _logger.LogDebug("Instantiated plugin: {Name}", descriptor.Name);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to instantiate plugin '{descriptor.Name}' " +
                    $"(type: {descriptor.PluginType.FullName}). " +
                    $"Check that all required services are available.", ex);
            }
        }
    }

    /// <summary>
    /// Calls OnLoadAsync on all plugins in dependency order.
    /// All failures are fatal.
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
                throw new InvalidOperationException(
                    $"Plugin '{instance.Descriptor.Name}' failed during OnLoadAsync.", ex);
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
                _logger.LogDebug("  Plugin '{Name}' provides: {Service}", descriptor.Name, svc.FullName);
            foreach (var svc in descriptor.RequiredServices)
                _logger.LogDebug("  Plugin '{Name}' requires: {Service}", descriptor.Name, svc.FullName);
            foreach (var svc in descriptor.OptionalServices)
                _logger.LogDebug("  Plugin '{Name}' optional: {Service}", descriptor.Name, svc.FullName);
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
