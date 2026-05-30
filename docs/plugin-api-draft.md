# Plugin API Draft

This document describes the types, patterns, and APIs that plugin
authors work with when building Marv plugins.

---

## Core Types for Plugin Authors

A plugin author's day-to-day involves these types from `Marv.Core`:

| Type | Purpose |
|---|---|
| `MarvPlugin` | Base class all plugins inherit from |
| `IBot` | Facade for sending messages and querying state |
| `IChannel`, `IUser`, `IChannelMember` | Read-only state models |
| `ICapabilityManager` | Check negotiated capabilities |
| `IServerInfo` | Server configuration (ISUPPORT) |
| Event classes (`ChannelMessageEvent`, etc.) | Typed event payloads |
| Attributes (`[OnEvent]`, `[OnCommand]`, etc.) | Declare event interest |
| `[ProvidesService]`, `[ConsumesService]`, `[DependsOn]` | Service/dependency declarations |

---

## Plugin Lifecycle

### MarvPlugin Base Class

```csharp
public abstract class MarvPlugin
{
    /// Called once after the plugin is constructed and all services
    /// are available. Use for one-time initialization.
    public virtual Task OnLoadAsync(CancellationToken ct) => Task.CompletedTask;

    /// Called each time the bot establishes an IRC connection.
    public virtual Task OnConnectedAsync(CancellationToken ct) => Task.CompletedTask;

    /// Called when the IRC connection is lost. The bot may reconnect.
    public virtual Task OnDisconnectedAsync() => Task.CompletedTask;

    /// Called once during shutdown, before the DI container is disposed.
    /// Use for cleanup (unsubscribe, flush, close handles).
    public virtual Task OnUnloadAsync() => Task.CompletedTask;
}
```

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
method must be an instance method on the plugin class and return
`Task`.

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
├── ReplyAsync(string): Task        (responds in-context)
└── RawMessage: IrcMessage
```

The command prefix (e.g., `!` or `.`) is configured per-bot, not
per-plugin.

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

Interval handlers are not tied to any message — they run on the
message processor task during idle periods.

---

## Registering a Service

A plugin provides a service for other plugins by:

1. Defining the service interface
2. Implementing it
3. Registering it during `ConfigureServices`
4. Declaring it via `[ProvidesService]`

### Example: Auth Service

**Interface** (in the plugin assembly or a separate contracts assembly):

```csharp
/// Determines whether a user is authorized to perform an action.
public interface IAuthorizationService
{
    /// Returns true if the user is authorized for the given permission.
    Task<bool> IsAuthorizedAsync(IUser user, string permission, CancellationToken ct);
}
```

**Plugin class**:

```csharp
[ProvidesService(typeof(IAuthorizationService))]
public class AuthPlugin : MarvPlugin
{
    private readonly IOptions<AuthPluginConfig> _config;
    private readonly IBot _bot;
    private readonly AuthorizationService _authService;

    public AuthPlugin(IBot bot, IOptions<AuthPluginConfig> config)
    {
        _bot = bot;
        _config = config;
        _authService = new AuthorizationService(config);
    }

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationService>(sp =>
        {
            var plugin = sp.GetRequiredService<AuthPlugin>();
            return plugin._authService;
        });

        services.AddOptions<AuthPluginConfig>()
            .BindConfiguration("Plugins:Auth");
    }
}
```

**Configuration** (`marv.toml`):

```toml
[Plugins.Auth]
AdminAccounts = ["admin1", "admin2"]
```

---

## Declaring and Resolving Dependencies

### Required Dependency

```csharp
[ConsumesService(typeof(IAuthorizationService))]
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

```csharp
[ConsumesService(typeof(IAuthorizationService), Required = false)]
public class GreetPlugin : MarvPlugin
{
    private readonly IAuthorizationService? _auth;

    public GreetPlugin(IBot bot, IAuthorizationService? auth = null)
    {
        _auth = auth;
    }

    [OnEvent]
    public async Task HandleJoin(UserJoinedEvent e, CancellationToken ct)
    {
        // If auth is available, only greet authorized users.
        if (_auth is not null &&
            !await _auth.IsAuthorizedAsync(e.User, "greet.receive", ct))
        {
            return;
        }

        await e.Channel.SendMessageAsync($"Welcome, {e.User.Nick}!");
    }
}
```

### Direct Plugin Dependency

For cases where a plugin depends on another plugin directly (not just
a service interface):

```csharp
[DependsOn(typeof(AuthPlugin))]
public class AdminPlugin : MarvPlugin { ... }
```

This ensures `AuthPlugin` loads before `AdminPlugin` but does not
imply any service consumption. Prefer `[ConsumesService]` over
`[DependsOn]` when the dependency is on a service interface — it
provides better decoupling.

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

    // --- State Queries (call only from event handlers) ---
    IChannel? GetChannel(string name);
    IReadOnlyCollection<IChannel> Channels { get; }
    IUser? GetUser(string nick);

    // --- Server Info ---
    IServerInfo ServerInfo { get; }
    ICapabilityManager Capabilities { get; }

    // --- Advanced ---
    Task<IReadOnlyList<IrcMessage>> SendAndAwaitAsync(
        IrcMessage message, CancellationToken ct);
}
```

**Thread safety**: `SendMessageAsync`, `SendNoticeAsync`,
`SendActionAsync`, and `SendRawAsync` are thread-safe — they can be
called from any context (background tasks, timers, etc.). The state
query methods (`GetChannel`, `GetUser`, `Channels`) are only safe to
call from event handlers running on the message processor task.

### Convenience Methods on Event Objects

Event objects provide contextual shortcuts so plugins don't need to
thread `IBot` through everything:

```csharp
// On ChannelMessageEvent:
await e.ReplyAsync("response");           // sends to the channel
await e.ReplyToSenderAsync("response");   // sends a private message

// On UserJoinedEvent:
await e.Channel.SendMessageAsync("Welcome!");
```

These delegate to `IBot` internally.

---

## Complete Examples

### Simplest Possible Plugin

A plugin that responds to `!ping` with `pong`:

```csharp
/// A minimal plugin that responds to the !ping command.
public class PingPlugin : MarvPlugin
{
    [OnCommand("ping")]
    public async Task HandlePing(CommandContext ctx, CancellationToken ct)
    {
        await ctx.ReplyAsync("pong");
    }
}
```

That's it. No configuration, no services, no lifecycle hooks. The
plugin is discovered by assembly scanning, loaded into the DI
container, and its `[OnCommand]` handler is automatically registered.

### Plugin with Configuration

A greeting plugin with configurable messages:

```csharp
public record GreetPluginConfig
{
    public string GreetMessage { get; init; } = "Welcome, {nick}!";
    public bool GreetOnJoin { get; init; } = true;
}

public class GreetPlugin : MarvPlugin
{
    private readonly IOptions<GreetPluginConfig> _config;

    public GreetPlugin(IOptions<GreetPluginConfig> config)
    {
        _config = config;
    }

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions<GreetPluginConfig>()
            .BindConfiguration("Plugins:Greet");
    }

    [OnEvent]
    public async Task HandleJoin(UserJoinedEvent e, CancellationToken ct)
    {
        if (!_config.Value.GreetOnJoin)
            return;

        var message = _config.Value.GreetMessage.Replace("{nick}", e.User.Nick);
        await e.Channel.SendMessageAsync(message);
    }
}
```

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

public record AuthPluginConfig
{
    public List<string> AdminAccounts { get; init; } = [];
}

[ProvidesService(typeof(IAuthorizationService))]
public class AuthPlugin : MarvPlugin
{
    private readonly AuthPluginConfig _config;

    public AuthPlugin(IOptions<AuthPluginConfig> config)
    {
        _config = config.Value;
    }

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions<AuthPluginConfig>()
            .BindConfiguration("Plugins:Auth");

        services.AddSingleton<IAuthorizationService>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<AuthPluginConfig>>();
            return new AccountBasedAuthService(config.Value);
        });
    }
}

internal class AccountBasedAuthService : IAuthorizationService
{
    private readonly AuthPluginConfig _config;

    public AccountBasedAuthService(AuthPluginConfig config)
    {
        _config = config;
    }

    public Task<bool> IsAuthorizedAsync(
        IUser user, string permission, CancellationToken ct)
    {
        var isAdmin = user.Account is not null &&
            _config.AdminAccounts.Contains(user.Account);
        return Task.FromResult(isAdmin);
    }
}

// --- Moderation Plugin (consumes auth) ---

[ConsumesService(typeof(IAuthorizationService))]
public class ModerationPlugin : MarvPlugin
{
    private readonly IBot _bot;
    private readonly IAuthorizationService _auth;

    public ModerationPlugin(IBot bot, IAuthorizationService auth)
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

        if (ctx.Args.Count == 0)
        {
            await ctx.ReplyAsync("Usage: !kick <nick> [reason]");
            return;
        }

        var targetNick = ctx.Args[0];
        var reason = ctx.Args.Count > 1
            ? string.Join(' ', ctx.Args.Skip(1))
            : "Kicked by moderator";

        await _bot.SendRawAsync(
            new IrcMessage("KICK", [ctx.Channel!.Name, targetNick, reason]),
            ct);
    }
}
```

---

## Plugin Project Structure

A typical plugin project on disk:

```
Marv.Plugins.MyPlugin/
├── Marv.Plugins.MyPlugin.csproj   (references Marv.Core)
├── MyPlugin.cs                    (plugin class)
├── MyPluginConfig.cs              (configuration record)
├── Services/
│   ├── IMyService.cs              (service interface)
│   └── MyService.cs               (service implementation)
└── Handlers/
    └── ...                        (if handlers are complex enough
                                    to warrant separate files)
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

`Marv.Core` provides test fakes (`CommandContextFake`,
`ChannelFake`, `UserFake`, `BotFake`) so plugin authors can unit test
handlers without mocking infrastructure.
