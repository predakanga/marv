# Plugin API Draft

This document describes the types, patterns, and APIs that plugin
authors work with when building Marv plugins.

---

## Core Types for Plugin Authors

A plugin author's day-to-day involves these types from `Marv.Core`:

| Type | Purpose |
|---|---|
| `MarvPlugin` | Base class for all plugins |
| `IBot` | Facade for sending messages and querying state |
| `IChannel`, `IUser` | Read-only state models |
| `ICapabilityManager` | Check negotiated capabilities |
| `IServerInfo` | Server configuration (ISUPPORT) |
| `[PluginConfig]` | Attribute for configuration classes |
| Event classes (`MessageEvent`, etc.) | Typed event payloads |
| Attributes (`[OnEvent]`, `[OnCommand]`, etc.) | Declare event interest |
| `[ProvidesService]` | Declare a service this plugin provides |
| `[DependsOn]`, `[OptionalService]` | Dependency declarations |

---

## Plugin Lifecycle

### MarvPlugin Base Classes

```csharp
public abstract class MarvPlugin
{
    /// The bot instance, injected automatically by the core.
    protected IBot Bot { get; }

    /// Called once after the plugin is constructed and all services
    /// are available. Use for one-time initialization.
    public virtual Task OnLoadAsync(CancellationToken ct) => Task.CompletedTask;

    /// Called each time the bot establishes an IRC connection.
    public virtual Task OnConnectedAsync(CancellationToken ct) => Task.CompletedTask;

    /// Called when the IRC connection is lost. The bot may reconnect.
    /// Any cached IChannel/IUser references are stale after this call.
    public virtual Task OnDisconnectedAsync() => Task.CompletedTask;

    /// Called once during shutdown, before the DI container is disposed.
    /// Use for cleanup (unsubscribe, flush, close handles).
    public virtual Task OnUnloadAsync() => Task.CompletedTask;
}
```

`IBot` is injected into the base class automatically — plugins do not
need to accept it as a constructor parameter (though they may still
inject additional services).

Plugins that need configuration declare a separate configuration
class tagged with `[PluginConfig(Section = "Name")]`. The plugin
loader discovers these during assembly scanning and registers
`IOptions<TConfig>` bound to the `Plugins:{Section}` configuration
section — no `ConfigureServices` boilerplate needed. Plugins access
their configuration via constructor injection of `IOptions<TConfig>`.

### Lifecycle Order

1. Constructor (DI injects config and services)
2. `OnLoadAsync` (in dependency order)
3. `OnConnectedAsync` (in dependency order, after each connect)
4. Event handlers run during the connection
5. `OnDisconnectedAsync` (reverse dependency order)
6. Steps 3–5 repeat on reconnection
7. `OnUnloadAsync` (reverse dependency order, once at shutdown)

---

## Registering Interest in Events

Plugins declare event handlers using attributes on methods. The
method must be an instance method on the plugin class (or a handler
group class — see below) and return `Task`. Handler methods do not
need to be public — the dispatch is performed from within the
`MarvPlugin` base class using reflection, so `protected` and
`private` methods are accessible.

If multiple handler methods on the same plugin (or its handler
groups) match the same event, they are all called consecutively but
in an undefined order.

### `[OnEvent]` — Subscribe to any typed event

```csharp
[OnEvent]
public Task HandleJoin(UserJoinedEvent e, CancellationToken ct)
{
    // Runs when any user joins any channel the bot is in.
    return Task.CompletedTask;
}
```

The event type is inferred from the method parameter. Only one event
parameter is allowed.

### `[OnCommand]` — Respond to a user command

```csharp
[OnCommand("hello")]
public async Task HandleHello(CommandContext ctx, CancellationToken ct)
{
    await ctx.ReplyAsync($"Hello, {ctx.Sender.Nick}!");
}
```

`CommandContext` extends the message event with parsed command
arguments:

```
CommandContext
├── Command: string                 ("hello")
├── Args: IReadOnlyList<string>     (remaining words)
├── ArgString: string               (remaining text, unparsed)
├── Channel: IChannel?              (null for private messages)
├── Sender: IUser
├── IsDirect: bool
├── ReplyAsync(string): Task        (responds in-context)
└── RawMessage: IrcMessage
```

The command prefix (e.g., `!` or `.`) is configured per-bot, not
per-plugin.

### `[OnRegex]` — Match messages against a regular expression

```csharp
[OnRegex(@"https?://\S+")]
public async Task HandleUrl(RegexMatchContext ctx, CancellationToken ct)
{
    var url = ctx.Match.Value;
    await ctx.ReplyAsync($"I see a URL: {url}");
}
```

`RegexMatchContext` extends the message event with the regex match:

```
RegexMatchContext
├── Match: Match                    (System.Text.RegularExpressions.Match)
├── Channel: IChannel?
├── Sender: IUser
├── IsDirect: bool
├── ReplyAsync(string): Task
└── RawMessage: IrcMessage
```

The regex is matched against the full message text. If there are
multiple matches, the handler is called once with the first match.
Named capture groups are accessible via `ctx.Match.Groups["name"]`.

### `[OnRawMessage]` — Subscribe to raw IRC commands

```csharp
[OnRawMessage("INVITE")]
public async Task HandleInvite(IrcMessage message, CancellationToken ct)
{
    // Direct access to the raw protocol message.
}
```

This is for protocol-level handling that the typed events don't cover.

### `[OnInterval]` — Periodic tasks

```csharp
[OnInterval(minutes: 5)]
public async Task CheckSomething(CancellationToken ct)
{
    // Runs every 5 minutes while connected.
}
```

Interval handlers run on the plugin's own task.

---

## Handler Groups

For plugins with many handlers, related handlers can be organized
into separate classes using `[HandlerGroup]`. A handler group is a
class whose methods are treated as if they were methods on the plugin
itself.

```csharp
public class MyPlugin : MarvPlugin
{
    // Plugin-level lifecycle, minimal handler code here
}

[HandlerGroup(typeof(MyPlugin))]
public class MyAdminHandlers
{
    private readonly IBot _bot;
    private readonly IAuthorizationService _auth;

    // Constructor-injected from the DI container, just like plugins
    public MyAdminHandlers(IBot bot, IAuthorizationService auth)
    {
        _bot = bot;
        _auth = auth;
    }

    [OnCommand("kick")]
    public async Task HandleKick(CommandContext ctx, CancellationToken ct)
    {
        if (!await _auth.IsAuthorizedAsync(ctx.Sender, "mod.kick", ct))
        {
            await ctx.ReplyAsync("Permission denied.");
            return;
        }
        // ... perform the kick
    }

    [OnCommand("ban")]
    public async Task HandleBan(CommandContext ctx, CancellationToken ct)
    {
        // ...
    }
}
```

Handler groups are:

- Discovered automatically by scanning the plugin's assembly for
  classes with `[HandlerGroup(typeof(MyPlugin))]`
- Constructed via DI — they can inject any service the plugin can
- Registered as singletons in the container
- Their event handlers run on the owning plugin's task, sequentially
  with the plugin's own handlers (order between handlers from
  different groups is undefined)
- May define lifecycle methods (`OnLoadAsync`, `OnConnectedAsync`,
  `OnDisconnectedAsync`, `OnUnloadAsync`) which are called alongside
  the owning plugin's lifecycle methods
- Useful for separating concerns without creating multiple plugins
  (which would each get their own task and independent event ordering)

---

## Registering a Service

A plugin provides a service for other plugins by declaring
`[ProvidesService]` on its class and registering the implementation
in a static `ConfigureServices` method.

### Example: Auth Service

**Interface** (in the plugin assembly or a separate contracts assembly):

```csharp
public interface IAuthorizationService
{
    Task<bool> IsAuthorizedAsync(IUser user, string permission, CancellationToken ct);
}
```

**Plugin class**:

```csharp
[ProvidesService(typeof(IAuthorizationService))]
public class AuthPlugin : MarvPlugin
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationService, AccountBasedAuthService>();
    }
}
```

The `[ProvidesService]` attribute tells the dependency sorter that
`AuthPlugin` provides `IAuthorizationService`. Other plugins that
inject `IAuthorizationService` via their constructor are automatically
sorted to load after `AuthPlugin`.

---

## Declaring and Resolving Dependencies

### Required Dependency

Simply inject the service via the constructor:

```csharp
public class ModerationPlugin : MarvPlugin
{
    private readonly IAuthorizationService _auth;

    public ModerationPlugin(IBot bot, IAuthorizationService auth)
    {
        _auth = auth;
    }

    [OnCommand("ban")]
    public async Task HandleBan(CommandContext ctx, CancellationToken ct)
    {
        if (!await _auth.IsAuthorizedAsync(ctx.Sender, "moderation.ban", ct))
        {
            await ctx.ReplyAsync("You are not authorized to use this command.");
            return;
        }
        // ... perform the ban
    }
}
```

If no plugin provides `IAuthorizationService`, `ModerationPlugin`
fails to load at startup with a clear error.

### Optional Dependency

Mark the parameter with `[OptionalService]` and make it nullable
with a default of `null`:

```csharp
public class GreetPlugin : MarvPlugin
{
    private readonly GreetPluginConfig _config;
    private readonly IAuthorizationService? _auth;

    public GreetPlugin(
        IOptions<GreetPluginConfig> config,
        [OptionalService] IAuthorizationService? auth = null)
    {
        _config = config.Value;
        _auth = auth;
    }

    [OnEvent]
    public async Task HandleJoin(UserJoinedEvent e, CancellationToken ct)
    {
        if (_auth is not null &&
            !await _auth.IsAuthorizedAsync(e.User, "greet.receive", ct))
        {
            return;
        }

        var message = _config.GreetMessage.Replace("{nick}", e.User.Nick);
        await Bot.SendMessageAsync(e.Channel.Name, message, ct);
    }
}
```

### Direct Plugin Dependency

For cases where a plugin depends on another plugin directly (for load
ordering, not service consumption):

```csharp
[DependsOn(typeof(AuthPlugin))]
public class AdminPlugin : MarvPlugin { ... }
```

This ensures `AuthPlugin` loads before `AdminPlugin`.

---

## What the Bot Exposes to Plugins

### `IBot` Interface

```csharp
public interface IBot
{
    // --- Identity ---
    IUser Self { get; }

    // --- Sending Messages ---
    Task SendMessageAsync(string target, string text, CancellationToken ct);
    Task SendNoticeAsync(string target, string text, CancellationToken ct);
    Task SendActionAsync(string target, string text, CancellationToken ct);
    Task SendRawAsync(IrcMessage message, CancellationToken ct);

    // --- Channel Operations ---
    Task JoinAsync(string channel, string? key, CancellationToken ct);
    Task PartAsync(string channel, string? reason, CancellationToken ct);

    // --- State ---
    IReadOnlyDictionary<string, IChannel> Channels { get; }
    IReadOnlyDictionary<string, IUser> Users { get; }

    // --- Server Info ---
    IServerInfo ServerInfo { get; }
    ICapabilityManager Capabilities { get; }

    // --- Advanced ---
    Task<IReadOnlyList<IrcMessage>> SendAndAwaitAsync(
        IrcMessage message, CancellationToken ct);
}
```

**`Channels`**: Dictionary keyed by case-mapped channel name. Use the
indexer for O(1) lookup by name (e.g., `bot.Channels["#general"]`).

**`Users`**: Dictionary keyed by case-mapped nick. Contains all users
the bot is aware of (through shared channels or direct interaction).

**`SendAndAwaitAsync`**: Sends an IRC command and waits for the
server's correlated response. Uses the `labeled-response` IRCv3
capability to tag the outbound message with a unique label, then
collects all inbound messages bearing that label until the server
signals completion. Useful for commands like WHO, WHOIS, and LIST
where the response spans multiple messages. Falls back to
timeout-based correlation if `labeled-response` is not negotiated.

**Thread safety**: All `Send*Async` methods, `JoinAsync`, `PartAsync`,
and `SendAndAwaitAsync` are thread-safe — they can be called from any
context (plugin tasks, background tasks, timers). The `Channels` and
`Users` dictionaries are read-safe from any plugin task (they use
concurrent-read-safe data structures updated only by the message
processor).

### Convenience Methods on Event Objects

Event objects provide contextual shortcuts:

```csharp
// On MessageEvent:
await e.ReplyAsync("response");    // sends to channel or DM, matching context

// On UserJoinedEvent:
await Bot.SendMessageAsync(e.Channel.Name, "Welcome!", ct);
```

`ReplyAsync` delegates to `IBot` internally, choosing the correct
target (channel or sender nick) based on context.

---

## Complete Examples

### Simplest Possible Plugin

A plugin that responds to `!ping` with `pong`:

```csharp
public class PingPlugin : MarvPlugin
{
    [OnCommand("ping")]
    public async Task HandlePing(CommandContext ctx, CancellationToken ct)
    {
        await ctx.ReplyAsync("pong");
    }
}
```

No configuration, no services, no lifecycle hooks, no
`ConfigureServices`. Discovered by assembly scanning and wired up
automatically.

### Plugin with Configuration

A greeting plugin with configurable messages:

```csharp
[PluginConfig(Section = "Greet")]
public record GreetPluginConfig
{
    public string GreetMessage { get; init; } = "Welcome, {nick}!";
    public bool GreetOnJoin { get; init; } = true;
}

public class GreetPlugin : MarvPlugin
{
    private readonly GreetPluginConfig _config;

    public GreetPlugin(IOptions<GreetPluginConfig> config)
    {
        _config = config.Value;
    }

    [OnEvent]
    public async Task HandleJoin(UserJoinedEvent e, CancellationToken ct)
    {
        if (!_config.GreetOnJoin)
            return;

        var message = _config.GreetMessage.Replace("{nick}", e.User.Nick);
        await Bot.SendMessageAsync(e.Channel.Name, message, ct);
    }
}
```

Configuration is automatically bound to `Plugins:Greet` in the
config file (from the `[PluginConfig]` attribute's `Section`). No
`ConfigureServices` needed.

### Plugin That Provides and Consumes a Service

An auth plugin providing `IAuthorizationService`, consumed by a
moderation plugin:

```csharp
// --- Contracts (could be in a separate assembly) ---

public interface IAuthorizationService
{
    Task<bool> IsAuthorizedAsync(IUser user, string permission, CancellationToken ct);
}

// --- Auth Plugin ---

[PluginConfig(Section = "Auth")]
public record AuthPluginConfig
{
    public List<string> AdminAccounts { get; init; } = [];
}

[ProvidesService(typeof(IAuthorizationService))]
public class AuthPlugin : MarvPlugin
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationService, AccountBasedAuthService>();
    }
}

internal class AccountBasedAuthService : IAuthorizationService
{
    private readonly IOptions<AuthPluginConfig> _config;

    public AccountBasedAuthService(IOptions<AuthPluginConfig> config)
    {
        _config = config;
    }

    public Task<bool> IsAuthorizedAsync(
        IUser user, string permission, CancellationToken ct)
    {
        var isAdmin = user.Account is not null &&
            _config.Value.AdminAccounts.Contains(user.Account);
        return Task.FromResult(isAdmin);
    }
}

// --- Moderation Plugin (consumes auth, required) ---

public class ModerationPlugin : MarvPlugin
{
    private readonly IAuthorizationService _auth;

    public ModerationPlugin(IAuthorizationService auth)
    {
        _auth = auth;
    }

    [OnCommand("kick")]
    public async Task HandleKick(CommandContext ctx, CancellationToken ct)
    {
        if (!await _auth.IsAuthorizedAsync(ctx.Sender, "mod.kick", ct))
        {
            await ctx.ReplyAsync("Permission denied.");
            return;
        }

        if (ctx.Args.Count == 0)
        {
            await ctx.ReplyAsync("Usage: !kick <nick> [reason]");
            return;
        }

        var targetNick = ctx.Args[0];
        var reason = ctx.Args.Count > 1
            ? string.Join(' ', ctx.Args.Skip(1))
            : "Kicked by moderator";

        await Bot.SendRawAsync(
            new IrcMessage("KICK", [ctx.Channel!.Name, targetNick, reason]),
            ct);
    }
}
```

### Plugin with Handler Groups

A moderation plugin that organizes handlers by concern:

```csharp
public class ModerationPlugin : MarvPlugin
{
    private readonly IAuthorizationService _auth;

    public ModerationPlugin(IAuthorizationService auth)
    {
        _auth = auth;
    }
}

[HandlerGroup(typeof(ModerationPlugin))]
public class KickBanHandlers
{
    private readonly IBot _bot;
    private readonly IAuthorizationService _auth;

    public KickBanHandlers(IBot bot, IAuthorizationService auth)
    {
        _bot = bot;
        _auth = auth;
    }

    [OnCommand("kick")]
    public async Task HandleKick(CommandContext ctx, CancellationToken ct) { ... }

    [OnCommand("ban")]
    public async Task HandleBan(CommandContext ctx, CancellationToken ct) { ... }

    // Handler groups can also have lifecycle methods
    public Task OnConnectedAsync(CancellationToken ct) => Task.CompletedTask;
}

[HandlerGroup(typeof(ModerationPlugin))]
public class FloodProtectionHandlers
{
    [OnEvent]
    public async Task HandleMessage(MessageEvent e, CancellationToken ct) { ... }
}
```

---

## Plugin Project Structure

A typical plugin project on disk:

```
Marv.Plugins.MyPlugin/
├── Marv.Plugins.MyPlugin.csproj   (references Marv.Core)
├── MyPlugin.cs                    (plugin class)
├── MyPluginConfig.cs              (configuration record, if needed)
├── Services/
│   ├── IMyService.cs              (service interface, if providing)
│   └── MyService.cs               (service implementation)
└── Handlers/
    ├── AdminHandlers.cs           (handler group)
    └── UserHandlers.cs            (handler group)
```

The `.csproj` references `Marv.Core` and nothing else from the Marv
solution (unless consuming another plugin's contracts assembly).

---

## Testing Plugins

Plugins are testable without a running bot:

```csharp
[Fact]
public async Task PingPlugin_responds_with_pong()
{
    var plugin = new PingPlugin();
    var ctx = CommandContextFake.Create(command: "ping", sender: "testuser");

    await plugin.HandlePing(ctx, CancellationToken.None);

    Assert.Equal("pong", ctx.Replies.Single());
}
```

`Marv.Core` provides test fakes (`CommandContextFake`, `ChannelFake`,
`UserFake`, `BotFake`) so plugin authors can unit test handlers
without mocking infrastructure.
