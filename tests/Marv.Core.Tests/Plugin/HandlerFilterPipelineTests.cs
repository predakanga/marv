using Marv.Core.Events;
using Marv.Core.Platform;
using Marv.Core.Plugin;
using Marv.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Marv.Core.Tests.Plugin;

/// <summary>
/// Tests for the handler filter pipeline: <see cref="MarvPlugin.FilterHandlerAsync"/>,
/// <see cref="IFilteringAttribute"/>, <see cref="IFilterEvaluator"/>, and
/// <see cref="FilterEvaluator{TAttribute}"/>.
/// </summary>
public class HandlerFilterPipelineTests
{
    private static readonly IrcMessage DummyMessage = new("PRIVMSG", ["#test", "hello"]);

    private static IBot CreateBot()
    {
        var bot = Substitute.For<IBot>();
        bot.CommandPrefix.Returns("!");
        return bot;
    }

    private static IPluginActivator CreateActivator()
    {
        var activator = Substitute.For<IPluginActivator>();
        // Default: return a mock for any CreateInstance call
        return activator;
    }

    private static IPluginActivator CreateActivatorThatReturns<T>(T instance) where T : class
    {
        var activator = Substitute.For<IPluginActivator>();
        activator.CreateInstance<T>().Returns(instance);
        return activator;
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

    #region FilterResult

    [Fact]
    public void FilterResult_Allowed_IsAllowedTrue()
    {
        var result = FilterResult.Allowed;
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void FilterResult_Denied_IsAllowedFalse()
    {
        var result = FilterResult.Denied;
        Assert.False(result.IsAllowed);
    }

    #endregion

    #region No filter attributes — handler runs normally

    private sealed class NoFilterPlugin : MarvPlugin
    {
        public bool Called;

        public NoFilterPlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnCommand("test")]
        public Task Handle(CommandContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Handler_WithoutFilterAttributes_RunsNormally()
    {
        var plugin = new NoFilterPlugin(CreateBot(), CreateActivator());
        await plugin.HandleEventAsync(CreateChannelMessage("!test"), CancellationToken.None);
        Assert.True(plugin.Called);
    }

    #endregion

    #region IFilteringAttribute that denies

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class AlwaysDenyAttribute : Attribute, IFilteringAttribute
    {
        public Type EvaluatorType => typeof(AlwaysDenyEvaluator);
    }

    private sealed class AlwaysDenyEvaluator : IFilterEvaluator
    {
        public ValueTask<FilterResult> EvaluateAsync(
            IFilteringAttribute attribute, HandlerInvocation invocation,
            IBot bot, CancellationToken ct)
        {
            return new ValueTask<FilterResult>(FilterResult.Denied);
        }
    }

    private sealed class DeniedCommandPlugin : MarvPlugin
    {
        public bool Called;

        public DeniedCommandPlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnCommand("test")]
        [AlwaysDeny]
        public Task Handle(CommandContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Handler_WithDenyFilter_IsSkipped()
    {
        var activator = Substitute.For<IPluginActivator>();
        SetupActivatorFor<AlwaysDenyEvaluator>(activator);

        var plugin = new DeniedCommandPlugin(CreateBot(), activator);
        await plugin.HandleEventAsync(CreateChannelMessage("!test"), CancellationToken.None);
        Assert.False(plugin.Called);
    }

    #endregion

    #region IFilteringAttribute that allows

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class AlwaysAllowAttribute : Attribute, IFilteringAttribute
    {
        public Type EvaluatorType => typeof(AlwaysAllowEvaluator);
    }

    private sealed class AlwaysAllowEvaluator : IFilterEvaluator
    {
        public ValueTask<FilterResult> EvaluateAsync(
            IFilteringAttribute attribute, HandlerInvocation invocation,
            IBot bot, CancellationToken ct)
        {
            return new ValueTask<FilterResult>(FilterResult.Allowed);
        }
    }

    private sealed class AllowedCommandPlugin : MarvPlugin
    {
        public bool Called;

        public AllowedCommandPlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnCommand("test")]
        [AlwaysAllow]
        public Task Handle(CommandContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Handler_WithAllowFilter_Runs()
    {
        var activator = Substitute.For<IPluginActivator>();
        SetupActivatorFor<AlwaysAllowEvaluator>(activator);

        var plugin = new AllowedCommandPlugin(CreateBot(), activator);
        await plugin.HandleEventAsync(CreateChannelMessage("!test"), CancellationToken.None);
        Assert.True(plugin.Called);
    }

    #endregion

    #region Multiple filters — first deny wins

    private sealed class AllowThenDenyPlugin : MarvPlugin
    {
        public bool Called;

        public AllowThenDenyPlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnCommand("test")]
        [AlwaysAllow]
        [AlwaysDeny]
        public Task Handle(CommandContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Handler_WithMultipleFilters_FirstDenyWins()
    {
        var activator = Substitute.For<IPluginActivator>();
        SetupActivatorFor<AlwaysAllowEvaluator>(activator);
        SetupActivatorFor<AlwaysDenyEvaluator>(activator);

        var plugin = new AllowThenDenyPlugin(CreateBot(), activator);
        await plugin.HandleEventAsync(CreateChannelMessage("!test"), CancellationToken.None);
        Assert.False(plugin.Called);
    }

    #endregion

    #region FilterEvaluator<T> typed base class

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class RequireSenderAttribute(string nick) : Attribute, IFilteringAttribute
    {
        public string Nick { get; } = nick;
        public Type EvaluatorType => typeof(RequireSenderEvaluator);
    }

    private sealed class RequireSenderEvaluator : FilterEvaluator<RequireSenderAttribute>
    {
        protected override ValueTask<FilterResult> EvaluateAsync(
            RequireSenderAttribute attribute, HandlerInvocation invocation,
            IBot bot, CancellationToken ct)
        {
            var sender = (invocation.Context as CommandContext)?.Sender;
            var result = sender?.Nick == attribute.Nick
                ? FilterResult.Allowed
                : FilterResult.Denied;
            return new ValueTask<FilterResult>(result);
        }
    }

    private sealed class RequireSenderPlugin : MarvPlugin
    {
        public bool Called;

        public RequireSenderPlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnCommand("test")]
        [RequireSender("admin")]
        public Task Handle(CommandContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task TypedEvaluator_AllowsMatchingSender()
    {
        var activator = Substitute.For<IPluginActivator>();
        SetupActivatorFor<RequireSenderEvaluator>(activator);

        var bot = CreateBot();
        var plugin = new RequireSenderPlugin(bot, activator);

        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("admin");

        var evt = new MessageEvent
        {
            Channel = channel,
            Sender = user,
            Text = "!test",
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        await plugin.HandleEventAsync(evt, CancellationToken.None);
        Assert.True(plugin.Called);
    }

    [Fact]
    public async Task TypedEvaluator_DeniesNonMatchingSender()
    {
        var activator = Substitute.For<IPluginActivator>();
        SetupActivatorFor<RequireSenderEvaluator>(activator);

        var bot = CreateBot();
        var plugin = new RequireSenderPlugin(bot, activator);
        await plugin.HandleEventAsync(CreateChannelMessage("!test"), CancellationToken.None);
        Assert.False(plugin.Called);
    }

    #endregion

    #region Evaluator receives IBot

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class BotCheckAttribute : Attribute, IFilteringAttribute
    {
        public Type EvaluatorType => typeof(BotCheckEvaluator);
    }

    private sealed class BotCheckEvaluator : IFilterEvaluator
    {
        public IBot? ReceivedBot { get; private set; }

        public ValueTask<FilterResult> EvaluateAsync(
            IFilteringAttribute attribute, HandlerInvocation invocation,
            IBot bot, CancellationToken ct)
        {
            ReceivedBot = bot;
            return new ValueTask<FilterResult>(FilterResult.Allowed);
        }
    }

    private sealed class BotCheckPlugin : MarvPlugin
    {
        public bool Called;

        public BotCheckPlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnCommand("test")]
        [BotCheck]
        public Task Handle(CommandContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Evaluator_ReceivesBotInstance()
    {
        var evaluator = new BotCheckEvaluator();
        var activator = Substitute.For<IPluginActivator>();
        SetupActivatorInstance<BotCheckEvaluator>(activator, evaluator);

        var bot = CreateBot();
        var plugin = new BotCheckPlugin(bot, activator);
        await plugin.HandleEventAsync(CreateChannelMessage("!test"), CancellationToken.None);

        Assert.True(plugin.Called);
        Assert.Same(bot, evaluator.ReceivedBot);
    }

    #endregion

    #region FilterHandlerAsync override

    private sealed class CustomFilterPlugin : MarvPlugin
    {
        public bool Called;
        public bool FilterCalled;
        private readonly bool _shouldAllow;

        public CustomFilterPlugin(IBot bot, IPluginActivator activator, bool shouldAllow)
            : base(bot, activator, NullLoggerFactory.Instance)
        {
            _shouldAllow = shouldAllow;
        }

        protected override ValueTask<bool> FilterHandlerAsync(
            HandlerInvocation invocation, CancellationToken ct)
        {
            FilterCalled = true;
            return new ValueTask<bool>(_shouldAllow);
        }

        [OnCommand("test")]
        public Task Handle(CommandContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task FilterHandlerAsync_Override_CanAllowHandler()
    {
        var plugin = new CustomFilterPlugin(CreateBot(), CreateActivator(), shouldAllow: true);
        await plugin.HandleEventAsync(CreateChannelMessage("!test"), CancellationToken.None);
        Assert.True(plugin.FilterCalled);
        Assert.True(plugin.Called);
    }

    [Fact]
    public async Task FilterHandlerAsync_Override_CanDenyHandler()
    {
        var plugin = new CustomFilterPlugin(CreateBot(), CreateActivator(), shouldAllow: false);
        await plugin.HandleEventAsync(CreateChannelMessage("!test"), CancellationToken.None);
        Assert.True(plugin.FilterCalled);
        Assert.False(plugin.Called);
    }

    #endregion

    #region Filter exception — fail-closed

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class ThrowingFilterAttribute : Attribute, IFilteringAttribute
    {
        public Type EvaluatorType => typeof(ThrowingEvaluator);
    }

    private sealed class ThrowingEvaluator : IFilterEvaluator
    {
        public ValueTask<FilterResult> EvaluateAsync(
            IFilteringAttribute attribute, HandlerInvocation invocation,
            IBot bot, CancellationToken ct)
        {
            throw new InvalidOperationException("Filter error");
        }
    }

    private sealed class ThrowingFilterPlugin : MarvPlugin
    {
        public bool Called;

        public ThrowingFilterPlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnCommand("test")]
        [ThrowingFilter]
        public Task Handle(CommandContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Filter_ThatThrows_SkipsHandler()
    {
        var activator = Substitute.For<IPluginActivator>();
        SetupActivatorFor<ThrowingEvaluator>(activator);

        var plugin = new ThrowingFilterPlugin(CreateBot(), activator);
        await plugin.HandleEventAsync(CreateChannelMessage("!test"), CancellationToken.None);
        Assert.False(plugin.Called);
    }

    #endregion

    #region HandlerInvocation carries correct context

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class InvocationCaptureAttribute : Attribute, IFilteringAttribute
    {
        public Type EvaluatorType => typeof(InvocationCaptureEvaluator);
    }

    private sealed class InvocationCaptureEvaluator : IFilterEvaluator
    {
        public HandlerInvocation? CapturedInvocation { get; private set; }

        public ValueTask<FilterResult> EvaluateAsync(
            IFilteringAttribute attribute, HandlerInvocation invocation,
            IBot bot, CancellationToken ct)
        {
            CapturedInvocation = invocation;
            return new ValueTask<FilterResult>(FilterResult.Allowed);
        }
    }

    private sealed class InvocationCapturePlugin : MarvPlugin
    {
        public InvocationCapturePlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnCommand("test")]
        [InvocationCapture]
        public Task Handle(CommandContext ctx, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task HandlerInvocation_CarriesCorrectMetadata()
    {
        var evaluator = new InvocationCaptureEvaluator();
        var activator = Substitute.For<IPluginActivator>();
        SetupActivatorInstance<InvocationCaptureEvaluator>(activator, evaluator);

        var plugin = new InvocationCapturePlugin(CreateBot(), activator);
        await plugin.HandleEventAsync(CreateChannelMessage("!test"), CancellationToken.None);

        Assert.NotNull(evaluator.CapturedInvocation);
        var inv = evaluator.CapturedInvocation!.Value;

        Assert.Equal(HandlerType.Command, inv.Type);
        Assert.IsType<CommandContext>(inv.Context);
        Assert.Equal("Handle", inv.Method.Name);
        Assert.Same(plugin, inv.Target);
        Assert.Contains(inv.Attributes, a => a is InvocationCaptureAttribute);
    }

    #endregion

    #region Evaluator caching

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    private sealed class CountingFilterAttribute : Attribute, IFilteringAttribute
    {
        public Type EvaluatorType => typeof(CountingEvaluator);
    }

    private sealed class CountingEvaluator : IFilterEvaluator
    {
        public int CallCount;

        public ValueTask<FilterResult> EvaluateAsync(
            IFilteringAttribute attribute, HandlerInvocation invocation,
            IBot bot, CancellationToken ct)
        {
            CallCount++;
            return new ValueTask<FilterResult>(FilterResult.Allowed);
        }
    }

    private sealed class CountingPlugin : MarvPlugin
    {
        public CountingPlugin(IBot bot, IPluginActivator activator)
            : base(bot, activator, NullLoggerFactory.Instance) { }

        [OnCommand("test")]
        [CountingFilter]
        public Task Handle(CommandContext ctx, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task Evaluator_IsCachedAcrossInvocations()
    {
        var evaluator = new CountingEvaluator();
        var activator = Substitute.For<IPluginActivator>();
        SetupActivatorInstance<CountingEvaluator>(activator, evaluator);

        var plugin = new CountingPlugin(CreateBot(), activator);

        await plugin.HandleEventAsync(CreateChannelMessage("!test"), CancellationToken.None);
        await plugin.HandleEventAsync(CreateChannelMessage("!test"), CancellationToken.None);

        Assert.Equal(2, evaluator.CallCount);
        // CreateInstance should only be called once — evaluator is cached
        activator.Received(1).CreateInstance<CountingEvaluator>();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Configures the activator mock to return a new instance of <typeparamref name="T"/>
    /// when CreateInstance is called for that type.
    /// </summary>
    private static void SetupActivatorFor<T>(IPluginActivator activator) where T : class, new()
    {
        activator.CreateInstance<T>().Returns(_ => new T());
    }

    /// <summary>
    /// Configures the activator mock to return a specific instance.
    /// </summary>
    private static void SetupActivatorInstance<T>(IPluginActivator activator, T instance) where T : class
    {
        activator.CreateInstance<T>().Returns(instance);
    }

    #endregion
}
