# Architecture

This document describes Marv's assembly structure, internal layering,
async/threading model, plugin loading, and inter-plugin service system.

---

## Assembly Structure

```
Marv.sln
├── src/
│   ├── Marv.App/            # CLI host application
│   ├── Marv.Core/           # Core library
│   └── plugins/
│       ├── Marv.Plugins.Ping/       # Example: simple ping/pong
│       ├── Marv.Plugins.Auth/       # Example: auth service provider
│       └── Marv.Plugins.Greet/      # Example: consumes auth service
├── tests/
│   ├── Marv.Core.Tests/
│   └── Marv.Plugins.Tests/
└── docs/
```

### Marv.App

The CLI host application. A thin shell that delegates to the core.

Responsibilities:

- Parse command-line arguments (using `System.CommandLine` 2.x)
- Read configuration from files, environment variables, and CLI args
  (in ascending priority order)
- Initialize logging (`Microsoft.Extensions.Logging` with console and
  file sinks)
- Call `services.AddMarv(configuration)` to delegate plugin loading,
  dependency sorting, service registration, and bot setup to the core
- Run the hosted service and handle graceful shutdown (Ctrl+C /
  SIGTERM)

The app does **not** perform plugin discovery, DI wiring, or bot
orchestration itself — that is the core's responsibility. The app
builds the `IHostBuilder`, registers configuration and logging, calls
the core's extension method, and runs.

This assembly references `Marv.Core` and the .NET hosting/DI packages.
It does not reference any plugin assemblies directly — plugins are
loaded at runtime.

### Marv.Core

The core library. Contains everything a plugin author needs to
reference at compile time, and everything the host needs to run the
bot. This is the only assembly that plugin projects reference.

Responsibilities:

- IRC message parsing and serialization
- TCP/TLS connection management
- IRCv3 capability negotiation and SASL authentication
- Protocol-level message handling (PING/PONG, CTCP VERSION/PING/TIME)
- Rate-limited message sending
- Channel/user/mode state tracking
- The `IPlugin` interface and `MarvPlugin` convenience base class
- Plugin attributes and lifecycle management
- Event types and the event dispatch system
- The `IBot` facade interface
- Plugin discovery, dependency sorting, and lifecycle management
- The `IServiceCollection` extension method (`AddMarv`) that wires
  everything together

### Plugin Assemblies

Each plugin is a separate assembly (class library) that references
`Marv.Core`. A plugin assembly contains:

- Exactly one plugin class implementing `IPlugin` (typically via
  `MarvPlugin`)
- Optional configuration record/class tagged with
  `[PluginConfig(Section = "Name")]`
- Optional service interfaces (or these may live in a separate
  contracts assembly shared between provider and consumer)
- Optional service implementations
- Optional handler group classes

Plugin assemblies are built as DLLs and placed in a known directory.
The host discovers them at startup via the configured plugin paths.

---

## Internal Layering (Marv.Core)

The core library is organized into layers, each building on the one
below it. Higher layers depend on lower layers; lower layers never
reference higher layers.

```
┌─────────────────────────────────────────┐
│            Plugin System                │  Plugin loading, lifecycle,
│   IPlugin, MarvPlugin, attributes       │  event dispatch
├─────────────────────────────────────────┤
│            Bot / Message Processor      │  IBot — high-level API
│   Send messages, query state,           │  exposed to plugins.
│   PING/PONG, CAP, state updates,        │  Protocol handling and
│   event fan-out to plugin tasks         │  state management.
├─────────────────────────────────────────┤
│            State Tracking               │  Channels, users, modes,
│   IChannelStore, IUserStore             │  ISUPPORT parameters
├─────────────────────────────────────────┤
│            Connection                   │  TCP/TLS, reconnection,
│   IIrcConnection                        │  rate limiting
├─────────────────────────────────────────┤
│            Protocol                     │  IrcMessage parsing,
│   IrcParser, IrcSerializer              │  serialization, tags,
│                                         │  case mapping
└─────────────────────────────────────────┘
```

### Protocol Layer

- `IrcMessage`: Immutable record representing a parsed IRC message
  (tags, source, command, parameters). Used for both inbound and
  outbound messages — the structure is identical (tags, command,
  parameters), with the only difference being that inbound messages
  have a source prefix and outbound messages do not. Keeping a single
  type avoids conversion overhead and lets plugins inspect/transform
  messages uniformly.
- `IrcParser`: Parses raw bytes/strings into `IrcMessage` instances.
  Handles tag value escaping, multi-space separation, empty trailing
  parameters, and source prefix parsing. Validated against
  ircdocs/parser-tests vectors.
- `IrcSerializer`: Serializes `IrcMessage` back to wire format.
- `CaseMapping`: Implements RFC 1459, strict-RFC 1459, and ASCII case
  mapping for nick/channel comparison. The active mapping is read from
  ISUPPORT and can change at runtime.

### Connection Layer

- `IIrcConnection`: Abstracts the TCP/TLS connection. Provides a
  `ChannelReader<IrcMessage>` for inbound messages and accepts
  outbound messages via a `ChannelWriter<IrcMessage>`.
- Implements rate limiting on outbound messages using a token bucket
  algorithm to prevent excess flood disconnections.
- Handles reconnection with exponential backoff.
- UTF-8 encoding with graceful handling of invalid sequences.

The connection layer is a pure transport — it does not interpret
message content. PING/PONG handling, CAP negotiation, and all other
protocol-level logic live in the message processor (bot layer) where
they have access to state and can be tested without a network
connection.

### State Tracking

- `IChannelStore`: Maintains the set of channels the bot is in, each
  with its topic, modes, and member list (including per-user prefixes
  and join times).
- `IUserStore`: Maintains known users (nick, user, host, account, away
  status). Updated from NAMES, WHO, JOIN, PART, QUIT, NICK, CHGHOST,
  ACCOUNT, AWAY, SETNAME messages.
- `IModeParser`: Parses MODE changes using the server's CHANMODES and
  PREFIX from ISUPPORT to correctly handle type A/B/C/D modes.
- All comparisons use the server's advertised CASEMAPPING.

State stores are written by the message processor task and read by
plugin tasks. `IUser` and `IChannel` objects are mutable — the
message processor updates properties in place. Only the message
processor writes, so no write contention exists.

Thread safety for concurrent reads is achieved through:

- Atomic individual property reads (reference type fields in .NET)
- `ConcurrentDictionary` for collection-valued properties, ensuring
  safe enumeration while the message processor adds/removes entries

Cross-property consistency within a single `IUser` or `IChannel` is
not guaranteed during a handler — a state change could land between
two reads. This is rare in practice, and plugins needing strict
consistency can copy values into locals at handler entry. Plugins
holding a reference to an `IUser` or `IChannel` see live updates
to that object's properties.

### Bot / Message Processor

- `IBot`: The primary interface plugins interact with. Provides
  methods for sending messages, querying channel/user state, and
  checking capability availability. See `plugin-api-draft.md` for the
  full surface.
- The message processor is the central task that reads from the
  inbound channel and:
  - Handles PING/PONG (responds immediately, not exposed to plugins)
  - Handles core CTCP queries (VERSION, PING, TIME) — responds
    automatically without exposing host information; only the bot
    version string is included. Other CTCP queries are translated
    into `CtcpEvent` for plugin handling.
  - Drives CAP negotiation and SASL authentication
  - Updates state tracking (channels, users, modes)
  - Translates raw `IrcMessage` into typed events
  - Fans out events to each plugin's individual event channel

### Plugin System

- Plugin discovery, assembly loading, dependency graph construction,
  topological sorting, and lifecycle management.
- Per-plugin event channels and tasks (see Async/Threading Model).
- Described in detail below.

---

## Async / Threading Model

Marv uses a small number of long-lived async tasks communicating via
`System.Threading.Channels`. Each plugin runs on its own dedicated
task, receiving events through its own channel.

### Task Structure

```
                                             ┌──────────────────┐
                                          ┌─►│  Plugin A Task   │
                                          │  │  Channel<Event>  │
┌──────────────┐   Channel<IrcMessage>   ┌┴───────────────────┐ │
│  Read Loop   │ ──────────────────────► │  Message           │ │
│  (network)   │   (inbound)             │  Processor         │ │
└──────────────┘                         │                    ├─┤
                                         │  - PING/PONG       │ │
                                         │  - CAP negotiation │ │  ┌──────────────────┐
                                         │  - State updates   ├─┼─►│  Plugin B Task   │
                                         │  - Event fan-out   │ │  │  Channel<Event>  │
                                         └┬───────────────────┘ │  └──────────────────┘
                                          │                     │
                                          └─►  ... more plugins

         (plugins call IBot.SendAsync from any task)
                          │
                          ▼
┌──────────────┐   Channel<IrcMessage>   ┌──────────────────┐
│  Write Loop  │ ◄────────────────────── │  Rate Limiter    │
│  (network)   │   (outbound)            │  (token bucket)  │
└──────────────┘                         └──────────────────┘
```

1. **Read Loop**: A dedicated async task reads from the TCP stream,
   parses raw lines into `IrcMessage` instances, and writes them to
   the inbound channel. This task does no processing beyond parsing.

2. **Message Processor**: A dedicated async task reads from the
   inbound channel. For each message it:
   - Handles protocol-level concerns (PING/PONG, CAP negotiation)
   - Updates state tracking (channels, users, modes)
   - Translates raw `IrcMessage` into typed events
   - Writes the event to each plugin's individual event channel

3. **Plugin Tasks**: Each plugin has its own dedicated async task and
   `Channel<MarvEvent>`. The task reads events from the channel and
   calls `plugin.HandleEventAsync(event, ct)` for each one — the
   core never calls handler methods directly. This means:
   - Each plugin processes events independently and concurrently with
     other plugins
   - Within a single plugin, `HandleEventAsync` is never called
     concurrently with itself — no concurrency within a plugin
   - A slow plugin does not block other plugins or state tracking
   - Event ordering is preserved within each plugin

4. **Rate Limiter**: Accepts outbound messages from any task (plugins
   call `IBot.SendAsync` which writes to this component) and drains
   them to the outbound channel at a rate that respects the server's
   flood limits.

5. **Write Loop**: A dedicated async task reads from the outbound
   channel and writes serialized messages to the TCP stream.

### Concurrency Rules

- **`HandleEventAsync` is called sequentially within each plugin task.**
  A plugin's event handler is never called concurrently with itself.
  But different plugins' handlers do run concurrently.

- **State stores are read-safe from any plugin task.** The message
  processor updates state before fanning out events. State stores use
  concurrent-read-safe data structures, so plugins can safely query
  `IBot.Channels` and `IBot.Users` from their event handlers without
  synchronization.

- **`IBot.SendAsync` (and its variants) is thread-safe.** It can be
  called from any plugin task, background tasks, timers, etc.

- **`CancellationToken` propagation**: All async methods accept a
  `CancellationToken`. The bot's top-level token is cancelled on
  shutdown, which drains the channels and allows tasks to exit cleanly.

### Labeled Response Correlation

When the bot sends a command that expects a response (e.g., WHO,
WHOIS), it uses the `labeled-response` capability to tag the outbound
message with a unique label. The message processor collects responses
with matching labels and delivers them as a batch to the caller via a
`TaskCompletionSource<T>`. This allows:

```
var whoReply = await bot.SendAndAwaitAsync(whoMessage, cancellationToken);
```

`SendAndAwaitAsync` sends an IRC command and returns the server's
correlated response messages. It uses the `labeled-response` IRCv3
capability to tag the outbound message with a unique label, then
collects all inbound messages bearing that label until the server
signals completion. This is useful for commands like WHO, WHOIS, and
LIST where the response spans multiple messages.

If `labeled-response` is not negotiated, the bot falls back to
sequential command queuing with timeout-based correlation.

---

## Plugin Discovery and Loading

### Startup Sequence

Plugin loading happens during application startup, before the IRC
connection is established. The app calls
`services.AddMarv(configuration)`, which triggers the following
sequence inside `Marv.Core`.

#### Phase 1: Bootstrap (No DI)

1. **Read plugin paths**: From the configuration (already parsed by
   the app), extract the list of plugin assembly paths.

2. **Discover assemblies**: For each configured plugin path, load the
   assembly into an `AssemblyLoadContext`. Scan for types that
   implement `IPlugin` — each assembly must contain exactly one. Also
   scan for configuration classes tagged with `[PluginConfig]` and
   handler group classes tagged with `[HandlerGroup]`. Read each
   plugin's name for identification in logs, config, and diagnostics.
   The name is derived by stripping the "Plugin" suffix from the class
   name (e.g. `GreetPlugin` → `"Greet"`), or overridden with
   `[PluginName("CustomName")]`.

3. **Build dependency graph**: Inspect each discovered plugin type:
   - Read `[ProvidesService]` attributes for service types the plugin
     provides
   - Read `[DependsOn]` attributes for explicit plugin ordering
   - Read constructor parameters to identify service dependencies
     (non-core types). Non-nullable parameters are required;
     nullable parameters with a default of `null` are optional.
   Construct a directed graph of plugin dependencies.

4. **Topological sort**: Sort the graph. If there is a cycle, report
   it and fail startup. If a required dependency is missing, report
   it and fail startup. Optional dependencies participate in ordering
   (if present, the provider loads first) but do not block startup if
   absent.

#### Phase 2: DI Container Build

5. **Core service registration**: Register core services into the
   `IServiceCollection`: logging, configuration, `IIrcConnection`,
   `ICapabilityManager`, `IChannelStore`, `IUserStore`, `IBot`.

6. **Plugin configuration registration**: For each configuration class
   tagged with `[PluginConfig(Section = "Name")]`, automatically
   register `IOptions<TConfig>` bound to the matching root-level
   configuration section. No boilerplate needed in the plugin —
   plugins access configuration via constructor injection of
   `IOptions<TConfig>`.

7. **Plugin service registration**: For each plugin type (in
   dependency order), call its static `ConfigureServices` method
   (defined on `IPlugin` with an empty default implementation).
   Plugins that provide services to other plugins register their
   implementations here. Plugins that only handle events and use
   configuration do not need to override this method.

8. **Build container**: Call `BuildServiceProvider()` to create the
   single `IServiceProvider`. Note that plugin types and handler
   group types are **not** registered in the container — they are
   created via `ActivatorUtilities` (through `IPluginActivator`).

9. **Instantiate plugins**: Create each plugin instance via
   `ActivatorUtilities.CreateInstance`. Constructor parameters
   (configuration, services, `IBot`, `IPluginActivator`) are
   resolved from the container. Handler groups are created by
   `MarvPlugin` via `IPluginActivator` during construction.

10. **Initialize plugins**: Call `OnLoadAsync()` on each plugin in
    dependency order. `MarvPlugin`'s default implementation forwards
    to handler group lifecycle methods.

#### Runtime

12. **Connect**: Establish the IRC connection, negotiate capabilities,
    authenticate, join channels.

13. **Notify plugins**: Call `OnConnectedAsync()` on each plugin and
    its handler groups.

14. **Start plugin tasks**: Create a dedicated `Channel<MarvEvent>`
    and async task for each plugin. The task reads events and calls
    `plugin.HandleEventAsync(event, ct)` — the core never calls
    handler methods directly.

    For `MarvPlugin` subclasses, `HandleEventAsync` uses reflection
    to dispatch to attributed handler methods (`[OnEvent]`,
    `[OnCommand]`, `[OnRegex]`, `[OnRawMessage]`, `[OnInterval]`)
    on both the plugin class and its handler groups. The core does
    not interact with handler groups at all — `MarvPlugin` owns
    their creation (via `IPluginActivator`), lifecycle forwarding,
    and event dispatch. Handler methods on the plugin class do not
    need to be public (dispatch is from within the base class);
    handler methods on handler groups must be public. If multiple
    handlers match the same event, they are called consecutively in
    an undefined order.

    Direct `IPlugin` implementations provide their own
    `HandleEventAsync` logic.

15. **Message loop**: Process messages, update state, fan out events
    to plugin channels.

16. **Reconnection**: On disconnection, all state is discarded:
    pending `SendAndAwaitAsync` calls are cancelled, outbound message
    queues are cleared, and channel/user state stores are reset.
    Plugins are notified via `OnDisconnectedAsync`, then
    `OnUnloadAsync`. Plugin and handler group instances are discarded.
    The bot reconnects with exponential backoff, then reinstantiates
    all plugins via `ActivatorUtilities` (step 9) and repeats from
    step 10. This ensures no stale references survive a reconnection.

17. **Shutdown**: Signal all plugin tasks to stop. Call
    `OnDisconnectedAsync()` then `OnUnloadAsync()` on each plugin
    (and its handler groups) in reverse dependency order. Dispose the
    DI container.

### Assembly Load Context

Each plugin assembly is loaded into a shared `AssemblyLoadContext`.
Using a single shared context (rather than isolated contexts per
plugin) avoids type identity issues — interfaces defined in
`Marv.Core` or in a shared contracts assembly are the same `Type`
across all plugins.

If we later need assembly isolation (e.g., for hot-reloading or
conflicting dependencies), we can revisit this, but the single-context
approach is simpler and avoids a class of subtle bugs.

---

## Inter-Plugin Services

### How It Works

**Providing a service**: A plugin declares `[ProvidesService]` on its
class and overrides the static `ConfigureServices` method from
`IPlugin`. The attribute tells the dependency sorter which plugin
provides which service type; the method does the actual DI
registration.

**Consuming a service**: A plugin declares a constructor parameter of
the service type. The plugin loader inspects constructor parameters to
identify consumed services. Non-nullable parameters are required
dependencies; nullable parameters with a default of `null` are
optional.

**Explicit ordering**: `[DependsOn(typeof(OtherPlugin))]` forces load
ordering without implying a service relationship.

### Example

```csharp
// Auth plugin provides IAuthorizationService
[ProvidesService(typeof(IAuthorizationService))]
public class AuthPlugin : MarvPlugin
{
    public AuthPlugin(IBot bot, IPluginActivator activator)
        : base(bot, activator) { }

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationService, AccountBasedAuthService>();
    }
}

// Moderation plugin consumes IAuthorizationService (required)
public class ModerationPlugin : MarvPlugin
{
    public ModerationPlugin(IBot bot, IPluginActivator activator,
        IAuthorizationService auth) : base(bot, activator) { ... }
}

// Greet plugin consumes IAuthorizationService (optional)
public class GreetPlugin : MarvPlugin
{
    public GreetPlugin(
        IBot bot, IPluginActivator activator,
        IOptions<GreetPluginConfig> config,
        IAuthorizationService? auth = null) : base(bot, activator) { ... }
}
```

The loader sees:
- `AuthPlugin` provides `IAuthorizationService` (from attribute)
- `ModerationPlugin` requires `IAuthorizationService` (from
  constructor, non-nullable)
- `GreetPlugin` optionally uses `IAuthorizationService` (from
  constructor, nullable with default `null`)

Load order: AuthPlugin → ModerationPlugin, GreetPlugin (order between
Moderation and Greet is unspecified since neither depends on the
other).

### Load Order Details

The dependency sorter builds a graph from:

- `[DependsOn(typeof(OtherPlugin))]` — direct plugin dependency
- Required constructor parameters — resolved to the plugin with a
  matching `[ProvidesService]` attribute
- Nullable constructor parameters with default `null` —
  ordered-if-present

The graph is topologically sorted. Plugins with no dependencies load
first; plugins with dependencies load after their providers.

**Cycle detection**: A cycle in the dependency graph is a
configuration error. The bot reports the cycle and refuses to start.

**Missing required dependency**: If plugin A requires
`IAuthorizationService` but no loaded plugin provides it, startup
fails with a clear error message naming the missing service and the
plugin(s) that need it.

**Missing optional dependency**: If plugin A optionally consumes
`IAuthorizationService` and no plugin provides it, A loads normally.
The constructor receives `null` for that parameter.

### Diagnostics

At startup (and available via a status command), the bot logs:

- Which plugins are loaded, in what order
- Which services each plugin provides (from `[ProvidesService]`)
- Which services each plugin consumes (from constructors)
- Any plugins that were skipped and why

---

## Configuration

Configuration is layered, with later sources overriding earlier ones:

1. **Default values** (hardcoded in configuration classes)
2. **Configuration file** (`marv.json` by default). A `--config`
   CLI argument allows specifying an alternative path; the file
   extension determines the format (`.json` for JSON, `.yaml`/`.yml`
   for YAML, etc. via the appropriate
   `Microsoft.Extensions.Configuration` provider).
3. **Environment variables** (`MARV_*` prefix, double-underscore for
   nesting: `MARV_IRC__SERVER`)
4. **Command-line arguments** (`--irc:server=irc.example.com`)

This uses the standard `Microsoft.Extensions.Configuration` stack.
Command-line parsing uses `System.CommandLine` 2.x.

### Plugin Configuration

Plugins that need configuration declare a configuration class tagged
with `[PluginConfig]`:

```csharp
[PluginConfig(Section = "Greet")]
public record GreetPluginConfig
{
    public string GreetMessage { get; init; } = "Hello, {nick}!";
    public bool GreetOnJoin { get; init; } = true;
}

public class GreetPlugin : MarvPlugin
{
    private readonly GreetPluginConfig _config;

    public GreetPlugin(IBot bot, IPluginActivator activator,
        IOptions<GreetPluginConfig> config) : base(bot, activator)
    {
        _config = config.Value;
    }
}
```

The plugin loader discovers configuration classes tagged with
`[PluginConfig(Section = "Name")]` during assembly scanning and
automatically registers `IOptions<TConfig>` bound to the
the matching root-level configuration section. Plugins access their
configuration via constructor injection of `IOptions<TConfig>`.

There is no need for a `ConfigureServices` method just for
configuration — this is handled automatically. Only plugins that
register services for other plugins to consume need
`ConfigureServices`.

---

## Error Handling

- **Parse errors**: Malformed messages from the server are logged and
  skipped — they never crash the message processor.
- **Plugin handler exceptions**: Caught by the plugin's event loop
  task, logged with the plugin name and event type, and do not affect
  other plugins or the message loop.
- **Connection loss**: The connection layer signals disconnection. All
  pending `SendAndAwaitAsync` calls are cancelled, outbound message
  queues are cleared, and channel/user state stores are reset. The bot
  notifies plugins via `OnDisconnectedAsync`, then begins reconnection
  with exponential backoff.
- **Startup failures**: Missing required plugins/services, config
  validation errors, and dependency cycles all produce clear error
  messages and prevent the bot from starting.
