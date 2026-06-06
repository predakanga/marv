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

        // Register core services
        var serverInfo = new ServerInfo();
        var capabilityManager = new CapabilityManager();

        services.AddSingleton<IServerInfo>(serverInfo);
        services.AddSingleton(serverInfo);
        services.AddSingleton<ICapabilityManager>(capabilityManager);
        services.AddSingleton(capabilityManager);
        services.AddSingleton<IPluginActivator, PluginActivator>();
        services.AddSingleton<PluginManager>();

        // Register IHttpClientFactory so plugins can inject it without adding the package themselves
        services.AddHttpClient();

        // Register the bot and hosted service
        services.AddSingleton<IrcBot>();
        services.AddSingleton<IBot>(sp => sp.GetRequiredService<IrcBot>());
        services.AddHostedService<MarvBotService>();

        // Discover plugins from configured directories, filtered by name
        var config = configuration.Get<MarvConfiguration>() ?? new MarvConfiguration();

        // Deduplicate plugin directories
        var pluginDirs = DeduplicateDirectories(config.PluginDirectories);

        // Register assembly resolving handlers so plugin transitive dependencies
        // are found by probing the plugin directories and the app base directory.
        RegisterAssemblyResolvers(pluginDirs);

        IReadOnlyList<PluginDescriptor> sortedPlugins = [];
        if (config.Plugins.Count > 0)
        {
            using var bootstrapLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
            var bootstrapLogger = bootstrapLoggerFactory.CreateLogger("Marv.Bootstrap");

            // Phase 1: Metadata scan — identify plugins without loading assemblies
            var allPluginMetadata = PluginMetadataScanner.ScanDirectories(pluginDirs, bootstrapLogger);

            // Phase 2: Resolve requested plugin names to assembly paths
            var resolvedPaths = ResolveRequestedPlugins(
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

    /// <summary>
    /// Registers handlers on the default <see cref="AssemblyLoadContext"/> to probe
    /// plugin directories and the application base directory for managed and native
    /// assemblies that are not in the host's dependency graph.
    /// </summary>
    private static void RegisterAssemblyResolvers(IReadOnlyList<string> pluginDirectories)
    {
        // Probe plugin directories + app base directory (for PublishSingleFile support)
        var probeDirs = pluginDirectories
            .Append(AppContext.BaseDirectory)
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
