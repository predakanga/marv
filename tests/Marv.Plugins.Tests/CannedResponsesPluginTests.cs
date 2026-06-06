using Xunit;
using NSubstitute;
using Marv.Core;
using Marv.Core.Events;
using Marv.Plugins.CannedResponses;
using Marv.Testing;

namespace Marv.Plugins.Tests;

/// <summary>
/// Tests for the <see cref="CannedResponsesPlugin"/> and its handler groups.
/// Validates that HandlerGroups are discovered and dispatched correctly.
/// </summary>
public class CannedResponsesPluginTests
{
    private static PluginTestHarness<CannedResponsesPlugin> CreateHarness() =>
        PluginTestHarness<CannedResponsesPlugin>.Create();

    [Fact]
    public async Task PingCommand_FromHandlerGroup_Responds()
    {
        var harness = CreateHarness();
        var evt = EventBuilder<MessageEvent>.Create(raw => new MessageEvent
        {
            Channel = MockChannel.Create("#test"),
            Sender = MockUser.Create("tester"),
            Text = "!ping",
            RawMessage = raw
        }).Build();

        await harness.HandleEventAsync(evt);
        await harness.Bot.Received(1).SendMessageAsync("#test", "pong", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VersionCommand_FromHandlerGroup_Responds()
    {
        var harness = CreateHarness();
        var evt = EventBuilder<MessageEvent>.Create(raw => new MessageEvent
        {
            Channel = MockChannel.Create("#test"),
            Sender = MockUser.Create("tester"),
            Text = "!version",
            RawMessage = raw
        }).Build();

        await harness.HandleEventAsync(evt);
        await harness.Bot.Received(1).SendMessageAsync("#test", $"Marv IRC Bot v{MarvVersion.Current}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HelpCommand_FromHandlerGroup_Responds()
    {
        var harness = CreateHarness();
        var evt = EventBuilder<MessageEvent>.Create(raw => new MessageEvent
        {
            Channel = MockChannel.Create("#test"),
            Sender = MockUser.Create("tester"),
            Text = "!help",
            RawMessage = raw
        }).Build();

        await harness.HandleEventAsync(evt);
        await harness.Bot.Received(1).SendMessageAsync("#test",
            "Available commands: !help, !version, !source, !ping",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GoodBot_RegexMatch_Responds()
    {
        var harness = CreateHarness();
        var evt = EventBuilder<MessageEvent>.Create(raw => new MessageEvent
        {
            Channel = MockChannel.Create("#test"),
            Sender = MockUser.Create("tester"),
            Text = "you are a good bot",
            RawMessage = raw
        }).Build();

        await harness.HandleEventAsync(evt);
        await harness.Bot.Received(1).SendMessageAsync("#test", "Thank you! 😊", Arg.Any<CancellationToken>());
    }
}
