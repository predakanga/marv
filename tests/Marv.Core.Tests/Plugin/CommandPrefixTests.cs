using Marv.Core.Events;
using Marv.Core.Platform;
using Marv.Core.Plugin;
using Marv.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Marv.Core.Tests.Plugin;

/// <summary>
/// Tests for configurable command prefix and per-handler prefix override.
/// </summary>
public class CommandPrefixTests
{
    private static readonly IrcMessage DummyMessage = new("PRIVMSG", ["#test", "hello"]);

    private static MessageEvent CreateMessage(string text)
    {
        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("tester");

        return new MessageEvent
        {
            Channel = channel,
            Sender = user,
            Text = text,
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };
    }

    #region Default prefix

    private sealed class DefaultPrefixPlugin : MarvPlugin
    {
        public bool Called;
        public string? ReceivedCommand;
        public string? ReceivedArgString;

        public DefaultPrefixPlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnCommand("hello")]
        public Task Handle(CommandContext ctx, CancellationToken ct)
        {
            Called = true;
            ReceivedCommand = ctx.Command;
            ReceivedArgString = ctx.ArgString;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task DefaultPrefix_MatchesExclamation()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns("!");
        var plugin = new DefaultPrefixPlugin(bot, Substitute.For<IPluginActivator>());

        await plugin.HandleEventAsync(CreateMessage("!hello world"), CancellationToken.None);

        Assert.True(plugin.Called);
        Assert.Equal("hello", plugin.ReceivedCommand);
        Assert.Equal("world", plugin.ReceivedArgString);
    }

    [Fact]
    public async Task DefaultPrefix_DoesNotMatchWrongPrefix()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns("!");
        var plugin = new DefaultPrefixPlugin(bot, Substitute.For<IPluginActivator>());

        await plugin.HandleEventAsync(CreateMessage(".hello world"), CancellationToken.None);

        Assert.False(plugin.Called);
    }

    #endregion

    #region Custom bot-wide prefix

    [Fact]
    public async Task CustomPrefix_MatchesDotPrefix()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns(".");
        var plugin = new DefaultPrefixPlugin(bot, Substitute.For<IPluginActivator>());

        await plugin.HandleEventAsync(CreateMessage(".hello world"), CancellationToken.None);

        Assert.True(plugin.Called);
        Assert.Equal("hello", plugin.ReceivedCommand);
    }

    [Fact]
    public async Task CustomPrefix_DoesNotMatchDefaultPrefix()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns(".");
        var plugin = new DefaultPrefixPlugin(bot, Substitute.For<IPluginActivator>());

        await plugin.HandleEventAsync(CreateMessage("!hello world"), CancellationToken.None);

        Assert.False(plugin.Called);
    }

    #endregion

    #region Multi-character prefix

    [Fact]
    public async Task MultiCharPrefix_MatchesCorrectly()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns("marv:");
        var plugin = new DefaultPrefixPlugin(bot, Substitute.For<IPluginActivator>());

        await plugin.HandleEventAsync(CreateMessage("marv:hello world"), CancellationToken.None);

        Assert.True(plugin.Called);
        Assert.Equal("hello", plugin.ReceivedCommand);
        Assert.Equal("world", plugin.ReceivedArgString);
    }

    [Fact]
    public async Task MultiCharPrefix_PartialMatchDoesNotFire()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns("marv:");
        var plugin = new DefaultPrefixPlugin(bot, Substitute.For<IPluginActivator>());

        await plugin.HandleEventAsync(CreateMessage("marv hello"), CancellationToken.None);

        Assert.False(plugin.Called);
    }

    #endregion

    #region Case sensitivity

    [Fact]
    public async Task Prefix_IsCaseSensitive()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns("Bot:");
        var plugin = new DefaultPrefixPlugin(bot, Substitute.For<IPluginActivator>());

        await plugin.HandleEventAsync(CreateMessage("bot:hello"), CancellationToken.None);
        Assert.False(plugin.Called);

        await plugin.HandleEventAsync(CreateMessage("Bot:hello"), CancellationToken.None);
        Assert.True(plugin.Called);
    }

    #endregion

    #region Per-handler prefix override

    private sealed class PerHandlerPrefixPlugin : MarvPlugin
    {
        public bool DefaultCalled;
        public bool OverrideCalled;

        public PerHandlerPrefixPlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnCommand("ban")]
        public Task HandleBan(CommandContext ctx, CancellationToken ct)
        {
            DefaultCalled = true;
            return Task.CompletedTask;
        }

        [OnCommand("invite", Prefix = ".")]
        public Task HandleInvite(CommandContext ctx, CancellationToken ct)
        {
            OverrideCalled = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PerHandlerPrefix_OverrideUsesOwnPrefix()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns("!");
        var plugin = new PerHandlerPrefixPlugin(bot, Substitute.For<IPluginActivator>());

        await plugin.HandleEventAsync(CreateMessage(".invite #channel"), CancellationToken.None);

        Assert.True(plugin.OverrideCalled);
        Assert.False(plugin.DefaultCalled);
    }

    [Fact]
    public async Task PerHandlerPrefix_OverrideDoesNotMatchDefaultPrefix()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns("!");
        var plugin = new PerHandlerPrefixPlugin(bot, Substitute.For<IPluginActivator>());

        await plugin.HandleEventAsync(CreateMessage("!invite #channel"), CancellationToken.None);

        Assert.False(plugin.OverrideCalled);
    }

    [Fact]
    public async Task PerHandlerPrefix_DefaultHandlerStillUsesDefaultPrefix()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns("!");
        var plugin = new PerHandlerPrefixPlugin(bot, Substitute.For<IPluginActivator>());

        await plugin.HandleEventAsync(CreateMessage("!ban user"), CancellationToken.None);

        Assert.True(plugin.DefaultCalled);
        Assert.False(plugin.OverrideCalled);
    }

    #endregion

    #region Edge cases

    [Fact]
    public async Task PrefixOnly_DoesNotMatchWithoutCommand()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns("!");
        var plugin = new DefaultPrefixPlugin(bot, Substitute.For<IPluginActivator>());

        await plugin.HandleEventAsync(CreateMessage("!"), CancellationToken.None);

        Assert.False(plugin.Called);
    }

    [Fact]
    public async Task EmptyMessage_DoesNotMatch()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns("!");
        var plugin = new DefaultPrefixPlugin(bot, Substitute.For<IPluginActivator>());

        await plugin.HandleEventAsync(CreateMessage(""), CancellationToken.None);

        Assert.False(plugin.Called);
    }

    [Fact]
    public async Task CommandWithNoArgs_SetsEmptyArgString()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns("!");
        var plugin = new DefaultPrefixPlugin(bot, Substitute.For<IPluginActivator>());

        await plugin.HandleEventAsync(CreateMessage("!hello"), CancellationToken.None);

        Assert.True(plugin.Called);
        Assert.Equal("", plugin.ReceivedArgString);
    }

    #endregion
}
