using Marv.Core.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Marv.Core.Tests.Plugin;

/// <summary>
/// Tests for <see cref="PluginDiscovery.IsCoreService"/> with DI container probing
/// instead of static <c>CoreServiceTypes</c>.
/// </summary>
public class PluginDiscoveryTests
{
    private interface ICustomService;
    private interface IPluginService;

    [Fact]
    public void IsCoreService_CancellationToken_ReturnsTrue()
    {
        var services = new ServiceCollection();
        Assert.True(PluginDiscovery.IsCoreService(typeof(CancellationToken), services));
    }

    [Fact]
    public void IsCoreService_RegisteredService_ReturnsTrue()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICustomService, CustomServiceImpl>();

        Assert.True(PluginDiscovery.IsCoreService(typeof(ICustomService), services));
    }

    [Fact]
    public void IsCoreService_UnregisteredService_ReturnsFalse()
    {
        var services = new ServiceCollection();
        Assert.False(PluginDiscovery.IsCoreService(typeof(IPluginService), services));
    }

    [Fact]
    public void IsCoreService_IHttpClientFactory_WhenRegistered_ReturnsTrue()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();

        Assert.True(PluginDiscovery.IsCoreService(typeof(System.Net.Http.IHttpClientFactory), services));
    }

    [Fact]
    public void IsCoreService_OpenGeneric_IOptions_ReturnsTrue()
    {
        var services = new ServiceCollection();
        services.AddOptions();

        Assert.True(PluginDiscovery.IsCoreService(
            typeof(Microsoft.Extensions.Options.IOptions<CustomServiceImpl>), services));
    }

    [Fact]
    public void IsCoreService_OpenGeneric_ILogger_ReturnsTrue()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Assert.True(PluginDiscovery.IsCoreService(
            typeof(Microsoft.Extensions.Logging.ILogger<CustomServiceImpl>), services));
    }

    private class CustomServiceImpl : ICustomService;
}
