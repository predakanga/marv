using System.Reflection;
using System.Text.RegularExpressions;
using Marv.Core.Events;
using Marv.Core.Formatting;
using Marv.Core.Platform;
using Marv.Core.Protocol;

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

    private readonly List<object> _handlerGroups = [];
    private readonly List<HandlerRegistration> _eventHandlers = [];
    private readonly List<CommandRegistration> _commandHandlers = [];
    private readonly List<RegexRegistration> _regexHandlers = [];
    private readonly List<RawMessageRegistration> _rawMessageHandlers = [];
    private readonly List<IntervalRegistration> _intervalHandlers = [];

    /// <summary>
    /// Derived plugins accept <see cref="IBot"/> and <see cref="IPluginActivator"/>,
    /// and forward both via <c>: base(bot, activator)</c>.
    /// </summary>
    protected MarvPlugin(IBot bot, IPluginActivator activator)
    {
        Bot = bot;
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
        var pluginType = GetType();
        var assembly = pluginType.Assembly;

        var groupTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetCustomAttribute<HandlerGroupAttribute>() is { } attr &&
                        attr.PluginType == pluginType);

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
                _commandHandlers.Add(new CommandRegistration(
                    target, method, cmdAttr.Command.ToLowerInvariant()));
            }

            // [OnRegex] handlers
            foreach (var regexAttr in method.GetCustomAttributes<OnRegexAttribute>())
            {
                _regexHandlers.Add(new RegexRegistration(
                    target, method, new Regex(regexAttr.Pattern, RegexOptions.Compiled)));
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

    /// <inheritdoc />
    public virtual async Task HandleEventAsync(MarvEvent evt, CancellationToken ct)
    {
        // Dispatch [OnEvent] handlers matching by event type
        foreach (var handler in _eventHandlers)
        {
            if (handler.EventType.IsInstanceOfType(evt))
                await InvokeHandler(handler.Target, handler.Method, evt, ct);
        }

        // Dispatch [OnRawMessage] handlers for RawMessageEvent
        if (evt is RawMessageEvent rawEvt)
        {
            foreach (var handler in _rawMessageHandlers)
            {
                if (handler.Command == rawEvt.RawMessage.Command)
                    await InvokeHandler(handler.Target, handler.Method, rawEvt.RawMessage, ct);
            }
        }

        // Dispatch [OnCommand] and [OnRegex] handlers for MessageEvent
        if (evt is MessageEvent msgEvt)
        {
            await DispatchCommandHandlers(msgEvt, ct);
            await DispatchRegexHandlers(msgEvt, ct);
        }

        // Dispatch [OnInterval] handlers
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < _intervalHandlers.Count; i++)
        {
            var handler = _intervalHandlers[i];
            if (now - handler.LastRun >= handler.Interval)
            {
                _intervalHandlers[i] = handler with { LastRun = now };
                await InvokeHandler(handler.Target, handler.Method, ct);
            }
        }
    }

    private async Task DispatchCommandHandlers(MessageEvent msgEvt, CancellationToken ct)
    {
        if (_commandHandlers.Count == 0)
            return;

        var text = IrcFormat.Strip(msgEvt.Text);

        // The command prefix is '!' by default
        // TODO: Make configurable per-bot
        if (text.Length < 2 || text[0] != '!')
            return;

        var spaceIndex = text.IndexOf(' ', 1);
        var command = spaceIndex < 0
            ? text[1..].ToLowerInvariant()
            : text[1..spaceIndex].ToLowerInvariant();
        var argString = spaceIndex < 0 ? "" : text[(spaceIndex + 1)..].TrimStart();
        var args = string.IsNullOrEmpty(argString)
            ? Array.Empty<string>()
            : argString.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var handler in _commandHandlers)
        {
            if (handler.Command == command)
            {
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

                await InvokeHandler(handler.Target, handler.Method, ctx, ct);
            }
        }
    }

    private async Task DispatchRegexHandlers(MessageEvent msgEvt, CancellationToken ct)
    {
        var strippedText = IrcFormat.Strip(msgEvt.Text);
        foreach (var handler in _regexHandlers)
        {
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

                await InvokeHandler(handler.Target, handler.Method, ctx, ct);
            }
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

    /// <inheritdoc />
    public virtual async Task OnUnloadAsync()
    {
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
    private sealed record CommandRegistration(object Target, MethodInfo Method, string Command);
    private sealed record RegexRegistration(object Target, MethodInfo Method, Regex Pattern);
    private sealed record RawMessageRegistration(object Target, MethodInfo Method, string Command);
    private sealed record IntervalRegistration(object Target, MethodInfo Method, TimeSpan Interval, DateTimeOffset LastRun);
}
