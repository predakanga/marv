using System.Reflection;
using Marv.Core.Events;
using Marv.Core.Platform;
using Marv.Core.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Marv.Core.Tests.Plugin;

/// <summary>
/// Tests that PluginManager treats all plugin loading failures as fatal,
/// throwing <see cref="InvalidOperationException"/> to prevent the bot
/// from starting in a degraded state.
/// </summary>
public class PluginManagerResilienceTests
{
    // Marker interfaces for service dependency testing
    private interface ITestService;

    // A plugin that always instantiates successfully
    private class GoodPlugin : IPlugin
    {
        public bool Loaded { get; private set; }
        public Task OnLoadAsync(CancellationToken ct) { Loaded = true; return Task.CompletedTask; }
        public Task OnConnectedAsync(CancellationToken ct) => Task.CompletedTask;
        public Task OnDisconnectedAsync() => Task.CompletedTask;
        public Task OnUnloadAsync() => Task.CompletedTask;
        public Task HandleEventAsync(MarvEvent evt, CancellationToken ct) => Task.CompletedTask;
    }

    // A plugin that throws during instantiation (no parameterless constructor)
    private class FailingPlugin : IPlugin
    {
        public FailingPlugin(string requiredArg) => throw new InvalidOperationException("Boom");
        public Task OnLoadAsync(CancellationToken ct) => Task.CompletedTask;
        public Task OnConnectedAsync(CancellationToken ct) => Task.CompletedTask;
        public Task OnDisconnectedAsync() => Task.CompletedTask;
        public Task OnUnloadAsync() => Task.CompletedTask;
        public Task HandleEventAsync(MarvEvent evt, CancellationToken ct) => Task.CompletedTask;
    }

    // A plugin that fails during OnLoadAsync
    private class LoadFailPlugin : IPlugin
    {
        public Task OnLoadAsync(CancellationToken ct) => throw new InvalidOperationException("Load failed");
        public Task OnConnectedAsync(CancellationToken ct) => Task.CompletedTask;
        public Task OnDisconnectedAsync() => Task.CompletedTask;
        public Task OnUnloadAsync() => Task.CompletedTask;
        public Task HandleEventAsync(MarvEvent evt, CancellationToken ct) => Task.CompletedTask;
    }

    private static PluginDescriptor MakeDescriptor(
        string name,
        Type pluginType,
        IReadOnlyList<Type>? providedServices = null,
        IReadOnlyList<Type>? explicitDeps = null,
        IReadOnlyList<Type>? requiredServices = null,
        IReadOnlyList<Type>? optionalServices = null)
    {
        return new PluginDescriptor
        {
            Name = name,
            PluginType = pluginType,
            ProvidedServices = providedServices ?? [],
            ExplicitDependencies = explicitDeps ?? [],
            RequiredServices = requiredServices ?? [],
            OptionalServices = optionalServices ?? [],
            Configurations = [],
            Assembly = Assembly.GetExecutingAssembly()
        };
    }

    private static (PluginManager Manager, IServiceProvider Provider) CreateManager()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPluginActivator, PluginActivator>();
        services.AddSingleton(Substitute.For<IBot>());
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        var sp = services.BuildServiceProvider();

        var logger = NullLoggerFactory.Instance.CreateLogger<PluginManager>();
        return (new PluginManager(logger, sp), sp);
    }

    [Fact]
    public void InstantiatePlugins_FailingPlugin_ThrowsFatal()
    {
        var (manager, _) = CreateManager();

        var goodDesc = MakeDescriptor("Good", typeof(GoodPlugin));
        var failDesc = MakeDescriptor("Failing", typeof(FailingPlugin));

        var ex = Assert.Throws<InvalidOperationException>(
            () => manager.InstantiatePlugins([goodDesc, failDesc]));
        Assert.Contains("Failing", ex.Message);
        Assert.Contains("Failed to instantiate plugin", ex.Message);
    }

    [Fact]
    public void InstantiatePlugins_AllFail_ThrowsFatal()
    {
        var (manager, _) = CreateManager();

        var desc = MakeDescriptor("Bad", typeof(FailingPlugin));

        var ex = Assert.Throws<InvalidOperationException>(
            () => manager.InstantiatePlugins([desc]));
        Assert.Contains("Bad", ex.Message);
    }

    [Fact]
    public async Task LoadPlugins_FailedLoad_ThrowsFatal()
    {
        var (manager, _) = CreateManager();

        var desc = MakeDescriptor("FailLoad", typeof(LoadFailPlugin));
        var goodDesc = MakeDescriptor("Good", typeof(GoodPlugin));

        // LoadFailPlugin has no constructor issues, so instantiation succeeds
        manager.InstantiatePlugins([desc, goodDesc]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.LoadPluginsAsync(CancellationToken.None));
        Assert.Contains("FailLoad", ex.Message);
        Assert.Contains("OnLoadAsync", ex.Message);
    }

    [Fact]
    public async Task LoadPlugins_AllSucceed_AllPresent()
    {
        var (manager, _) = CreateManager();

        var a = MakeDescriptor("A", typeof(GoodPlugin));
        var b = MakeDescriptor("B", typeof(GoodPlugin));

        manager.InstantiatePlugins([a, b]);
        await manager.LoadPluginsAsync(CancellationToken.None);

        var instances = GetInstances(manager);
        Assert.Equal(2, instances.Count);
    }

    [Fact]
    public void InstantiatePlugins_AllGood_AllPresent()
    {
        var (manager, _) = CreateManager();

        var a = MakeDescriptor("A", typeof(GoodPlugin));
        var b = MakeDescriptor("B", typeof(GoodPlugin));
        var c = MakeDescriptor("C", typeof(GoodPlugin));

        manager.InstantiatePlugins([a, b, c]);

        var instances = GetInstances(manager);
        Assert.Equal(3, instances.Count);
        Assert.Equal("A", instances[0].Descriptor.Name);
        Assert.Equal("B", instances[1].Descriptor.Name);
        Assert.Equal("C", instances[2].Descriptor.Name);
    }

    private static List<PluginInstance> GetInstances(PluginManager manager)
    {
        return (List<PluginInstance>)typeof(PluginManager)
            .GetField("_instances", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(manager)!;
    }
}
