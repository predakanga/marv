using System.IO.Enumeration;
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
    private IReadOnlyList<PluginDescriptor> _descriptors = [];
    private List<PluginInstance> _instances = [];

    /// <summary>
    /// Creates a new <see cref="PluginManager"/>.
    /// </summary>
    public PluginManager(ILogger<PluginManager> logger)
    {
        _logger = logger;
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
    /// <param name="descriptors">The sorted plugin descriptors to instantiate.</param>
    /// <param name="scopedProvider">
    /// The connection-scoped <see cref="IServiceProvider"/> to resolve dependencies from.
    /// </param>
    internal void InstantiatePlugins(IReadOnlyList<PluginDescriptor> descriptors, IServiceProvider scopedProvider)
    {
        _descriptors = descriptors;
        _instances = [];

        foreach (var descriptor in descriptors)
        {
            try
            {
                var plugin = (IPlugin)ActivatorUtilities.CreateInstance(
                    scopedProvider, descriptor.PluginType);

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

    /// <summary>
    /// Expands wildcard and negation patterns in the plugin list against
    /// discovered plugin metadata. Plain names pass through unchanged;
    /// glob patterns (<c>*</c>, <c>?</c>) match against plugin names;
    /// negation patterns (<c>!</c> prefix) remove previously matched names.
    /// Patterns are evaluated left-to-right.
    /// </summary>
    internal static IReadOnlyList<string> ExpandPluginPatterns(
        IReadOnlyList<string> patterns,
        IReadOnlyList<PluginMetadata> allMetadata,
        ILogger? logger = null)
    {
        var result = new List<string>();
        var resultSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in patterns)
        {
            if (pattern.StartsWith('!'))
            {
                var inner = pattern[1..];
                if (IsGlobPattern(inner))
                {
                    var removed = result.RemoveAll(name =>
                        FileSystemName.MatchesSimpleExpression(inner, name, ignoreCase: true));
                    if (removed > 0)
                    {
                        resultSet.Clear();
                        foreach (var name in result)
                            resultSet.Add(name);
                        logger?.LogInformation(
                            "Negation pattern '{Pattern}' excluded {Count} plugin(s)",
                            pattern, removed);
                    }
                }
                else
                {
                    if (resultSet.Remove(inner))
                    {
                        result.RemoveAll(name =>
                            string.Equals(name, inner, StringComparison.OrdinalIgnoreCase));
                        logger?.LogInformation(
                            "Negation pattern '{Pattern}' excluded plugin '{Name}'",
                            pattern, inner);
                    }
                }
            }
            else if (IsGlobPattern(pattern))
            {
                var matched = new List<string>();
                foreach (var meta in allMetadata)
                {
                    if (FileSystemName.MatchesSimpleExpression(pattern, meta.Name, ignoreCase: true)
                        && resultSet.Add(meta.Name))
                    {
                        result.Add(meta.Name);
                        matched.Add(meta.Name);
                    }
                }

                if (matched.Count > 0)
                    logger?.LogInformation(
                        "Pattern '{Pattern}' matched plugin(s): {Names}",
                        pattern, string.Join(", ", matched));
                else
                    logger?.LogDebug(
                        "Pattern '{Pattern}' matched no plugins", pattern);
            }
            else
            {
                if (resultSet.Add(pattern))
                    result.Add(pattern);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns true if the pattern contains glob wildcard characters.
    /// </summary>
    private static bool IsGlobPattern(string pattern) =>
        pattern.Contains('*') || pattern.Contains('?');

    /// <summary>
    /// Deduplicates plugin directories by normalizing to absolute paths.
    /// </summary>
    internal static IReadOnlyList<string> DeduplicateDirectories(IReadOnlyList<string> directories)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var dir in directories)
        {
            var fullPath = Path.GetFullPath(dir);
            if (seen.Add(fullPath))
                result.Add(fullPath);
        }

        return result;
    }

    /// <summary>
    /// Resolves each requested plugin name to an assembly path using the metadata
    /// scan results. Matching order:
    /// 1. Exact plugin name match (case-insensitive).
    /// 2. Assembly filename convention match (e.g., "CannedResponses" matches
    ///    Marv.Plugins.CannedResponses.dll) — logs a warning.
    /// 3. No match — fatal error with suggestions.
    /// </summary>
    internal static IReadOnlyList<string> ResolveRequestedPlugins(
        IReadOnlyList<string> requestedPlugins,
        IReadOnlyList<PluginMetadata> allMetadata,
        ILogger? logger = null)
    {
        var resolvedPaths = new List<string>();
        var resolvedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var requested in requestedPlugins)
        {
            // 1. Exact plugin name match
            var exact = allMetadata.FirstOrDefault(
                m => string.Equals(m.Name, requested, StringComparison.OrdinalIgnoreCase));

            if (exact is not null)
            {
                if (resolvedSet.Add(exact.AssemblyPath))
                    resolvedPaths.Add(exact.AssemblyPath);
                else
                    logger?.LogWarning(
                        "Plugin '{Name}' (from {Path}) was already resolved. " +
                        "Skipping duplicate. Check your Plugins config for repeated entries",
                        exact.Name, exact.AssemblyPath);
                continue;
            }

            // 2. Assembly filename convention match
            var conventionMatch = allMetadata.FirstOrDefault(m =>
                string.Equals(
                    PluginMetadataScanner.DeriveNameFromAssemblyFile(m.AssemblyFileName),
                    requested,
                    StringComparison.OrdinalIgnoreCase));

            if (conventionMatch is not null)
            {
                logger?.LogWarning(
                    "Plugin '{Requested}' matched by assembly filename convention to " +
                    "plugin '{ActualName}' (from {Path}). Consider updating your config " +
                    "to use the canonical plugin name '{ActualName}'",
                    requested, conventionMatch.Name, conventionMatch.AssemblyPath,
                    conventionMatch.Name);

                if (resolvedSet.Add(conventionMatch.AssemblyPath))
                    resolvedPaths.Add(conventionMatch.AssemblyPath);
                continue;
            }

            // 3. No match — fatal error with suggestions
            var availableNames = allMetadata
                .Select(m => $"  - '{m.Name}' (from {m.AssemblyFileName})")
                .ToList();

            var suggestion = FindClosestMatch(requested, allMetadata);
            var suggestionText = suggestion is not null
                ? $"\n\n  Did you mean '{suggestion}'?"
                : "";

            var availableList = availableNames.Count > 0
                ? "\n\n  Available plugins in configured directories:\n" +
                  string.Join("\n", availableNames)
                : "\n\n  No plugins were found in the configured directories.";

            throw new InvalidOperationException(
                $"Plugin '{requested}' was requested in config but no plugin with " +
                $"that name was found.{availableList}{suggestionText}");
        }

        return resolvedPaths;
    }

    /// <summary>
    /// Finds the closest matching plugin name for a "did you mean?" suggestion.
    /// Uses case-insensitive substring matching and Levenshtein distance.
    /// </summary>
    private static string? FindClosestMatch(
        string requested, IReadOnlyList<PluginMetadata> allMetadata)
    {
        var requestedLower = requested.ToLowerInvariant();
        string? bestMatch = null;
        var bestDistance = int.MaxValue;

        foreach (var meta in allMetadata)
        {
            // Check plugin name
            var distance = LevenshteinDistance(requestedLower, meta.Name.ToLowerInvariant());
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestMatch = meta.Name;
            }

            // Check assembly-derived name
            var assemblyName = PluginMetadataScanner.DeriveNameFromAssemblyFile(meta.AssemblyFileName);
            distance = LevenshteinDistance(requestedLower, assemblyName.ToLowerInvariant());
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestMatch = meta.Name;
            }
        }

        // Only suggest if the distance is reasonable (less than half the length)
        if (bestMatch is not null && bestDistance <= Math.Max(requested.Length, bestMatch.Length) / 2)
            return bestMatch;

        return null;
    }

    /// <summary>
    /// Computes the Levenshtein edit distance between two strings.
    /// </summary>
    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
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
