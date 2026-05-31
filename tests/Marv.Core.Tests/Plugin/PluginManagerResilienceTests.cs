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
/// Tests that PluginManager handles plugin failures gracefully, skipping
/// dependent plugins when a dependency fails.
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
    public void InstantiatePlugins_IndependentPluginFailure_DoesNotAffectOthers()
    {
        var (manager, _) = CreateManager();

        var goodDesc = MakeDescriptor("Good", typeof(GoodPlugin));
        var failDesc = MakeDescriptor("Failing", typeof(FailingPlugin));
        var good2Desc = MakeDescriptor("Good2", typeof(GoodPlugin));

        // Failing plugin is in the middle — Good and Good2 should still load
        manager.InstantiatePlugins([goodDesc, failDesc, good2Desc]);

        var instances = GetInstances(manager);
        Assert.Equal(2, instances.Count);
        Assert.Equal("Good", instances[0].Descriptor.Name);
        Assert.Equal("Good2", instances[1].Descriptor.Name);
    }

    [Fact]
    public void InstantiatePlugins_FailedPlugin_SkipsDependents()
    {
        var (manager, _) = CreateManager();

        var providerDesc = MakeDescriptor("Provider", typeof(FailingPlugin),
            providedServices: [typeof(ITestService)]);
        var consumerDesc = MakeDescriptor("Consumer", typeof(GoodPlugin),
            requiredServices: [typeof(ITestService)]);
        var independentDesc = MakeDescriptor("Independent", typeof(GoodPlugin));

        manager.InstantiatePlugins([providerDesc, consumerDesc, independentDesc]);

        var instances = GetInstances(manager);
        // Provider fails, Consumer should be skipped, Independent should succeed
        Assert.Single(instances);
        Assert.Equal("Independent", instances[0].Descriptor.Name);
    }

    [Fact]
    public void InstantiatePlugins_FailedPlugin_SkipsExplicitDependents()
    {
        var (manager, _) = CreateManager();

        var depDesc = MakeDescriptor("Dependency", typeof(FailingPlugin));
        var dependentDesc = MakeDescriptor("Dependent", typeof(GoodPlugin),
            explicitDeps: [typeof(FailingPlugin)]);
        var independentDesc = MakeDescriptor("Independent", typeof(GoodPlugin));

        manager.InstantiatePlugins([depDesc, dependentDesc, independentDesc]);

        var instances = GetInstances(manager);
        Assert.Single(instances);
        Assert.Equal("Independent", instances[0].Descriptor.Name);
    }

    [Fact]
    public async Task LoadPlugins_FailedLoad_SkipsDependents()
    {
        var (manager, _) = CreateManager();

        var providerDesc = MakeDescriptor("Provider", typeof(LoadFailPlugin),
            providedServices: [typeof(ITestService)]);
        var consumerDesc = MakeDescriptor("Consumer", typeof(GoodPlugin),
            requiredServices: [typeof(ITestService)]);
        var independentDesc = MakeDescriptor("Independent", typeof(GoodPlugin));

        manager.InstantiatePlugins([providerDesc, consumerDesc, independentDesc]);
        await manager.LoadPluginsAsync(CancellationToken.None);

        var instances = GetInstances(manager);
        // Provider fails OnLoad, Consumer should be skipped, Independent stays
        Assert.Single(instances);
        Assert.Equal("Independent", instances[0].Descriptor.Name);
    }

    [Fact]
    public async Task LoadPlugins_FailedLoad_TransitiveDependentsSkipped()
    {
        var (manager, _) = CreateManager();

        // A -> B -> C chain, A fails
        var aDesc = MakeDescriptor("A", typeof(LoadFailPlugin),
            providedServices: [typeof(ITestService)]);
        var bDesc = MakeDescriptor("B", typeof(GoodPlugin),
            requiredServices: [typeof(ITestService)],
            providedServices: [typeof(IDisposable)]);
        var cDesc = MakeDescriptor("C", typeof(GoodPlugin),
            requiredServices: [typeof(IDisposable)]);
        var dDesc = MakeDescriptor("D", typeof(GoodPlugin));

        manager.InstantiatePlugins([aDesc, bDesc, cDesc, dDesc]);
        await manager.LoadPluginsAsync(CancellationToken.None);

        var instances = GetInstances(manager);
        // A fails OnLoad, B depends on A (skipped), C depends on B (skipped), D independent (stays)
        Assert.Single(instances);
        Assert.Equal("D", instances[0].Descriptor.Name);
    }

    [Fact]
    public void InstantiatePlugins_AllFail_EmptyInstances()
    {
        var (manager, _) = CreateManager();

        var desc = MakeDescriptor("Bad", typeof(FailingPlugin));
        manager.InstantiatePlugins([desc]);

        var instances = GetInstances(manager);
        Assert.Empty(instances);
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

    private static List<PluginInstance> GetInstances(PluginManager manager)
    {
        return (List<PluginInstance>)typeof(PluginManager)
            .GetField("_instances", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(manager)!;
    }
}
