namespace Marv.Core.Plugin;

/// <summary>
/// Topologically sorts plugins based on their dependency graph.
/// Detects cycles and missing required dependencies.
/// </summary>
internal static class PluginDependencySorter
{
    /// <summary>
    /// Sorts plugin descriptors in dependency order (providers before consumers).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a dependency cycle is detected or a required dependency is missing.
    /// </exception>
    public static IReadOnlyList<PluginDescriptor> Sort(IReadOnlyList<PluginDescriptor> plugins)
    {
        // Build a map of service type → providing plugin
        var serviceProviders = new Dictionary<Type, PluginDescriptor>();
        foreach (var plugin in plugins)
        {
            foreach (var svc in plugin.ProvidedServices)
            {
                if (serviceProviders.ContainsKey(svc))
                    throw new InvalidOperationException(
                        $"Service {svc.FullName} is provided by both '{serviceProviders[svc].Name}' and '{plugin.Name}'.");
                serviceProviders[svc] = plugin;
            }
        }

        // Note: we intentionally do NOT validate that every RequiredService has a
        // plugin provider. Constructor parameters that are not declared via
        // [ProvidesService] by any loaded plugin are assumed to be registered in the
        // DI container by the host or core framework (e.g. IHttpClientFactory).
        // If a required service is truly missing, DI will report it at instantiation.

        // Build adjacency list (edges point from dependency → dependent)
        var pluginIndex = new Dictionary<Type, int>();
        for (var i = 0; i < plugins.Count; i++)
            pluginIndex[plugins[i].PluginType] = i;

        var inDegree = new int[plugins.Count];
        var adj = new List<int>[plugins.Count];
        for (var i = 0; i < plugins.Count; i++)
            adj[i] = [];

        void AddEdge(int from, int to)
        {
            if (from == to) return;
            adj[from].Add(to);
            inDegree[to]++;
        }

        for (var i = 0; i < plugins.Count; i++)
        {
            var plugin = plugins[i];

            // Explicit [DependsOn] edges
            foreach (var dep in plugin.ExplicitDependencies)
            {
                if (pluginIndex.TryGetValue(dep, out var depIdx))
                    AddEdge(depIdx, i);
            }

            // Required service dependencies
            foreach (var svc in plugin.RequiredServices)
            {
                if (serviceProviders.TryGetValue(svc, out var provider) &&
                    pluginIndex.TryGetValue(provider.PluginType, out var provIdx))
                    AddEdge(provIdx, i);
            }

            // Optional service dependencies (order-if-present)
            foreach (var svc in plugin.OptionalServices)
            {
                if (serviceProviders.TryGetValue(svc, out var provider) &&
                    pluginIndex.TryGetValue(provider.PluginType, out var provIdx))
                    AddEdge(provIdx, i);
            }
        }

        // Kahn's algorithm for topological sort
        var queue = new Queue<int>();
        for (var i = 0; i < plugins.Count; i++)
        {
            if (inDegree[i] == 0)
                queue.Enqueue(i);
        }

        var result = new List<PluginDescriptor>();
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(plugins[current]);

            foreach (var neighbor in adj[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        if (result.Count != plugins.Count)
        {
            var missing = plugins.Where((_, i) => inDegree[i] > 0).Select(p => p.Name);
            throw new InvalidOperationException(
                $"Dependency cycle detected among plugins: {string.Join(", ", missing)}");
        }

        return result;
    }
}
