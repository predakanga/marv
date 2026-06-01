using System.Reflection;

namespace Marv.Core;

/// <summary>
/// Provides the current Marv version, read from the assembly metadata.
/// </summary>
public static class MarvVersion
{
    /// <summary>
    /// The informational version string (e.g. "0.1.0" or "0.1.0+abc123").
    /// Falls back to the assembly version if no informational version is set.
    /// </summary>
    public static string Current { get; } = GetVersion();

    private static string GetVersion()
    {
        var assembly = typeof(MarvVersion).Assembly;
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (infoVersion is not null)
        {
            // Strip the build metadata suffix (e.g. "+abc123def") that MSBuild appends from SourceRevisionId
            var plusIndex = infoVersion.IndexOf('+');
            return plusIndex >= 0 ? infoVersion[..plusIndex] : infoVersion;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
