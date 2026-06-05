using Marv.Core.Events;
using Marv.Core.Platform;
using Marv.Core.Plugin;
using Marv.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Marv.Core.Tests.Plugin;

/// <summary>
/// Tests that MarvPlugin catches handler exceptions so they don't skip
/// subsequent handlers or kill the interval timer loop.
/// </summary>
public class HandlerResilienceTests
{
    private static readonly IrcMessage DummyMessage = new("PRIVMSG", ["#test", "hello"]);

    /// <summary>
    /// Plugin with two [OnEvent] handlers for the same event type.
    /// The first one throws; the second should still run.
    /// </summary>
    private sealed class TwoEventHandlerPlugin : MarvPlugin
    {
        public bool FirstCalled;
        public bool SecondCalled;

        public TwoEventHandlerPlugin(IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory)
            : base(bot, activator, loggerFactory) { }

        [OnEvent]
        public Task HandleFirst(MessageEvent e, CancellationToken ct)
        {
            FirstCalled = true;
            throw new InvalidOperationException("Handler 1 fails");
        }

        [OnEvent]
        public Task HandleSecond(MessageEvent e, CancellationToken ct)
        {
            SecondCalled = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ThrowingEventHandler_DoesNotSkipSubsequentHandlers()
    {
        var bot = Substitute.For<IBot>();
        var activator = Substitute.For<IPluginActivator>();
        var plugin = new TwoEventHandlerPlugin(bot, activator, NullLoggerFactory.Instance);

        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("tester");

        var evt = new MessageEvent
        {
            Channel = channel,
            Sender = user,
            Text = "hello",
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        // Should not throw — the exception is caught internally
        await plugin.HandleEventAsync(evt, CancellationToken.None);

        Assert.True(plugin.FirstCalled);
        Assert.True(plugin.SecondCalled);
    }

    /// <summary>
    /// Plugin with two [OnCommand] handlers. The first throws; the second should still run.
    /// </summary>
    private sealed class TwoCommandPlugin : MarvPlugin
    {
        public bool PingCalled;
        public bool PongCalled;

        public TwoCommandPlugin(IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory)
            : base(bot, activator, loggerFactory) { }

        [OnCommand("test")]
        public Task HandlePing(CommandContext ctx, CancellationToken ct)
        {
            PingCalled = true;
            throw new InvalidOperationException("Command handler fails");
        }

        [OnCommand("test")]
        public Task HandlePong(CommandContext ctx, CancellationToken ct)
        {
            PongCalled = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ThrowingCommandHandler_DoesNotSkipSubsequentHandlers()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns("!");
        var activator = Substitute.For<IPluginActivator>();
        var plugin = new TwoCommandPlugin(bot, activator, NullLoggerFactory.Instance);

        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("tester");

        var evt = new MessageEvent
        {
            Channel = channel,
            Sender = user,
            Text = "!test",
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        await plugin.HandleEventAsync(evt, CancellationToken.None);

        Assert.True(plugin.PingCalled);
        Assert.True(plugin.PongCalled);
    }

    /// <summary>
    /// Plugin with a throwing regex handler and a non-throwing event handler.
    /// The event handler should still be called.
    /// </summary>
    private sealed class RegexAndEventPlugin : MarvPlugin
    {
        public bool RegexCalled;
        public bool EventCalled;

        public RegexAndEventPlugin(IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory)
            : base(bot, activator, loggerFactory) { }

        [OnRegex("hello")]
        public Task HandleRegex(RegexMatchContext ctx, CancellationToken ct)
        {
            RegexCalled = true;
            throw new InvalidOperationException("Regex handler fails");
        }

        [OnEvent]
        public Task HandleEvent(MessageEvent e, CancellationToken ct)
        {
            EventCalled = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ThrowingRegexHandler_DoesNotSkipEventHandlers()
    {
        var bot = Substitute.For<IBot>();
        var activator = Substitute.For<IPluginActivator>();
        var plugin = new RegexAndEventPlugin(bot, activator, NullLoggerFactory.Instance);

        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("tester");

        var evt = new MessageEvent
        {
            Channel = channel,
            Sender = user,
            Text = "hello world",
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        await plugin.HandleEventAsync(evt, CancellationToken.None);

        // Event handlers run before regex handlers (in HandleEventAsync order),
        // but both should run regardless of which throws
        Assert.True(plugin.EventCalled);
        Assert.True(plugin.RegexCalled);
    }

    /// <summary>
    /// Plugin with a throwing [OnInterval] handler. The loop should survive
    /// and continue ticking.
    /// </summary>
    private sealed class ThrowingIntervalPlugin : MarvPlugin
    {
        public int InvocationCount;

        public ThrowingIntervalPlugin(IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory)
            : base(bot, activator, loggerFactory) { }

        [OnInterval(Seconds = 0.1)]
        public void Tick()
        {
            Interlocked.Increment(ref InvocationCount);
            throw new InvalidOperationException("Interval handler fails");
        }
    }

    [Fact]
    public async Task ThrowingIntervalHandler_DoesNotKillLoop()
    {
        var bot = Substitute.For<IBot>();
        var activator = Substitute.For<IPluginActivator>();
        var plugin = new ThrowingIntervalPlugin(bot, activator, NullLoggerFactory.Instance);

        await plugin.OnLoadAsync(CancellationToken.None);
        await Task.Delay(350);
        await plugin.OnUnloadAsync();

        // Should have fired multiple times despite each one throwing
        Assert.True(plugin.InvocationCount >= 2,
            $"Expected at least 2 invocations but got {plugin.InvocationCount}");
    }

    /// <summary>
    /// Plugin with a throwing [OnRawMessage] handler and a non-throwing one for the
    /// same command. Both should be invoked.
    /// </summary>
    private sealed class TwoRawMessagePlugin : MarvPlugin
    {
        public bool FirstCalled;
        public bool SecondCalled;

        public TwoRawMessagePlugin(IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory)
            : base(bot, activator, loggerFactory) { }

        [OnRawMessage("PRIVMSG")]
        public Task HandleFirst(IrcMessage msg, CancellationToken ct)
        {
            FirstCalled = true;
            throw new InvalidOperationException("Raw handler fails");
        }

        [OnRawMessage("PRIVMSG")]
        public Task HandleSecond(IrcMessage msg, CancellationToken ct)
        {
            SecondCalled = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ThrowingRawMessageHandler_DoesNotSkipSubsequent()
    {
        var bot = Substitute.For<IBot>();
        var activator = Substitute.For<IPluginActivator>();
        var plugin = new TwoRawMessagePlugin(bot, activator, NullLoggerFactory.Instance);

        var rawEvt = new RawMessageEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        await plugin.HandleEventAsync(rawEvt, CancellationToken.None);

        Assert.True(plugin.FirstCalled);
        Assert.True(plugin.SecondCalled);
    }
}
