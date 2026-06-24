using Marv.Core.Plugin;
using Xunit;

namespace Marv.Core.Tests.Plugin;

/// <summary>
/// Tests for plugin name resolution in <see cref="PluginManager.ResolveRequestedPlugins"/>
/// and <see cref="PluginMetadataScanner.DeriveNameFromAssemblyFile"/>.
/// </summary>
public class PluginNameResolutionTests
{
    private static PluginMetadata Meta(string name, string fileName) =>
        new(name, $"/plugins/{fileName}", fileName);

    [Fact]
    public void DeriveNameFromAssemblyFile_StripsPrefix()
    {
        Assert.Equal("CannedResponses",
            PluginMetadataScanner.DeriveNameFromAssemblyFile("Marv.Plugins.CannedResponses.dll"));
    }

    [Fact]
    public void DeriveNameFromAssemblyFile_NoDots_ReturnsWithoutExtension()
    {
        Assert.Equal("MyPlugin",
            PluginMetadataScanner.DeriveNameFromAssemblyFile("MyPlugin.dll"));
    }

    [Fact]
    public void ResolveRequestedPlugins_ExactMatch_Resolves()
    {
        var metadata = new[] { Meta("Auth", "Marv.Plugins.Auth.dll") };
        var result = PluginManager.ResolveRequestedPlugins(["Auth"], metadata);

        Assert.Single(result);
        Assert.Equal("/plugins/Marv.Plugins.Auth.dll", result[0]);
    }

    [Fact]
    public void ResolveRequestedPlugins_CaseInsensitiveMatch_Resolves()
    {
        var metadata = new[] { Meta("Auth", "Marv.Plugins.Auth.dll") };
        var result = PluginManager.ResolveRequestedPlugins(["auth"], metadata);

        Assert.Single(result);
    }

    [Fact]
    public void ResolveRequestedPlugins_AssemblyConventionMatch_Resolves()
    {
        var metadata = new[] { Meta("ExampleCommon", "Marv.Plugins.Common.dll") };
        // "Common" doesn't match plugin name "ExampleCommon", but matches the
        // assembly-derived name from "Marv.Plugins.Common.dll" → "Common"
        var result = PluginManager.ResolveRequestedPlugins(["Common"], metadata);

        Assert.Single(result);
    }

    [Fact]
    public void ResolveRequestedPlugins_NoMatch_ThrowsWithAvailablePlugins()
    {
        var metadata = new[]
        {
            Meta("Auth", "Marv.Plugins.Auth.dll"),
            Meta("Greet", "Marv.Plugins.Greet.dll")
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => PluginManager.ResolveRequestedPlugins(["Nonexistent"], metadata));

        Assert.Contains("Nonexistent", ex.Message);
        Assert.Contains("Auth", ex.Message);
        Assert.Contains("Greet", ex.Message);
    }

    [Fact]
    public void ResolveRequestedPlugins_CloseMatch_SuggestsCorrection()
    {
        var metadata = new[] { Meta("ExampleCommon", "Marv.Plugins.Common.dll") };

        var ex = Assert.Throws<InvalidOperationException>(
            () => PluginManager.ResolveRequestedPlugins(["ExampleComon"], metadata));

        Assert.Contains("Did you mean", ex.Message);
        Assert.Contains("ExampleCommon", ex.Message);
    }

    [Fact]
    public void ResolveRequestedPlugins_DuplicateRequest_DeduplicatesPath()
    {
        var metadata = new[] { Meta("Auth", "Marv.Plugins.Auth.dll") };
        var result = PluginManager.ResolveRequestedPlugins(["Auth", "Auth"], metadata);

        // Second "Auth" is a duplicate — same path, should be skipped
        Assert.Single(result);
    }

    [Fact]
    public void ResolveRequestedPlugins_EmptyRequest_ReturnsEmpty()
    {
        var metadata = new[] { Meta("Auth", "Marv.Plugins.Auth.dll") };
        var result = PluginManager.ResolveRequestedPlugins([], metadata);

        Assert.Empty(result);
    }

    [Fact]
    public void DeduplicateDirectories_RemovesDuplicates()
    {
        var dirs = new[] { "plugins", "./plugins", "other" };
        var result = PluginManager.DeduplicateDirectories(dirs);

        // "plugins" and "./plugins" resolve to the same absolute path
        Assert.Equal(2, result.Count);
    }
}
