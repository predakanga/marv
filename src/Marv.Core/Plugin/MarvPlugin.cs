using System.Reflection;
using System.Text.RegularExpressions;
using Marv.Core.Events;
using Marv.Core.Formatting;
using Marv.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Marv.Core.Plugin;

/// <summary>
/// Convenience base class for plugins. Provides default lifecycle implementations,
/// <see cref="IBot"/> access, and reflection-based event dispatch to attributed handler
/// methods on both the plugin class and its handler groups.
/// </summary>
public abstract class MarvPlugin : IPlugin
{
    /// <summary>The bot instance, available to all plugins.</summary>
    protected IBot Bot { get; }

    /// <summary>Logger scoped to the concrete plugin type.</summary>
    protected ILogger Logger { get; }

    private readonly List<object> _handlerGroups = [];
    private readonly List<HandlerRegistration> _eventHandlers = [];
    private readonly List<CommandRegistration> _commandHandlers = [];
    private readonly List<RegexRegistration> _regexHandlers = [];
    private readonly List<RawMessageRegistration> _rawMessageHandlers = [];
    private readonly List<IntervalRegistration> _intervalHandlers = [];
    private readonly Dictionary<MethodInfo, IReadOnlyList<Attribute>> _attributeCache = [];
    private readonly Dictionary<Type, IFilterEvaluator> _evaluatorCache = [];
    private IPluginActivator _activator = null!;
    private CancellationTokenSource? _intervalCts;
    private Task? _intervalTask;

    /// <summary>
    /// Derived plugins accept <see cref="IBot"/>, <see cref="IPluginActivator"/>,
    /// and <see cref="ILoggerFactory"/>, and forward all three via
    /// <c>: base(bot, activator, loggerFactory)</c>.
    /// </summary>
    protected MarvPlugin(IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory)
    {
        Bot = bot;
        _activator = activator;
        Logger = loggerFactory.CreateLogger(GetType());
        DiscoverHandlers(this, GetType());
        DiscoverHandlerGroups(activator);
    }

    /// <summary>
    /// Discovers handler group classes for this plugin in the assembly and creates
    /// instances via the activator. Builds the handler dispatch table from attributed
    /// methods on both this plugin and its handler groups.
    /// </summary>
    private void DiscoverHandlerGroups(IPluginActivator activator)
    {
        var assembly = GetType().Assembly;

        var groupTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetCustomAttribute<HandlerGroupAttribute>() is not null);

        foreach (var groupType in groupTypes)
        {
            var createMethod = typeof(IPluginActivator)
                .GetMethod(nameof(IPluginActivator.CreateInstance))!
                .MakeGenericMethod(groupType);

            var group = createMethod.Invoke(activator, [Array.Empty<object>()])!;
            _handlerGroups.Add(group);
            DiscoverHandlers(group, groupType);
        }
    }

    /// <summary>
    /// Scans a target object (this plugin or a handler group) for attributed handler methods
    /// and registers them in the dispatch tables.
    /// </summary>
    private void DiscoverHandlers(object target, Type type)
    {
        var bindingFlags = target == this
            ? BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            : BindingFlags.Instance | BindingFlags.Public;

        foreach (var method in type.GetMethods(bindingFlags))
        {
            // Pre-cache custom attributes for filter evaluation
            if (!_attributeCache.ContainsKey(method))
            {
                var attrs = method.GetCustomAttributes(inherit: true).Cast<Attribute>().ToArray();
                if (attrs.Length > 0)
                    _attributeCache[method] = attrs;
            }

            // [OnEvent] handlers
            if (method.GetCustomAttribute<OnEventAttribute>() is not null)
            {
                var parameters = method.GetParameters();
                if (parameters.Length >= 1 && typeof(MarvEvent).IsAssignableFrom(parameters[0].ParameterType))
                {
                    _eventHandlers.Add(new HandlerRegistration(
                        target, method, parameters[0].ParameterType));
                }
            }

            // [OnCommand] handlers
            foreach (var cmdAttr in method.GetCustomAttributes<OnCommandAttribute>())
            {
                WarnOnConflictingFilters(cmdAttr.ChannelOnly, cmdAttr.DirectOnly, cmdAttr.Channel,
                    target.GetType().Name, method.Name, "OnCommand");
                _commandHandlers.Add(new CommandRegistration(
                    target, method, cmdAttr.Command.ToLowerInvariant(),
                    cmdAttr.Prefix ?? Bot.CommandPrefix,
                    cmdAttr.ChannelOnly, cmdAttr.DirectOnly, cmdAttr.Channel));
            }

            // [OnRegex] handlers
            foreach (var regexAttr in method.GetCustomAttributes<OnRegexAttribute>())
            {
                WarnOnConflictingFilters(regexAttr.ChannelOnly, regexAttr.DirectOnly, regexAttr.Channel,
                    target.GetType().Name, method.Name, "OnRegex");
                _regexHandlers.Add(new RegexRegistration(
                    target, method, new Regex(regexAttr.Pattern, RegexOptions.Compiled | regexAttr.Options),
                    regexAttr.ChannelOnly, regexAttr.DirectOnly, regexAttr.Channel));
            }

            // [OnRawMessage] handlers
            foreach (var rawAttr in method.GetCustomAttributes<OnRawMessageAttribute>())
            {
                _rawMessageHandlers.Add(new RawMessageRegistration(
                    target, method, rawAttr.Command.ToUpperInvariant()));
            }

            // [OnInterval] handlers
            if (method.GetCustomAttribute<OnIntervalAttribute>() is { } intervalAttr)
            {
                var interval = intervalAttr.Seconds > 0
                    ? TimeSpan.FromSeconds(intervalAttr.Seconds)
                    : TimeSpan.FromMinutes(intervalAttr.Minutes > 0 ? intervalAttr.Minutes : 1);

                _intervalHandlers.Add(new IntervalRegistration(
                    target, method, interval, DateTimeOffset.MinValue));
            }
        }
    }

    private void WarnOnConflictingFilters(
        bool channelOnly, bool directOnly, string? channel,
        string typeName, string methodName, string attributeName)
    {
        if (channelOnly && directOnly)
            Logger.LogWarning("[{Attribute}] on {Type}.{Method} has both ChannelOnly and DirectOnly set — handler will never fire",
                attributeName, typeName, methodName);
        if (directOnly && channel is not null)
            Logger.LogWarning("[{Attribute}] on {Type}.{Method} has both DirectOnly and Channel set — these are contradictory",
                attributeName, typeName, methodName);
    }

    /// <inheritdoc />
    public virtual async Task HandleEventAsync(MarvEvent evt, CancellationToken ct)
    {
        // Dispatch [OnEvent] handlers matching by event type
        foreach (var handler in _eventHandlers)
        {
            if (handler.EventType.IsInstanceOfType(evt))
                await InvokeHandlerSafe(handler.Target, handler.Method, evt, HandlerType.Event, ct);
        }

        // Dispatch [OnRawMessage] handlers for RawMessageEvent
        if (evt is RawMessageEvent rawEvt)
        {
            foreach (var handler in _rawMessageHandlers)
            {
                if (handler.Command == rawEvt.RawMessage.Command)
                    await InvokeHandlerSafe(handler.Target, handler.Method, rawEvt.RawMessage, HandlerType.RawMessage, ct);
            }
        }

        // Dispatch [OnCommand] and [OnRegex] handlers for MessageEvent
        if (evt is MessageEvent msgEvt)
        {
            await DispatchCommandHandlers(msgEvt, ct);
            await DispatchRegexHandlers(msgEvt, ct);
        }
    }

    private async Task DispatchCommandHandlers(MessageEvent msgEvt, CancellationToken ct)
    {
        if (_commandHandlers.Count == 0)
            return;

        var text = IrcFormat.Strip(msgEvt.Text);

        foreach (var handler in _commandHandlers)
        {
            var prefix = handler.Prefix;

            if (text.Length < prefix.Length + 1
                || !text.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var afterPrefix = text.AsSpan(prefix.Length);
            var spaceIndex = afterPrefix.IndexOf(' ');
            var command = spaceIndex < 0
                ? afterPrefix.ToString().ToLowerInvariant()
                : afterPrefix[..spaceIndex].ToString().ToLowerInvariant();

            if (command != handler.Command)
                continue;

            if (handler.ChannelOnly && msgEvt.Channel is null)
                continue;
            if (handler.DirectOnly && msgEvt.Channel is not null)
                continue;
            if (handler.Channel is not null
                && !string.Equals(msgEvt.Channel?.Name, handler.Channel, StringComparison.OrdinalIgnoreCase))
                continue;

            var argString = spaceIndex < 0
                ? ""
                : afterPrefix[(spaceIndex + 1)..].ToString().TrimStart();
            var args = string.IsNullOrEmpty(argString)
                ? Array.Empty<string>()
                : argString.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var ctx = new CommandContext
            {
                Command = command,
                Args = args,
                ArgString = argString,
                Channel = msgEvt.Channel,
                Sender = msgEvt.Sender,
                RawMessage = msgEvt.RawMessage,
                Bot = Bot
            };

            await InvokeHandlerSafe(handler.Target, handler.Method, ctx, HandlerType.Command, ct);
        }
    }

    private async Task DispatchRegexHandlers(MessageEvent msgEvt, CancellationToken ct)
    {
        var strippedText = IrcFormat.Strip(msgEvt.Text);
        foreach (var handler in _regexHandlers)
        {
            if (handler.ChannelOnly && msgEvt.Channel is null)
                continue;
            if (handler.DirectOnly && msgEvt.Channel is not null)
                continue;
            if (handler.Channel is not null
                && !string.Equals(msgEvt.Channel?.Name, handler.Channel, StringComparison.OrdinalIgnoreCase))
                continue;

            var match = handler.Pattern.Match(strippedText);
            if (match.Success)
            {
                var ctx = new RegexMatchContext
                {
                    Match = match,
                    Channel = msgEvt.Channel,
                    Sender = msgEvt.Sender,
                    RawMessage = msgEvt.RawMessage,
                    Bot = Bot
                };

                await InvokeHandlerSafe(handler.Target, handler.Method, ctx, HandlerType.Regex, ct);
            }
        }
    }

    /// <summary>
    /// Called before each handler invocation. Return false to skip the handler.
    /// The default implementation evaluates any <see cref="IFilteringAttribute"/>
    /// attributes on the handler method. Evaluators receive the <see cref="IBot"/>
    /// instance so they can send replies or take IRC actions when denying.
    /// </summary>
    protected virtual async ValueTask<bool> FilterHandlerAsync(
        HandlerInvocation invocation, CancellationToken ct)
    {
        foreach (var attr in invocation.Attributes.OfType<IFilteringAttribute>())
        {
            var evaluator = ResolveEvaluator(attr.EvaluatorType);
            var result = await evaluator.EvaluateAsync(attr, invocation, Bot, ct);
            if (!result.IsAllowed)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Resolves a filter evaluator by type, caching instances for the plugin's lifetime.
    /// </summary>
    private IFilterEvaluator ResolveEvaluator(Type evaluatorType)
    {
        if (_evaluatorCache.TryGetValue(evaluatorType, out var cached))
            return cached;

        var createMethod = typeof(IPluginActivator)
            .GetMethod(nameof(IPluginActivator.CreateInstance))!
            .MakeGenericMethod(evaluatorType);

        var evaluator = (IFilterEvaluator)createMethod.Invoke(_activator, [Array.Empty<object>()])!;
        _evaluatorCache[evaluatorType] = evaluator;
        return evaluator;
    }

    /// <summary>
    /// Returns the pre-cached custom attributes for a handler method.
    /// </summary>
    private IReadOnlyList<Attribute> GetCachedAttributes(MethodInfo method)
    {
        return _attributeCache.TryGetValue(method, out var attrs)
            ? attrs
            : Array.Empty<Attribute>();
    }

    /// <summary>
    /// Invokes a handler method with filtering, catching and logging any exceptions
    /// so that subsequent handlers continue to run. If <see cref="FilterHandlerAsync"/>
    /// throws, the handler is skipped (fail-closed).
    /// </summary>
    private async Task InvokeHandlerSafe(
        object target, MethodInfo method, object? arg,
        HandlerType type, CancellationToken ct)
    {
        try
        {
            var invocation = new HandlerInvocation
            {
                Method = method,
                Target = target,
                Type = type,
                Context = arg,
                Attributes = GetCachedAttributes(method)
            };

            if (!await FilterHandlerAsync(invocation, ct))
                return;

            if (arg is not null)
                await InvokeHandler(target, method, arg, ct);
            else
                await InvokeHandler(target, method, ct);

            (Bot.Statistics as Irc.BotStatistics)?.IncrementHandlersInvoked();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Handler {Type}.{Method} threw an exception",
                target.GetType().Name, method.Name);
        }
    }

    /// <summary>
    /// Invokes a handler method with the appropriate parameters.
    /// </summary>
    private static async Task InvokeHandler(object target, MethodInfo method, object arg, CancellationToken ct)
    {
        var parameters = method.GetParameters();
        object?[] args = parameters.Length switch
        {
            1 => [arg],
            2 when parameters[1].ParameterType == typeof(CancellationToken) => [arg, ct],
            _ => [arg, ct]
        };

        var result = method.Invoke(target, args);
        if (result is Task task)
            await task;
    }

    /// <summary>
    /// Invokes a handler method with only a CancellationToken (for interval handlers).
    /// </summary>
    private static async Task InvokeHandler(object target, MethodInfo method, CancellationToken ct)
    {
        var parameters = method.GetParameters();
        object?[] args = parameters.Length switch
        {
            0 => [],
            1 when parameters[0].ParameterType == typeof(CancellationToken) => [ct],
            _ => [ct]
        };

        var result = method.Invoke(target, args);
        if (result is Task task)
            await task;
    }

    /// <inheritdoc />
    public virtual async Task OnLoadAsync(CancellationToken ct)
    {
        foreach (var group in _handlerGroups)
        {
            var method = group.GetType().GetMethod("OnLoadAsync",
                BindingFlags.Public | BindingFlags.Instance,
                [typeof(CancellationToken)]);
            if (method is not null)
            {
                var result = method.Invoke(group, [ct]);
                if (result is Task task) await task;
            }
        }

        StartIntervalTimers();
    }

    /// <inheritdoc />
    public virtual async Task OnConnectedAsync(CancellationToken ct)
    {
        foreach (var group in _handlerGroups)
        {
            var method = group.GetType().GetMethod("OnConnectedAsync",
                BindingFlags.Public | BindingFlags.Instance,
                [typeof(CancellationToken)]);
            if (method is not null)
            {
                var result = method.Invoke(group, [ct]);
                if (result is Task task) await task;
            }
        }

    }

    /// <inheritdoc />
    public virtual async Task OnDisconnectedAsync()
    {
        foreach (var group in _handlerGroups)
        {
            var method = group.GetType().GetMethod("OnDisconnectedAsync",
                BindingFlags.Public | BindingFlags.Instance,
                Type.EmptyTypes);
            if (method is not null)
            {
                var result = method.Invoke(group, []);
                if (result is Task task) await task;
            }
        }
    }

    /// <summary>
    /// Starts a background task that fires [OnInterval] handlers on schedule,
    /// independent of the event stream.
    /// </summary>
    private void StartIntervalTimers()
    {
        if (_intervalHandlers.Count == 0) return;

        // Reset last-run timestamps so intervals start fresh each connection
        for (var i = 0; i < _intervalHandlers.Count; i++)
            _intervalHandlers[i] = _intervalHandlers[i] with { LastRun = DateTimeOffset.UtcNow };

        _intervalCts = new CancellationTokenSource();
        var ct = _intervalCts.Token;
        _intervalTask = Task.Run(() => RunIntervalLoopAsync(ct), ct);
    }

    /// <summary>
    /// Stops the background interval timer task and waits for it to complete.
    /// </summary>
    private async Task StopIntervalTimersAsync()
    {
        if (_intervalCts is null) return;

        await _intervalCts.CancelAsync();
        if (_intervalTask is not null)
        {
            try { await _intervalTask; }
            catch (OperationCanceledException) { }
        }

        _intervalCts.Dispose();
        _intervalCts = null;
        _intervalTask = null;
    }

    /// <summary>
    /// Background loop that sleeps until the next interval handler is due,
    /// then invokes all due handlers.
    /// </summary>
    private async Task RunIntervalLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var nextDue = TimeSpan.MaxValue;

            for (var i = 0; i < _intervalHandlers.Count; i++)
            {
                var handler = _intervalHandlers[i];
                var elapsed = now - handler.LastRun;

                if (elapsed >= handler.Interval)
                {
                    _intervalHandlers[i] = handler with { LastRun = now };
                    await InvokeHandlerSafe(handler.Target, handler.Method, null, HandlerType.Interval, ct);
                }
                else
                {
                    var remaining = handler.Interval - elapsed;
                    if (remaining < nextDue)
                        nextDue = remaining;
                }
            }

            // Recalculate next due time after invoking handlers (they may have taken time)
            if (nextDue == TimeSpan.MaxValue)
            {
                nextDue = _intervalHandlers.Min(h => h.Interval);
            }

            await Task.Delay(nextDue, ct);
        }
    }

    /// <inheritdoc />
    public virtual async Task OnUnloadAsync()
    {
        await StopIntervalTimersAsync();

        foreach (var group in _handlerGroups)
        {
            var method = group.GetType().GetMethod("OnUnloadAsync",
                BindingFlags.Public | BindingFlags.Instance,
                Type.EmptyTypes);
            if (method is not null)
            {
                var result = method.Invoke(group, []);
                if (result is Task task) await task;
            }
        }
    }

    private sealed record HandlerRegistration(object Target, MethodInfo Method, Type EventType);
    private sealed record CommandRegistration(
        object Target, MethodInfo Method, string Command, string Prefix,
        bool ChannelOnly, bool DirectOnly, string? Channel);

    private sealed record RegexRegistration(
        object Target, MethodInfo Method, Regex Pattern,
        bool ChannelOnly, bool DirectOnly, string? Channel);
    private sealed record RawMessageRegistration(object Target, MethodInfo Method, string Command);
    private sealed record IntervalRegistration(object Target, MethodInfo Method, TimeSpan Interval, DateTimeOffset LastRun);
}
