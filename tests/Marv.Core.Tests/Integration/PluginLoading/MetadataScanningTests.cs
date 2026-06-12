using Marv.Core.Plugin;
using Xunit;

namespace Marv.Core.Tests.Integration.PluginLoading;

/// <summary>
/// Integration tests for <see cref="PluginMetadataScanner"/> against published plugin DLLs.
/// </summary>
[Collection("PluginLoading")]
[Trait("Category", "Integration")]
public class MetadataScanningTests
{
    private readonly PublishedOutputFixture _fixture;

    public MetadataScanningTests(PublishedOutputFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void ScanDirectories_DiscoversAllExpectedPlugins()
    {
        var results = PluginMetadataScanner.ScanDirectories([_fixture.PluginDir]);

        var discoveredNames = results.Select(r => r.Name).OrderBy(n => n).ToList();

        Assert.Contains("Auth", discoveredNames);
        Assert.Contains("AuthConsumer", discoveredNames);
        Assert.Contains("CannedResponses", discoveredNames);
        Assert.Contains("Greet", discoveredNames);
        Assert.Contains("Moderation", discoveredNames);
    }

    [Fact]
    public void ScanDirectories_ExtractsCorrectPluginNames()
    {
        var results = PluginMetadataScanner.ScanDirectories([_fixture.PluginDir]);
        var byFile = results.ToDictionary(r => r.AssemblyFileName, r => r.Name);

        Assert.Equal("Auth", byFile["Marv.Plugins.Auth.dll"]);
        Assert.Equal("AuthConsumer", byFile["Marv.Plugins.AuthConsumer.dll"]);
        Assert.Equal("CannedResponses", byFile["Marv.Plugins.CannedResponses.dll"]);
        Assert.Equal("Greet", byFile["Marv.Plugins.Greet.dll"]);
        Assert.Equal("Moderation", byFile["Marv.Plugins.Moderation.dll"]);
    }

    [Fact]
    public void ScanDirectories_IgnoresNonPluginDlls()
    {
        // Drop a non-plugin DLL into the plugin directory
        var fakeDllPath = Path.Combine(_fixture.PluginDir, "NotAPlugin.dll");
        File.Copy(
            typeof(object).Assembly.Location,
            fakeDllPath,
            overwrite: true);
        try
        {
            var results = PluginMetadataScanner.ScanDirectories([_fixture.PluginDir]);

            Assert.DoesNotContain(results, r => r.AssemblyFileName == "NotAPlugin.dll");
            Assert.Equal(5, results.Count);
        }
        finally
        {
            File.Delete(fakeDllPath);
        }
    }

    [Fact]
    public void ScanDirectories_ReturnsCorrectAssemblyPaths()
    {
        var results = PluginMetadataScanner.ScanDirectories([_fixture.PluginDir]);

        foreach (var result in results)
        {
            Assert.True(File.Exists(result.AssemblyPath),
                $"AssemblyPath should point to an existing file: {result.AssemblyPath}");
            Assert.Equal(Path.GetFullPath(result.AssemblyPath), result.AssemblyPath);
        }
    }

    [Fact]
    public void ScanDirectories_NonExistentDirectory_ReturnsEmpty()
    {
        var results = PluginMetadataScanner.ScanDirectories(
            [Path.Combine(_fixture.HostDir, "no-such-dir")]);

        Assert.Empty(results);
    }
}
