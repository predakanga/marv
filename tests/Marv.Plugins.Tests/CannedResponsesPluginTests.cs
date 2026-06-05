using Xunit;
using NSubstitute;
using Marv.Core.Events;
using Marv.Core.Platform;
using Marv.Core.Plugin;
using Marv.Core.Protocol;
using Marv.Plugins.CannedResponses;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marv.Plugins.Tests;

/// <summary>
/// Tests for the <see cref="CannedResponsesPlugin"/> and its handler groups.
/// Validates that HandlerGroups are discovered and dispatched correctly.
/// </summary>
public class CannedResponsesPluginTests
{
    private static readonly IrcMessage DummyMessage = new("PRIVMSG", ["#test", "!ping"]);

    private static (CannedResponsesPlugin Plugin, IBot Bot) CreatePlugin()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns("!");

        // The activator must be able to create handler group instances
        var activator = Substitute.For<IPluginActivator>();

        // Set up the activator to create real handler group instances
        activator.CreateInstance<InfoHandlers>(Arg.Any<object[]>())
            .Returns(ci => new InfoHandlers(bot));
        activator.CreateInstance<FunHandlers>(Arg.Any<object[]>())
            .Returns(ci => new FunHandlers(bot));

        return (new CannedResponsesPlugin(bot, activator, NullLoggerFactory.Instance), bot);
    }

    [Fact]
    public async Task PingCommand_FromHandlerGroup_Responds()
    {
        var (plugin, bot) = CreatePlugin();
        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("tester");

        var evt = new MessageEvent
        {
            Channel = channel,
            Sender = user,
            Text = "!ping",
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        await plugin.HandleEventAsync(evt, CancellationToken.None);
        await bot.Received(1).SendMessageAsync("#test", "pong", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VersionCommand_FromHandlerGroup_Responds()
    {
        var (plugin, bot) = CreatePlugin();
        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("tester");

        var evt = new MessageEvent
        {
            Channel = channel,
            Sender = user,
            Text = "!version",
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        await plugin.HandleEventAsync(evt, CancellationToken.None);
        await bot.Received(1).SendMessageAsync("#test", "Marv IRC Bot v0.1.0", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HelpCommand_FromHandlerGroup_Responds()
    {
        var (plugin, bot) = CreatePlugin();
        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("tester");

        var evt = new MessageEvent
        {
            Channel = channel,
            Sender = user,
            Text = "!help",
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        await plugin.HandleEventAsync(evt, CancellationToken.None);
        await bot.Received(1).SendMessageAsync("#test",
            "Available commands: !help, !version, !source, !ping",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GoodBot_RegexMatch_Responds()
    {
        var (plugin, bot) = CreatePlugin();
        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("tester");

        var evt = new MessageEvent
        {
            Channel = channel,
            Sender = user,
            Text = "you are a good bot",
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        await plugin.HandleEventAsync(evt, CancellationToken.None);
        await bot.Received(1).SendMessageAsync("#test", "Thank you! 😊", Arg.Any<CancellationToken>());
    }
}
