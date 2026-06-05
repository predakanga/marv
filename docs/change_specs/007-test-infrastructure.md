# CS-007: Test Infrastructure

**Source:** `downstream_suggestions/ai_enablers.md` §4
**Scope:** New package (`Marv.Testing`)
**Complexity:** Medium
**Breaking changes:** None (new package)

---

## Problem

Plugin test setup requires ~15-20 lines of NSubstitute boilerplate per test:
mocking `IBot`, `IUser`, `IChannel`, `IPluginActivator`, constructing
`CommandContext` with 6 required properties (most irrelevant to the test),
and creating dummy `IrcMessage` values for every event's `RawMessage`
property. The `plugin-api-draft.md` promises test fakes
(`CommandContextFake`, `BotFake`, etc.) that don't exist.

This boilerplate is a significant friction point for both human developers
and AI code generators.

## Design

Ship a `Marv.Testing` NuGet package (or namespace within `Marv.Core` if a
separate package feels heavy) with builder/factory helpers that reduce test
setup to 2-3 lines.

### Package or namespace?

- **Separate package (`Marv.Testing`):** Clean separation. Test helpers
  don't ship in production. Can depend on NSubstitute without polluting
  core. Plugin projects add it as a test-only dependency.
- **Namespace in `Marv.Core`:** No extra NuGet package to manage. But ships
  test utilities in production assemblies and can't depend on NSubstitute.

**Recommendation:** Separate `Marv.Testing` package. It's the standard
pattern (cf. `Microsoft.AspNetCore.Mvc.Testing`,
`Microsoft.EntityFrameworkCore.InMemory`).

## API surface

### CommandContextBuilder

```csharp
public sealed class CommandContextBuilder
{
    public static CommandContextBuilder Create(string command, string args = "");

    public CommandContextBuilder InChannel(string channelName);
    public CommandContextBuilder AsDirect();
    public CommandContextBuilder From(string nick, string? account = null);
    public CommandContextBuilder WithBot(IBot bot);
    public CommandContext Build();
}
```

`Build()` creates mock `IUser`, `IChannel` (if channel specified), `IBot`
(if not provided), and a dummy `IrcMessage`. Defaults:
- Sender: `"testuser"` with no account
- Channel: none (direct message) unless `InChannel` called
- Bot: NSubstitute mock with `Self.Nick` = `"Marv"`

### RegexMatchContextBuilder

```csharp
public sealed class RegexMatchContextBuilder
{
    public static RegexMatchContextBuilder Create(string pattern, string input);

    public RegexMatchContextBuilder InChannel(string channelName);
    public RegexMatchContextBuilder AsDirect();
    public RegexMatchContextBuilder From(string nick, string? account = null);
    public RegexMatchContextBuilder WithBot(IBot bot);
    public RegexMatchContext Build();
}
```

### EventBuilder\<T\>

```csharp
public sealed class EventBuilder<T> where T : MarvEvent, new()
{
    public static EventBuilder<T> Create();

    public EventBuilder<T> With(Action<T> configure);
    public T Build();
}
```

`Build()` fills in `RawMessage` with a dummy value and `Timestamp` with
`DateTimeOffset.UtcNow` if not explicitly set.

### PluginTestHarness

```csharp
public sealed class PluginTestHarness<TPlugin> where TPlugin : MarvPlugin
{
    public TPlugin Plugin { get; }
    public IBot Bot { get; }

    public static PluginTestHarness<TPlugin> Create(
        Action<IServiceCollection>? configureServices = null);
}
```

Creates a plugin instance with a mocked `IBot`, real `IPluginActivator`
backed by a test `IServiceProvider`, and a real `ILoggerFactory` (to
`NullLoggerFactory` or a capturing logger for test assertions).

### Usage example

```csharp
// Before (current boilerplate)
var bot = Substitute.For<IBot>();
var selfUser = Substitute.For<IUser>();
selfUser.Nick.Returns("Marv");
bot.Self.Returns(selfUser);
var activator = Substitute.For<IPluginActivator>();
var channel = Substitute.For<IChannel>();
channel.Name.Returns("#test");
var sender = Substitute.For<IUser>();
sender.Nick.Returns("alice");
var ctx = new CommandContext
{
    Command = "hello",
    Args = Array.Empty<string>(),
    ArgString = "",
    Channel = channel,
    Sender = sender,
    RawMessage = new IrcMessage("PRIVMSG", ["#test", "!hello"]),
    Bot = bot
};

// After (with Marv.Testing)
var ctx = CommandContextBuilder.Create("hello")
    .InChannel("#test")
    .From("alice")
    .Build();
```

## Dependencies

- `Marv.Core` (project reference)
- `NSubstitute` (for mock generation)
- `Microsoft.Extensions.DependencyInjection` (for PluginTestHarness)

## Impact

- **Plugin test authoring:** ~85% reduction in setup boilerplate per test.
- **AI code generation:** Fewer lines of boilerplate means fewer
  opportunities for LLMs to produce incorrect setup code.
- **Existing tests:** Marv's own test suite can optionally migrate to
  use these helpers, but this is not required.

## Open questions

1. Should `PluginTestHarness` call `OnLoadAsync` automatically? Probably
   not — tests may want to set up additional state before load. Provide a
   `LoadAsync()` method instead.
2. Should the builders support fluent chaining for `IBot` behavior
   configuration (e.g. `.WithBotThatReplies()`)? Probably not in v1 —
   keep it simple. Tests that need custom `IBot` behavior can still use
   `WithBot(customMock)`.
