using Marv.Core.Events;
using Marv.Core.Platform;
using Marv.Core.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Marv.Testing.Tests;

public class PluginTestHarnessTests
{
    [Fact]
    public void Create_InstantiatesPlugin()
    {
        var harness = PluginTestHarness<SimpleTestPlugin>.Create();

        Assert.NotNull(harness.Plugin);
        Assert.NotNull(harness.Bot);
        Assert.NotNull(harness.Services);
    }

    [Fact]
    public void Create_PluginHasWorkingBot()
    {
        var harness = PluginTestHarness<SimpleTestPlugin>.Create();

        Assert.Equal("Marv", harness.Bot.Self.Nick);
        Assert.Equal("!", harness.Bot.CommandPrefix);
    }

    [Fact]
    public void Create_WithCustomBot_UsesProvidedBot()
    {
        var bot = MockBot.Create("TestBot", ".");

        var harness = PluginTestHarness<SimpleTestPlugin>.Create(bot: bot);

        Assert.Same(bot, harness.Bot);
        Assert.Equal("TestBot", harness.Bot.Self.Nick);
    }

    [Fact]
    public async Task LoadAsync_CallsOnLoad()
    {
        var harness = PluginTestHarness<SimpleTestPlugin>.Create();

        await harness.LoadAsync();

        Assert.True(harness.Plugin.WasLoaded);
    }

    [Fact]
    public async Task ConnectedAsync_CallsOnConnected()
    {
        var harness = PluginTestHarness<SimpleTestPlugin>.Create();

        await harness.ConnectedAsync();

        Assert.True(harness.Plugin.WasConnected);
    }

    [Fact]
    public async Task HandleEventAsync_DispatchesEvents()
    {
        var harness = PluginTestHarness<SimpleTestPlugin>.Create();
        var evt = EventBuilder<ConnectedEvent>.Create(raw => new ConnectedEvent
        {
            RawMessage = raw
        }).Build();

        await harness.HandleEventAsync(evt);

        Assert.True(harness.Plugin.ReceivedConnectedEvent);
    }

    [Fact]
    public void Create_WithCustomServices_RegistersServices()
    {
        var harness = PluginTestHarness<PluginWithDependency>.Create(services =>
        {
            services.AddSingleton<ITestService, TestService>();
        });

        Assert.NotNull(harness.Plugin);
        Assert.NotNull(harness.Plugin.TestService);
    }
}

#region Test fixtures

/// <summary>Minimal plugin for testing the harness.</summary>
public class SimpleTestPlugin(IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory)
    : MarvPlugin(bot, activator, loggerFactory)
{
    public bool WasLoaded { get; private set; }
    public bool WasConnected { get; private set; }
    public bool ReceivedConnectedEvent { get; private set; }

    public override Task OnLoadAsync(CancellationToken ct)
    {
        WasLoaded = true;
        return Task.CompletedTask;
    }

    public override Task OnConnectedAsync(CancellationToken ct)
    {
        WasConnected = true;
        return Task.CompletedTask;
    }

    [OnEvent]
    private Task HandleConnected(ConnectedEvent e, CancellationToken ct)
    {
        ReceivedConnectedEvent = true;
        return Task.CompletedTask;
    }
}

public interface ITestService
{
    string Value { get; }
}

public class TestService : ITestService
{
    public string Value => "test";
}

/// <summary>Plugin that requires an additional DI service.</summary>
public class PluginWithDependency(
    IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory,
    ITestService testService)
    : MarvPlugin(bot, activator, loggerFactory)
{
    public ITestService TestService { get; } = testService;
}

#endregion
