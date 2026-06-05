using Xunit;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Marv.Core.Events;
using Marv.Plugins.Greet;
using Marv.Testing;

namespace Marv.Plugins.Tests;

/// <summary>
/// Tests for the <see cref="GreetPlugin"/> reference plugin.
/// </summary>
public class GreetPluginTests
{
    private static PluginTestHarness<GreetPlugin> CreateHarness(GreetPluginConfig? config = null) =>
        PluginTestHarness<GreetPlugin>.Create(services =>
        {
            services.AddSingleton(Options.Create(config ?? new GreetPluginConfig()));
        });

    [Fact]
    public async Task HandleJoin_SendsGreeting()
    {
        var harness = CreateHarness();
        var evt = EventBuilder<UserJoinedEvent>.Create(raw => new UserJoinedEvent
        {
            Channel = MockChannel.Create("#test"),
            User = MockUser.Create("testuser"),
            RawMessage = raw
        }).Build();

        await harness.HandleEventAsync(evt);
        await harness.Bot.Received(1).SendMessageAsync("#test", "Welcome, testuser!", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleJoin_DoesNotGreetSelf()
    {
        var harness = CreateHarness();
        var evt = EventBuilder<UserJoinedEvent>.Create(raw => new UserJoinedEvent
        {
            Channel = MockChannel.Create("#test"),
            User = MockUser.Create("Marv"),
            RawMessage = raw
        }).Build();

        await harness.HandleEventAsync(evt);
        await harness.Bot.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleJoin_DisabledByConfig()
    {
        var harness = CreateHarness(new GreetPluginConfig { GreetOnJoin = false });
        var evt = EventBuilder<UserJoinedEvent>.Create(raw => new UserJoinedEvent
        {
            Channel = MockChannel.Create("#test"),
            User = MockUser.Create("testuser"),
            RawMessage = raw
        }).Build();

        await harness.HandleEventAsync(evt);
        await harness.Bot.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleJoin_CustomMessage()
    {
        var harness = CreateHarness(new GreetPluginConfig { GreetMessage = "Hi {nick}, welcome aboard!" });
        var evt = EventBuilder<UserJoinedEvent>.Create(raw => new UserJoinedEvent
        {
            Channel = MockChannel.Create("#test"),
            User = MockUser.Create("alice"),
            RawMessage = raw
        }).Build();

        await harness.HandleEventAsync(evt);
        await harness.Bot.Received(1).SendMessageAsync("#test", "Hi alice, welcome aboard!", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleHello_RepliesWithGreeting()
    {
        var harness = CreateHarness();
        var evt = EventBuilder<MessageEvent>.Create(raw => new MessageEvent
        {
            Channel = MockChannel.Create("#test"),
            Sender = MockUser.Create("bob"),
            Text = "!hello",
            RawMessage = raw
        }).Build();

        await harness.HandleEventAsync(evt);
        await harness.Bot.Received(1).SendMessageAsync("#test", "Hello, bob!", Arg.Any<CancellationToken>());
    }
}
