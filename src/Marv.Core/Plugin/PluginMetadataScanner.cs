using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Marv.Core.Plugin;

/// <summary>
/// Lightweight metadata about a plugin discovered via <see cref="MetadataLoadContext"/>
/// without loading the assembly into the runtime.
/// </summary>
internal sealed record PluginMetadata(string Name, string AssemblyPath, string AssemblyFileName);

/// <summary>
/// Scans plugin directories for DLLs containing <see cref="IPlugin"/> implementations
/// using <see cref="MetadataLoadContext"/> to avoid loading non-plugin assemblies into
/// the runtime. Extracts plugin names from <c>[PluginName]</c> attributes or class name
/// conventions without executing any plugin code.
/// </summary>
internal static class PluginMetadataScanner
{
    private const string PluginInterfaceFullName = "Marv.Core.Plugin.IPlugin";
    private const string MarvPluginBaseFullName = "Marv.Core.Plugin.MarvPlugin";
    private const string PluginNameAttributeFullName = "Marv.Core.Plugin.PluginNameAttribute";
    private const string PluginSuffix = "Plugin";

    /// <summary>
    /// Scans the given directories for DLLs that contain an <see cref="IPlugin"/>
    /// implementation. Returns metadata for each discovered plugin without loading
    /// assemblies into the runtime.
    /// </summary>
    /// <param name="pluginDirectories">Deduplicated, absolute plugin directory paths.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <returns>Metadata for every plugin found across all directories.</returns>
    public static IReadOnlyList<PluginMetadata> ScanDirectories(
        IReadOnlyList<string> pluginDirectories,
        ILogger? logger = null)
    {
        var results = new List<PluginMetadata>();
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in pluginDirectories)
        {
            if (!Directory.Exists(dir))
            {
                logger?.LogWarning("Plugin directory does not exist: {Directory}", dir);
                continue;
            }

            foreach (var dllPath in Directory.GetFiles(dir, "*.dll"))
            {
                var fullPath = Path.GetFullPath(dllPath);
                if (!scannedPaths.Add(fullPath))
                {
                    logger?.LogDebug("Skipping already-scanned DLL: {Path}", fullPath);
                    continue;
                }

                var metadata = TryScanAssembly(fullPath, logger, pluginDirectories);
                if (metadata is not null)
                    results.Add(metadata);
            }
        }

        return results;
    }

    /// <summary>
    /// Attempts to scan a single assembly for a plugin type using
    /// <see cref="MetadataLoadContext"/>.
    /// </summary>
    private static PluginMetadata? TryScanAssembly(string assemblyPath, ILogger? logger,
        IReadOnlyList<string> pluginDirectories)
    {
        try
        {
            // Select all DLLs from the runtime dir as well as all plugin dirs
            var allAssemblies = pluginDirectories
                .Append(RuntimeEnvironment.GetRuntimeDirectory())
                .Append(AppContext.BaseDirectory)
                .SelectMany(dir => Directory.GetFiles(dir, "*.dll"))
                .Distinct();
            var resolver = new PathAssemblyResolver(allAssemblies);

            using var mlc = new MetadataLoadContext(resolver);
            var assembly = mlc.LoadFromAssemblyPath(assemblyPath);

            foreach (var type in assembly.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract)
                    continue;

                if (!IsPluginType(type))
                    continue;

                var name = ExtractPluginName(type);
                logger?.LogDebug(
                    "Found plugin '{Name}' in {Path}", name, assemblyPath);

                return new PluginMetadata(
                    name,
                    assemblyPath,
                    Path.GetFileName(assemblyPath));
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(
                ex, "Could not inspect {Path} for plugins (not a managed assembly?)",
                assemblyPath);
        }

        return null;
    }

    /// <summary>
    /// Checks whether a type implements <c>Marv.Core.Plugin.IPlugin</c> by name,
    /// since <see cref="MetadataLoadContext"/> types are not runtime types.
    /// </summary>
    private static bool IsPluginType(Type type)
    {
        // Check interfaces by full name
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.FullName == PluginInterfaceFullName)
                return true;
        }

        // Check base class chain by full name
        var baseType = type.BaseType;
        while (baseType is not null)
        {
            if (baseType.FullName == MarvPluginBaseFullName)
                return true;
            baseType = baseType.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Extracts the plugin name from a <c>[PluginName]</c> attribute if present,
    /// otherwise derives it from the class name by stripping the "Plugin" suffix.
    /// Uses <see cref="CustomAttributeData"/> since <see cref="MetadataLoadContext"/>
    /// types cannot use <see cref="MemberInfo.GetCustomAttribute{T}"/>.
    /// </summary>
    private static string ExtractPluginName(Type pluginType)
    {
        foreach (var attrData in pluginType.GetCustomAttributesData())
        {
            if (attrData.AttributeType.FullName != PluginNameAttributeFullName)
                continue;

            if (attrData.ConstructorArguments.Count > 0 &&
                attrData.ConstructorArguments[0].Value is string name)
                return name;
        }

        var typeName = pluginType.Name;
        return typeName.EndsWith(PluginSuffix, StringComparison.Ordinal)
            ? typeName[..^PluginSuffix.Length]
            : typeName;
    }

    /// <summary>
    /// Attempts to derive a plugin name from an assembly filename by stripping
    /// known prefixes and the .dll extension. For example,
    /// <c>Marv.Plugins.CannedResponses.dll</c> yields <c>CannedResponses</c>.
    /// </summary>
    public static string DeriveNameFromAssemblyFile(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);

        // Strip common namespace prefixes
        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }
}
