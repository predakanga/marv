using Marv.Core.Events;
using Marv.Core.Platform;
using Marv.Core.Plugin;
using Marv.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Marv.Core.Tests.Plugin;

/// <summary>
/// Tests for ChannelOnly, DirectOnly, and Channel filter properties
/// on [OnCommand] and [OnRegex] handler attributes.
/// </summary>
public class HandlerDispatchFilterTests
{
    private static readonly IrcMessage DummyMessage = new("PRIVMSG", ["#test", "hello"]);

    private static IBot CreateBot()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns("!");
        return bot;
    }

    private static MessageEvent CreateChannelMessage(string text, string channelName = "#test")
    {
        var channel = Substitute.For<IChannel>();
        channel.Name.Returns(channelName);
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

    private static MessageEvent CreateDirectMessage(string text)
    {
        var user = Substitute.For<IUser>();
        user.Nick.Returns("tester");

        return new MessageEvent
        {
            Channel = null,
            Sender = user,
            Text = text,
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };
    }

    #region Command ChannelOnly

    private sealed class ChannelOnlyCommandPlugin : MarvPlugin
    {
        public bool Called;

        public ChannelOnlyCommandPlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnCommand("test", ChannelOnly = true)]
        public Task Handle(CommandContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Command_ChannelOnly_FiresInChannel()
    {
        var plugin = new ChannelOnlyCommandPlugin(CreateBot(), Substitute.For<IPluginActivator>());
        await plugin.HandleEventAsync(CreateChannelMessage("!test"), CancellationToken.None);
        Assert.True(plugin.Called);
    }

    [Fact]
    public async Task Command_ChannelOnly_SkipsDirectMessage()
    {
        var plugin = new ChannelOnlyCommandPlugin(CreateBot(), Substitute.For<IPluginActivator>());
        await plugin.HandleEventAsync(CreateDirectMessage("!test"), CancellationToken.None);
        Assert.False(plugin.Called);
    }

    #endregion

    #region Command DirectOnly

    private sealed class DirectOnlyCommandPlugin : MarvPlugin
    {
        public bool Called;

        public DirectOnlyCommandPlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnCommand("test", DirectOnly = true)]
        public Task Handle(CommandContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Command_DirectOnly_FiresInDM()
    {
        var plugin = new DirectOnlyCommandPlugin(CreateBot(), Substitute.For<IPluginActivator>());
        await plugin.HandleEventAsync(CreateDirectMessage("!test"), CancellationToken.None);
        Assert.True(plugin.Called);
    }

    [Fact]
    public async Task Command_DirectOnly_SkipsChannel()
    {
        var plugin = new DirectOnlyCommandPlugin(CreateBot(), Substitute.For<IPluginActivator>());
        await plugin.HandleEventAsync(CreateChannelMessage("!test"), CancellationToken.None);
        Assert.False(plugin.Called);
    }

    #endregion

    #region Command Channel filter

    private sealed class ChannelFilterCommandPlugin : MarvPlugin
    {
        public bool Called;

        public ChannelFilterCommandPlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnCommand("test", Channel = "#ops")]
        public Task Handle(CommandContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Command_Channel_FiresInMatchingChannel()
    {
        var plugin = new ChannelFilterCommandPlugin(CreateBot(), Substitute.For<IPluginActivator>());
        await plugin.HandleEventAsync(CreateChannelMessage("!test", "#ops"), CancellationToken.None);
        Assert.True(plugin.Called);
    }

    [Fact]
    public async Task Command_Channel_SkipsNonMatchingChannel()
    {
        var plugin = new ChannelFilterCommandPlugin(CreateBot(), Substitute.For<IPluginActivator>());
        await plugin.HandleEventAsync(CreateChannelMessage("!test", "#general"), CancellationToken.None);
        Assert.False(plugin.Called);
    }

    [Fact]
    public async Task Command_Channel_SkipsDirectMessage()
    {
        var plugin = new ChannelFilterCommandPlugin(CreateBot(), Substitute.For<IPluginActivator>());
        await plugin.HandleEventAsync(CreateDirectMessage("!test"), CancellationToken.None);
        Assert.False(plugin.Called);
    }

    [Fact]
    public async Task Command_Channel_CaseInsensitive()
    {
        var plugin = new ChannelFilterCommandPlugin(CreateBot(), Substitute.For<IPluginActivator>());
        await plugin.HandleEventAsync(CreateChannelMessage("!test", "#OPS"), CancellationToken.None);
        Assert.True(plugin.Called);
    }

    #endregion

    #region Regex ChannelOnly

    private sealed class ChannelOnlyRegexPlugin : MarvPlugin
    {
        public bool Called;

        public ChannelOnlyRegexPlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnRegex("hello", ChannelOnly = true)]
        public Task Handle(RegexMatchContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Regex_ChannelOnly_FiresInChannel()
    {
        var plugin = new ChannelOnlyRegexPlugin(CreateBot(), Substitute.For<IPluginActivator>());
        await plugin.HandleEventAsync(CreateChannelMessage("hello world"), CancellationToken.None);
        Assert.True(plugin.Called);
    }

    [Fact]
    public async Task Regex_ChannelOnly_SkipsDirectMessage()
    {
        var plugin = new ChannelOnlyRegexPlugin(CreateBot(), Substitute.For<IPluginActivator>());
        await plugin.HandleEventAsync(CreateDirectMessage("hello world"), CancellationToken.None);
        Assert.False(plugin.Called);
    }

    #endregion

    #region Regex Channel filter

    private sealed class ChannelFilterRegexPlugin : MarvPlugin
    {
        public bool Called;

        public ChannelFilterRegexPlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnRegex(@"https?://\S+", Channel = "#links")]
        public Task Handle(RegexMatchContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Regex_Channel_FiresInMatchingChannel()
    {
        var plugin = new ChannelFilterRegexPlugin(CreateBot(), Substitute.For<IPluginActivator>());
        await plugin.HandleEventAsync(CreateChannelMessage("check https://example.com", "#links"), CancellationToken.None);
        Assert.True(plugin.Called);
    }

    [Fact]
    public async Task Regex_Channel_SkipsNonMatchingChannel()
    {
        var plugin = new ChannelFilterRegexPlugin(CreateBot(), Substitute.For<IPluginActivator>());
        await plugin.HandleEventAsync(CreateChannelMessage("check https://example.com", "#general"), CancellationToken.None);
        Assert.False(plugin.Called);
    }

    #endregion

    #region No filters (default behavior preserved)

    private sealed class NoFilterPlugin : MarvPlugin
    {
        public bool ChannelCalled;
        public bool DirectCalled;

        public NoFilterPlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnCommand("test")]
        public Task Handle(CommandContext ctx, CancellationToken ct)
        {
            if (ctx.IsDirect) DirectCalled = true;
            else ChannelCalled = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task NoFilter_FiresInBothContexts()
    {
        var plugin = new NoFilterPlugin(CreateBot(), Substitute.For<IPluginActivator>());

        await plugin.HandleEventAsync(CreateChannelMessage("!test"), CancellationToken.None);
        Assert.True(plugin.ChannelCalled);

        await plugin.HandleEventAsync(CreateDirectMessage("!test"), CancellationToken.None);
        Assert.True(plugin.DirectCalled);
    }

    #endregion
}
