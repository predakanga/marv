using Xunit;
using NSubstitute;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Marv.Core.Events;
using Marv.Core.Platform;
using Marv.Core.Plugin;
using Marv.Core.Protocol;
using Marv.Plugins.Greet;

namespace Marv.Plugins.Tests;

/// <summary>
/// Tests for the <see cref="GreetPlugin"/> reference plugin.
/// </summary>
public class GreetPluginTests
{
    private static readonly IrcMessage DummyMessage = new("PRIVMSG", ["#test", "hello"]);

    private static (GreetPlugin Plugin, IBot Bot) CreatePlugin(GreetPluginConfig? config = null)
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns("!");
        var selfUser = Substitute.For<IUser>();
        selfUser.Nick.Returns("Marv");
        bot.Self.Returns(selfUser);

        var activator = Substitute.For<IPluginActivator>();
        var options = Options.Create(config ?? new GreetPluginConfig());

        var plugin = new GreetPlugin(bot, activator, NullLoggerFactory.Instance, options);
        return (plugin, bot);
    }

    [Fact]
    public async Task HandleJoin_SendsGreeting()
    {
        var (plugin, bot) = CreatePlugin();
        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("testuser");

        var evt = new UserJoinedEvent
        {
            Channel = channel,
            User = user,
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        await plugin.HandleEventAsync(evt, CancellationToken.None);
        await bot.Received(1).SendMessageAsync("#test", "Welcome, testuser!", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleJoin_DoesNotGreetSelf()
    {
        var (plugin, bot) = CreatePlugin();
        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("Marv");

        var evt = new UserJoinedEvent
        {
            Channel = channel,
            User = user,
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        await plugin.HandleEventAsync(evt, CancellationToken.None);
        await bot.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleJoin_DisabledByConfig()
    {
        var config = new GreetPluginConfig { GreetOnJoin = false };
        var (plugin, bot) = CreatePlugin(config);
        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("testuser");

        var evt = new UserJoinedEvent
        {
            Channel = channel,
            User = user,
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        await plugin.HandleEventAsync(evt, CancellationToken.None);
        await bot.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleJoin_CustomMessage()
    {
        var config = new GreetPluginConfig { GreetMessage = "Hi {nick}, welcome aboard!" };
        var (plugin, bot) = CreatePlugin(config);
        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("alice");

        var evt = new UserJoinedEvent
        {
            Channel = channel,
            User = user,
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        await plugin.HandleEventAsync(evt, CancellationToken.None);
        await bot.Received(1).SendMessageAsync("#test", "Hi alice, welcome aboard!", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleHello_RepliesWithGreeting()
    {
        var (plugin, bot) = CreatePlugin();
        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("bob");

        var evt = new MessageEvent
        {
            Channel = channel,
            Sender = user,
            Text = "!hello",
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        await plugin.HandleEventAsync(evt, CancellationToken.None);
        await bot.Received(1).SendMessageAsync("#test", "Hello, bob!", Arg.Any<CancellationToken>());
    }
}
