# TDW.Marv.Core

Core library for the [Marv IRC bot](https://github.com/predakanga/marv).
Provides the plugin API, bot abstractions, IRC protocol handling, and
service registry.

## Who is this for?

This package is for **plugin authors** building extensions for Marv. It
provides the base classes, attributes, context types, and interfaces you
need to write plugins.

If you're writing **tests** for your plugins, also add
[TDW.Marv.Testing](https://www.nuget.org/packages/TDW.Marv.Testing).

## Quick start

Create a class library targeting the same .NET version as Marv, add a
reference to this package, and implement a plugin:

```csharp
using Marv.Core.Platform;
using Marv.Core.Plugin;
using Microsoft.Extensions.Logging;

public class HelloPlugin : MarvPlugin
{
    public HelloPlugin(IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory)
        : base(bot, activator, loggerFactory) { }

    [OnCommand("hello")]
    private async Task HandleHello(CommandContext ctx, CancellationToken ct)
    {
        await ctx.ReplyAsync($"Hi, {ctx.Sender.Nick}!", ct);
    }
}
```

## Key types

| Type | Description |
|------|-------------|
| `MarvPlugin` | Base class for all plugins |
| `IBot` | Bot instance — send messages, query state |
| `CommandContext` | Context for `[OnCommand]` handlers |
| `RegexMatchContext` | Context for `[OnRegex]` handlers |
| `IPluginActivator` | Create instances with DI resolution |
| `IFilteringAttribute` | Declare handler filter attributes |
| `FilterEvaluator<T>` | Implement handler filter logic |

## Handler attributes

| Attribute | Handler signature | Fires when |
|-----------|-------------------|------------|
| `[OnCommand("name")]` | `Task(CommandContext, CancellationToken)` | Prefixed command message |
| `[OnRegex("pattern")]` | `Task(RegexMatchContext, CancellationToken)` | Message matches pattern |
| `[OnEvent]` | `Task(TEvent, CancellationToken)` | Typed event (inferred from parameter) |
| `[OnRawMessage("CMD")]` | `Task(IrcMessage, CancellationToken)` | Raw IRC message |
| `[OnInterval(Seconds = n)]` | `Task(CancellationToken)` | Timer tick while connected |

All handler methods may omit the `CancellationToken` parameter.

## Configuration

Declare a config class with `[PluginConfig]` and inject `IOptions<T>`:

```csharp
[PluginConfig(Section = "MyPlugin")]
public record MyConfig
{
    public string Greeting { get; init; } = "Hello!";
}
```

Config binds to a root-level JSON section matching the `Section` name.
`IOptionsMonitor<T>` is also available for live-reload support.

## Services

Plugins can provide services to other plugins via `[ProvidesService]` and
`ConfigureServices`. Consume services by adding constructor parameters
(use `= null` for optional dependencies).

## Documentation

Full API reference: [Plugin API docs](https://github.com/predakanga/marv/blob/main/docs/PLUGIN_API.md)
