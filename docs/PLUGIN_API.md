# Marv Plugin API Reference

Quick reference for building Marv plugins. All types are in the `Marv.Core`
namespace unless noted otherwise.

---

## 1. Minimal Plugin

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

The three-parameter constructor (`IBot`, `IPluginActivator`, `ILoggerFactory`)
is the minimum. Add more DI parameters after `ILoggerFactory` as needed (see
§8 and §9).

---

## 2. Constructor & Base Class

```
protected MarvPlugin(IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory)
```

**Protected members:** `IBot Bot`, `ILogger Logger` (scoped to your plugin type).

**Lifecycle overrides** (all `virtual`, all no-op by default):

| Method | When |
|---|---|
| `OnLoadAsync(ct)` | Once, after construction |
| `OnConnectedAsync(ct)` | Each IRC connection |
| `OnDisconnectedAsync()` | Connection lost (IChannel/IUser refs are stale) |
| `OnUnloadAsync()` | Shutdown, before DI disposal |
| `HandleEventAsync(evt, ct)` | Each event (sequential, never concurrent) |
| `FilterHandlerAsync(invocation, ct)` | Before each handler — return `false` to skip |

---

## 3. Handler Attributes

### `[OnCommand("name")]` → `Task Handler(CommandContext ctx, CancellationToken ct)`

Fires when a message starts with `[prefix][command]`. Command matching is
case-insensitive. AllowMultiple — same method can handle multiple commands.

| Property | Type | Default | Effect |
|---|---|---|---|
| `Prefix` | `string?` | bot's `CommandPrefix` | Override the command prefix |
| `ChannelOnly` | `bool` | `false` | Skip DMs |
| `DirectOnly` | `bool` | `false` | Skip channel messages |
| `Channel` | `string?` | `null` | Only fire in this channel (case-insensitive) |

### `[OnRegex("pattern")]` → `Task Handler(RegexMatchContext ctx, CancellationToken ct)`

Fires when message text (IRC formatting stripped) matches the pattern. Same
filter properties as `[OnCommand]` minus `Prefix`, plus:

| Property | Type | Default | Effect |
|---|---|---|---|
| `Options` | `RegexOptions` | `None` | Additional regex options (`Compiled` is always added) |

### `[OnEvent]` → `Task Handler(TEvent evt, CancellationToken ct)`

Event type is inferred from the first parameter (must extend `MarvEvent`).

### `[OnRawMessage("COMMAND")]` → `Task Handler(IrcMessage msg, CancellationToken ct)`

Fires for raw IRC messages with matching command. Use for protocol-level
handling not covered by typed events. VERSION, PING, TIME are handled
internally.

### `[OnInterval(Seconds = 30)]` or `[OnInterval(Minutes = 5)]`

→ `Task Handler(CancellationToken ct)`

Fires on a timer while connected. Defaults to 1 minute if neither is set.

> **Note:** All handler methods may omit the `CancellationToken` parameter.

---

## 4. Context Types

### HandlerContext (`Marv.Core.Plugin`) — abstract base

Both `CommandContext` and `RegexMatchContext` extend `HandlerContext`,
which provides the shared properties. Filter evaluators can pattern-match
on `HandlerContext` instead of switching on each concrete type:

```csharp
var sender = (invocation.Context as HandlerContext)?.Sender;
```

| Property | Type | Description |
|---|---|---|
| `Channel` | `IChannel?` | `null` for DMs |
| `Sender` | `IUser` | Who sent it |
| `IsDirect` | `bool` | `true` when `Channel` is `null` |
| `RawMessage` | `IrcMessage` | Underlying IRC message |
| `Bot` | `IBot` | Bot instance |
| `ReplyAsync(text, ct)` | `Task` | Reply in context (channel or DM) |

### CommandContext (`Marv.Core.Plugin`) extends HandlerContext

| Property | Type | Description |
|---|---|---|
| `Command` | `string` | Matched command (without prefix) |
| `Args` | `IReadOnlyList<string>` | Arguments split by whitespace |
| `ArgString` | `string` | Raw argument text |

### RegexMatchContext (`Marv.Core.Plugin`) extends HandlerContext

| Property | Type | Description |
|---|---|---|
| `Match` | `System.Text.RegularExpressions.Match` | The regex match result |

---

## 5. IBot

All `Send*Async` methods are thread-safe.

| Method | Description |
|---|---|
| `SendMessageAsync(target, text, ct)` | PRIVMSG |
| `SendNoticeAsync(target, text, ct)` | NOTICE |
| `SendActionAsync(target, text, ct)` | CTCP ACTION (/me) |
| `SendRawAsync(IrcMessage, ct)` | Raw IRC message |
| `JoinAsync(channel, key?, ct)` | Join with optional key |
| `JoinMultipleAsync(channels, ct)` | Batched JOIN (auto-splits) |
| `PartAsync(channel, reason?, ct)` | Part with optional reason |
| `KickAsync(channel, nick, reason?, ct)` | Kick user from channel |
| `SetTopicAsync(channel, topic, ct)` | Set channel topic |
| `InviteAsync(nick, channel, ct)` | Invite user to channel |
| `SetModeAsync(target, modeString, ct)` | Set mode (channel or user) |
| `SetModeAsync(target, modeString, param, ct)` | Set mode with parameter |
| `GiveOpAsync(channel, nick, ct)` | Give +o |
| `RemoveOpAsync(channel, nick, ct)` | Remove -o |
| `GiveVoiceAsync(channel, nick, ct)` | Give +v |
| `RemoveVoiceAsync(channel, nick, ct)` | Remove -v |
| `ChangeNickAsync(newNick, ct)` | Change bot's nick |
| `SendAndAwaitAsync(IrcMessage, ct)` | Send + wait for correlated response |

| Property | Type | Description |
|---|---|---|
| `Self` | `IUser` | Bot's own identity |
| `CommandPrefix` | `string` | Configured prefix (e.g. `"!"`) |
| `CaseComparer` | `IEqualityComparer<string>` | Server's IRC case mapping comparer |
| `Channels` | `IReadOnlyDictionary<string, IChannel>` | By case-mapped name |
| `Users` | `IReadOnlyDictionary<string, IUser>` | By case-mapped nick |
| `ServerInfo` | `IServerInfo` | ISUPPORT configuration |
| `Capabilities` | `ICapabilityManager` | IRCv3 capability state |

---

## 6. Event Types

All events extend `MarvEvent` which carries: `Timestamp`, `RawMessage`,
`MessageId?`, `BatchId?`.

### Connection

| Event | Key Properties | When |
|---|---|---|
| `ConnectedEvent` | — | IRC registration complete (001) |
| `ReadyEvent` | — | Auth done, about to join channels |
| `DisconnectedEvent` | — | Connection lost/closed |
| `CapabilitiesChangedEvent` | — | Runtime cap-notify change |

### Message

| Event | Key Properties | When |
|---|---|---|
| `MessageEvent` | `Channel?`, `Sender`, `Text`, `IsDirect`, `ReplyTo?` | PRIVMSG |
| `NoticeEvent` | `Channel?`, `Sender`, `Text`, `IsDirect` | NOTICE |
| `ActionEvent` | `Channel?`, `Sender`, `Text`, `IsDirect` | CTCP ACTION |
| `CtcpEvent` | `Sender`, `Command`, `Args?`, `IsDirect` | Unhandled CTCP |

### User

| Event | Key Properties | When |
|---|---|---|
| `UserJoinedEvent` | `Channel`, `User`, `Account?` | JOIN |
| `UserPartedEvent` | `Channel`, `User`, `Reason?` | PART |
| `UserKickedEvent` | `Channel`, `Kicker`, `Kicked`, `Reason?` | KICK |
| `UserQuitEvent` | `User`, `Reason?`, `AffectedChannels` | QUIT |
| `NickChangedEvent` | `User`, `OldNick`, `NewNick` | NICK |
| `AccountChangedEvent` | `User`, `OldAccount?`, `NewAccount?` | Account change |
| `AwayChangedEvent` | `User`, `IsAway`, `Message?` | Away status change |
| `HostChangedEvent` | `User`, `OldHost`, `NewHost` | Host change (cloaking) |

### Channel

| Event | Key Properties | When |
|---|---|---|
| `TopicChangedEvent` | `Channel`, `SetBy`, `NewTopic` | TOPIC |
| `ModeChangedEvent` | `Channel`, `SetBy`, `Changes` (list of `ModeChange`) | MODE |
| `InviteReceivedEvent` | `Channel` (string), `InvitedBy` | INVITE |

`ModeChange`: `IsSet` (bool), `Mode` (char), `Parameter?` (string).

### Batch

| Event | Key Properties | When |
|---|---|---|
| `BatchStartEvent` | `BatchRefTag`, `Type`, `Parameters` | BATCH open |
| `BatchEndEvent` | `BatchRefTag` | BATCH close |

### Raw

| Event | Key Properties | When |
|---|---|---|
| `RawMessageEvent` | (inherits `RawMessage` from base) | Every inbound message |

---

## 7. IrcColor & IrcFormat

**Wrap-and-reset** (self-contained, safe):

```csharp
IrcFormat.Bold("important") + " and " + IrcFormat.Color("red text", IrcColor.Red);
```

Methods: `Bold`, `Italic`, `Underline`, `Strikethrough`, `Monospace`,
`Reverse`, `Color(text, fg)`, `Color(text, fg, bg)`.

**Stateful** (for complex formatting — you manage resets):

```csharp
$"{IrcColor.Cyan.On(IrcColor.Black)}[{IrcColor.Orange} status {IrcColor.Cyan}]{IrcFormat.Reset} ok"
```

Named colors: `White`, `Black`, `Blue`, `Green`, `Red`, `Brown`, `Purple`,
`Orange`, `Yellow`, `LightGreen`, `Cyan`, `LightCyan`, `LightBlue`, `Pink`,
`Grey`, `LightGrey`, `Default`. Extended colors via `new IrcColor(16..98)`.

`IrcFormat.Strip(text)` removes all formatting codes.

---

## 8. Configuration

1. Create a config class with `[PluginConfig]`:

```csharp
[PluginConfig(Section = "Greet")]
public record GreetConfig
{
    public string Message { get; init; } = "Welcome, {nick}!";
    public bool Enabled { get; init; } = true;
}
```

2. Inject `IOptions<T>` in your plugin constructor:

```csharp
public GreetExamplePlugin(IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory,
    IOptions<GreetConfig> config)
    : base(bot, activator, loggerFactory)
{
    _config = config.Value;
}
```

Config binds to a root-level section in the configuration file (e.g. `"Greet"`).

---

## 9. Services

**Providing a service:**

```csharp
[ProvidesService(typeof(IMyService))]
public class MyServicePlugin : MarvPlugin
{
    public MyServicePlugin(IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory)
        : base(bot, activator, loggerFactory) { }

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IMyService, MyServiceImpl>();
    }
}
```

**Consuming a service** (use `= null` for optional):

```csharp
public MyConsumerPlugin(IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory,
    IMyService? svc = null)
    : base(bot, activator, loggerFactory) { _svc = svc; }
```

Use `[DependsOn(typeof(OtherPlugin))]` for explicit load ordering without a
service relationship.

---

## 10. Handler Groups

Split handler methods into separate classes to organize large plugins:

```csharp
[HandlerGroup]
public class AdminHandlers
{
    private readonly IBot _bot;
    public AdminHandlers(IBot bot) { _bot = bot; }

    [OnCommand("status", ChannelOnly = true)]
    public async Task HandleStatus(CommandContext ctx, CancellationToken ct)
    {
        await ctx.ReplyAsync($"Channels: {_bot.Channels.Count}", ct);
    }
}
```

Handler groups are discovered automatically in the plugin's assembly.
Constructor parameters are resolved from DI via `IPluginActivator`. Handler
group methods must be `public`. Groups support optional lifecycle methods:
`OnLoadAsync(ct)`, `OnConnectedAsync(ct)`, `OnDisconnectedAsync()`,
`OnUnloadAsync()`.

---

## 11. Handler Filters

Declarative cross-cutting logic (auth, rate limits, auditing) via attributes:

```csharp
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireAccountAttribute : Attribute, IFilteringAttribute
{
    public Type EvaluatorType => typeof(RequireAccountEvaluator);
}

public class RequireAccountEvaluator : FilterEvaluator<RequireAccountAttribute>
{
    protected override ValueTask<FilterResult> EvaluateAsync(
        RequireAccountAttribute attribute, HandlerInvocation invocation,
        IBot bot, CancellationToken ct)
    {
        if (invocation.Context is HandlerContext ctx && ctx.Sender.Account is null)
        {
            _ = ctx.ReplyAsync("You must be logged in.", ct);
            return ValueTask.FromResult(FilterResult.Denied);
        }
        return ValueTask.FromResult(FilterResult.Allowed);
    }
}
```

Apply to any handler: `[RequireAccount] [OnCommand("admin")] private async Task ...`

**HandlerInvocation** fields: `Method`, `Target`, `Type` (Command/Regex/Event/
RawMessage/Interval), `Context` (the context object or `null` for intervals),
`Attributes` (pre-cached).

Override `FilterHandlerAsync` in your plugin for per-plugin filtering logic.

---

## 12. Testing (Marv.Testing)

**Builders** reduce context setup to 2–3 lines:

```csharp
var ctx = CommandContextBuilder.Create("hello", "world")
    .InChannel("#test").From("alice").Build();

var ctx2 = RegexMatchContextBuilder.Create(@"hello (\w+)", "hello world")
    .InChannel("#test").From("alice").Build();

var evt = EventBuilder<MessageEvent>.Create(raw => new MessageEvent
{
    Sender = MockUser.Create("alice"),
    Text = "hello",
    RawMessage = raw
}).Build();
```

**Mock factories:** `MockBot.Create(nick, prefix)`, `MockUser.Create(nick, account)`,
`MockChannel.Create(name)`, `DummyIrcMessage.Privmsg` / `.Notice` / `.Empty` /
`.PrivmsgFrom(nick, target, text)`.

**PluginTestHarness** wires up DI with mocked dependencies:

```csharp
var harness = PluginTestHarness<HelloPlugin>.Create();
await harness.LoadAsync();
await harness.HandleEventAsync(evt);
// harness.Plugin — the plugin instance
// harness.Bot — the mock IBot (use NSubstitute .Received() assertions)
```

Pass `configureServices:` to register `IOptions<T>` or inter-plugin service
mocks. Pass `bot:` to provide a custom `IBot` mock.

---

## 13. Available Services

**Always available (registered by Marv host):**

| Service | Description |
|---|---|
| `IBot` | Bot instance |
| `IPluginActivator` | Creates instances with DI resolution |
| `ILoggerFactory` / `ILogger<T>` | Logging |
| `IOptions<T>` | Config for `[PluginConfig]` types |
| `IServerInfo` | ISUPPORT configuration |
| `ICapabilityManager` | IRCv3 capability state |
| `IHttpClientFactory` | HTTP clients (no extra NuGet needed) |
| `IHostApplicationLifetime` | App shutdown coordination |
| `IConfiguration` | Raw configuration access |

Services registered by plugins via `[ProvidesService]` + `ConfigureServices`
are available to dependent plugins in load order (see §9).

---

## 14. Platform Types Quick Reference

**IUser:** `Nick`, `User?`, `Host?`, `Account?`, `RealName?`, `IsAway`,
`AwayMessage?`, `IsBot`, `Channels`, `Hostmask`.

**IChannel:** `Name`, `Topic?`, `TopicSetBy?`, `TopicSetAt?`, `Modes`,
`Members`, `CreatedAt?`, `GetPrefixes(nick)`, `GetJoinTime(nick)`,
`HasMember(nick)`, `IsOp(nick)`, `IsVoiced(nick)`.

**IServerInfo:** `NetworkName?`, `CaseMapping`, `ChannelModes`, `Prefix`,
`MaxChannels?`, `MaxNickLength?`, `MaxTopicLength?`, `MaxMessageLength`,
`ChannelTypes`, `Motd?`, `Supports(token)`, `GetValue(token)`.

**ICapabilityManager:** `IsNegotiated(cap)`, `IsAvailable(cap)`,
`NegotiatedCapabilities`, `AvailableCapabilities`, `CapabilitiesChanged` event.

**IrcMessage:** `Tags`, `Source?`, `Command`, `Parameters`.
