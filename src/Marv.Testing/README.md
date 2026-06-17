# TDW.Marv.Testing

Test helpers for [Marv IRC bot](https://github.com/predakanga/marv)
plugins. Provides builders for handler contexts, mock factories, and a
plugin test harness.

## Who is this for?

This package is for **plugin authors** writing unit tests for their Marv
plugins. It pairs with
[TDW.Marv.Core](https://www.nuget.org/packages/TDW.Marv.Core).

## Quick start

Add this package alongside your preferred test framework (xUnit, NUnit,
etc.) and [NSubstitute](https://nsubstitute.github.io/) (included as a
dependency).

```csharp
using Marv.Testing;
using NSubstitute;
using Xunit;

public class HelloPluginTests
{
    [Fact]
    public async Task Hello_RepliesWithGreeting()
    {
        var harness = PluginTestHarness<HelloPlugin>.Create();
        await harness.LoadAsync();

        var ctx = CommandContextBuilder.Create("hello")
            .InChannel("#test").From("alice").Build();

        await harness.HandleCommandAsync(ctx);

        await harness.Bot.Received().SendMessageAsync(
            "#test", Arg.Is<string>(s => s.Contains("alice")), Arg.Any<CancellationToken>());
    }
}
```

## Context builders

Reduce test setup to 2-3 lines with fluent builders:

```csharp
// Command context
var ctx = CommandContextBuilder.Create("greet", "world")
    .InChannel("#test").From("alice").Build();

// Regex match context
var ctx = RegexMatchContextBuilder.Create(@"hello (\w+)", "hello world")
    .InChannel("#test").From("alice").Build();

// Typed events
var evt = EventBuilder<MessageEvent>.Create(raw => new MessageEvent
{
    Sender = MockUser.Create("alice"),
    Text = "hello",
    RawMessage = raw
}).Build();
```

## Mock factories

| Factory | Description |
|---------|-------------|
| `MockBot.Create(nick?, prefix?)` | NSubstitute mock of `IBot` |
| `MockUser.Create(nick, account?)` | Mock `IUser` |
| `MockChannel.Create(name)` | Mock `IChannel` |
| `DummyIrcMessage.Privmsg` / `.Notice` / `.Empty` | Pre-built IRC messages |
| `DummyIrcMessage.PrivmsgFrom(nick, target, text)` | Custom PRIVMSG |

## Plugin test harness

`PluginTestHarness<T>` wires up DI with mocked dependencies:

```csharp
var harness = PluginTestHarness<MyPlugin>.Create(
    configureServices: services =>
    {
        services.AddSingleton(Options.Create(new MyConfig { ... }));
    });

await harness.LoadAsync();

// Access the plugin and mock bot
harness.Plugin;  // MyPlugin instance
harness.Bot;     // IBot mock (use .Received() assertions)
```

## Documentation

Full API reference: [Plugin API docs](https://github.com/predakanga/marv/blob/main/docs/PLUGIN_API.md)
