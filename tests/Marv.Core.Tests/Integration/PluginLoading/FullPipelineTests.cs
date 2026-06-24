using Marv.Core.Plugin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Marv.Core.Tests.Integration.PluginLoading;

/// <summary>
/// Integration tests that exercise the complete plugin loading pipeline:
/// metadata scanning → name resolution → assembly loading → service registration → instantiation.
/// </summary>
[Collection("PluginLoading")]
[Trait("Category", "Integration")]
public class FullPipelineTests
{
    private readonly PublishedOutputFixture _fixture;

    public FullPipelineTests(PublishedOutputFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void ResolveRequestedPlugins_ResolvesAllByName()
    {
        var metadata = PluginMetadataScanner.ScanDirectories([_fixture.PluginDir]);
        var requested = new[] { "Greet", "CannedResponses", "Auth", "AuthConsumer", "Moderation" };

        var resolved = PluginManager.ResolveRequestedPlugins(requested, metadata);

        Assert.Equal(5, resolved.Count);
        Assert.All(resolved, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void DiscoverAndRegister_LoadsAllPlugins()
    {
        var metadata = PluginMetadataScanner.ScanDirectories([_fixture.PluginDir]);
        var requested = new[] { "Greet", "CannedResponses", "Auth", "AuthConsumer", "Moderation" };
        var resolvedPaths = PluginManager.ResolveRequestedPlugins(requested, metadata);

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Accounts:0:Hostmask"] = "*!admin@*",
                ["Auth:Accounts:0:Roles:0"] = "admin",
            })
            .Build();

        var descriptors = PluginManager.DiscoverAndRegister(
            services, configuration, resolvedPaths, NullLogger.Instance);

        var descriptorNames = descriptors.Select(d => d.Name).ToList();
        Assert.Contains("Greet", descriptorNames);
        Assert.Contains("CannedResponses", descriptorNames);
        Assert.Contains("Auth", descriptorNames);
        Assert.Contains("AuthConsumer", descriptorNames);
        Assert.Contains("Moderation", descriptorNames);
    }

    [Fact]
    public void DiscoverAndRegister_SortsDependenciesCorrectly()
    {
        var metadata = PluginMetadataScanner.ScanDirectories([_fixture.PluginDir]);
        // Auth must be listed before AuthConsumer in resolved paths because
        // DiscoverAndRegister loads assemblies in order and AuthConsumer
        // references Auth at the assembly level.
        var requested = new[] { "Auth", "AuthConsumer" };
        var resolvedPaths = PluginManager.ResolveRequestedPlugins(requested, metadata);

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Accounts:0:Hostmask"] = "*!admin@*",
                ["Auth:Accounts:0:Roles:0"] = "admin",
            })
            .Build();

        var descriptors = PluginManager.DiscoverAndRegister(
            services, configuration, resolvedPaths, NullLogger.Instance);

        var authIndex = descriptors.ToList().FindIndex(d => d.Name == "Auth");
        var consumerIndex = descriptors.ToList().FindIndex(d => d.Name == "AuthConsumer");
        Assert.True(authIndex < consumerIndex,
            "Auth must appear before AuthConsumer in sorted output due to dependency ordering.");
    }

    [Fact]
    public void DiscoverAndRegister_RegistersPluginConfigurations()
    {
        var metadata = PluginMetadataScanner.ScanDirectories([_fixture.PluginDir]);
        var requested = new[] { "Greet" };
        var resolvedPaths = PluginManager.ResolveRequestedPlugins(requested, metadata);

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Greet:GreetMessage"] = "Hello, {nick}!",
            })
            .Build();

        var descriptors = PluginManager.DiscoverAndRegister(
            services, configuration, resolvedPaths, NullLogger.Instance);

        var greetDescriptor = descriptors.Single(d => d.Name == "Greet");
        Assert.NotEmpty(greetDescriptor.Configurations);
    }

    [Fact]
    public void MissingPluginName_ThrowsWithClearMessage()
    {
        var metadata = PluginMetadataScanner.ScanDirectories([_fixture.PluginDir]);
        var requested = new[] { "NonExistentPlugin" };

        var ex = Assert.Throws<InvalidOperationException>(
            () => PluginManager.ResolveRequestedPlugins(requested, metadata));

        Assert.Contains("NonExistentPlugin", ex.Message);
        Assert.Contains("was requested", ex.Message);
    }

    [Fact]
    public void MissingAssemblyFile_ThrowsWithClearMessage()
    {
        var fakePath = Path.Combine(_fixture.PluginDir, "DoesNotExist.dll");

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(
            () => PluginManager.DiscoverAndRegister(
                services, configuration, [fakePath], NullLogger.Instance));

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void NonPluginAssembly_ThrowsWhenPassedDirectly()
    {
        // Marv.Core.dll is a valid managed assembly but contains no IPlugin implementation
        var nonPluginDll = typeof(MarvConfiguration).Assembly.Location;

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(
            () => PluginManager.DiscoverAndRegister(
                services, configuration, [nonPluginDll], NullLogger.Instance));

        Assert.Contains("no IPlugin implementation", ex.Message);
    }

    [Fact]
    public void WildcardStar_LoadsAllPlugins()
    {
        var metadata = PluginMetadataScanner.ScanDirectories([_fixture.PluginDir]);
        var expanded = PluginManager.ExpandPluginPatterns(["*"], metadata);
        var resolved = PluginManager.ResolveRequestedPlugins(expanded, metadata);

        Assert.True(resolved.Count >= 5, "Expected at least 5 plugins from wildcard");
        Assert.All(resolved, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void WildcardWithNegation_ExcludesPlugin()
    {
        var metadata = PluginMetadataScanner.ScanDirectories([_fixture.PluginDir]);
        var expanded = PluginManager.ExpandPluginPatterns(["*", "!Greet"], metadata);

        Assert.DoesNotContain("Greet", expanded);
        Assert.Contains("Auth", expanded);
        Assert.Contains("CannedResponses", expanded);
    }
}
