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

The CLI host application. Responsibilities:

- Parse command-line arguments (using `System.CommandLine` or raw args)
- Read configuration from files, environment variables, and CLI args
  (in ascending priority order)
- Initialize logging (`Microsoft.Extensions.Logging` with console and
  file sinks)
- Discover and load plugin assemblies
- Build the DI container
- Run the bot's main loop
- Handle graceful shutdown (Ctrl+C / SIGTERM)

This assembly references `Marv.Core` and the .NET hosting/DI packages.
It does not reference any plugin assemblies directly — plugins are loaded
at runtime.

### Marv.Core

The core library. Contains everything a plugin author needs to reference
at compile time, and everything the host needs to run the bot. This is
the only assembly that plugin projects reference.

Responsibilities:

- IRC message parsing and serialization
- TCP/TLS connection management
- IRCv3 capability negotiation and SASL authentication
- Rate-limited message sending
- Channel/user/mode state tracking
- Plugin base classes, attributes, and lifecycle interfaces
- Event types and the event dispatch system
- The `IBot` facade interface
- Plugin discovery, dependency sorting, and lifecycle management
- The `IServiceCollection` integration point for plugin service
  registration

### Plugin Assemblies

Each plugin is a separate assembly (class library) that references
`Marv.Core`. A plugin assembly contains:

- One or more plugin classes inheriting from `MarvPlugin`
- Optional configuration record/class
- Optional service interfaces (or these may live in a separate
  contracts assembly shared between provider and consumer)
- Optional service implementations

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
│   MarvPlugin, attributes, events        │  event dispatch
├─────────────────────────────────────────┤
│            Bot Facade                   │  IBot — high-level API
│   Send messages, query state            │  exposed to plugins
├─────────────────────────────────────────┤
│            State Tracking               │  Channels, users, modes,
│   IChannelStore, IUserStore             │  ISUPPORT parameters
├─────────────────────────────────────────┤
│            Capability Engine            │  CAP negotiation, SASL,
│   ICapabilityManager                    │  capability state
├─────────────────────────────────────────┤
│            Connection                   │  TCP/TLS, reconnection,
│   IIrcConnection                        │  rate limiting, PING/PONG
├─────────────────────────────────────────┤
│            Protocol                     │  IrcMessage parsing,
│   IrcParser, IrcSerializer              │  serialization, tags,
│                                         │  case mapping
└─────────────────────────────────────────┘
```

### Protocol Layer

- `IrcMessage`: Immutable record representing a parsed IRC message
  (tags, source, command, parameters). The trailing parameter is folded
  into the parameter list — there is no separate `trailing` field.
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
  `ChannelReader<IrcMessage>` for inbound messages and accepts outbound
  messages via a `ChannelWriter<IrcMessage>`.
- Handles PING/PONG at the protocol level (not exposed to plugins).
- Implements rate limiting on outbound messages using a token bucket
  algorithm to prevent excess flood disconnections.
- Handles reconnection with exponential backoff.
- UTF-8 encoding with graceful handling of invalid sequences.

### Capability Engine

- `ICapabilityManager`: Manages IRCv3 CAP negotiation during
  registration.
- Handles multi-line `CAP LS`, capability values, `CAP NEW`/`CAP DEL`
  notifications (`cap-notify`).
- SASL authentication (PLAIN mechanism initially; EXTERNAL as a
  follow-up).
- Exposes the set of negotiated capabilities so higher layers and
  plugins can check feature availability.

### State Tracking

- `IChannelStore`: Maintains the set of channels the bot is in, each
  with its topic, modes, and member list.
- `IUserStore`: Maintains known users (nick, user, host, account, away
  status). Updated from NAMES, WHO, JOIN, PART, QUIT, NICK, CHGHOST,
  ACCOUNT, AWAY, SETNAME messages.
- `IModeParser`: Parses MODE changes using the server's CHANMODES and
  PREFIX from ISUPPORT to correctly handle type A/B/C/D modes.
- All comparisons use the server's advertised CASEMAPPING.

### Bot Facade

- `IBot`: The primary interface plugins interact with. Provides
  methods for sending messages, querying channel/user state, and
  checking capability availability. See `plugin-api-draft.md` for the
  full surface.

### Plugin System

- Plugin discovery, assembly loading, dependency graph construction,
  topological sorting, and lifecycle management.
- Event dispatch: routes incoming IRC events to interested plugin
  handlers.
- Described in detail below.

---

## Async / Threading Model

Marv uses a small number of long-lived async tasks communicating via
`System.Threading.Channels`. There is no thread pool dispatch for
message handling — plugin handlers run on the message processing task
to avoid concurrency issues with state.

### Task Structure

```
┌──────────────┐     Channel<IrcMessage>     ┌──────────────────┐
│  Read Loop   │ ──────────────────────────► │  Message         │
│  (network)   │     (inbound)               │  Processor       │
└──────────────┘                             │                  │
                                             │  - State updates │
                                             │  - Event dispatch│
                                             │  - Plugin calls  │
                                             └──────────────────┘
                                                      │
                                                      │ (plugins call
                                                      │  IBot.SendAsync)
                                                      ▼
┌──────────────┐     Channel<IrcMessage>     ┌──────────────────┐
│  Write Loop  │ ◄────────────────────────── │  Rate Limiter    │
│  (network)   │     (outbound)              │  (token bucket)  │
└──────────────┘                             └──────────────────┘
```

1. **Read Loop**: A dedicated async task reads from the TCP stream,
   parses raw lines into `IrcMessage` instances, and writes them to
   the inbound channel. This task does no processing beyond parsing.

2. **Message Processor**: A dedicated async task reads from the inbound
   channel. For each message it:
   - Handles protocol-level concerns (PING/PONG, CAP negotiation)
   - Updates state tracking (channels, users, modes)
   - Translates raw `IrcMessage` into typed events
   - Dispatches events to interested plugin handlers

3. **Rate Limiter**: Accepts outbound messages from any task (plugins
   call `IBot.SendAsync` which writes to this component) and drains
   them to the outbound channel at a rate that respects the server's
   flood limits.

4. **Write Loop**: A dedicated async task reads from the outbound
   channel and writes serialized messages to the TCP stream.

### Concurrency Rules

- **Plugin event handlers run on the message processor task.** This
  means handlers run one at a time, in order, and can safely read bot
  state without locks. A handler that needs to do slow work (HTTP
  calls, database queries) should offload to `Task.Run` and use
  `IBot.SendAsync` to send results back — `SendAsync` is thread-safe.

- **`IBot.SendAsync` is the only thread-safe entry point.** All other
  `IBot` state-query methods are only safe to call from a plugin event
  handler (i.e., on the message processor task).

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

If `labeled-response` is not negotiated, the bot falls back to
sequential command queuing with timeout-based correlation.

---

## Plugin Discovery and Loading

### Startup Sequence

Plugin loading happens during application startup, before the IRC
connection is established. The sequence has two phases as described in
the research document.

#### Phase 1: Bootstrap (No DI)

1. **Read configuration**: The host reads the configuration file
   (e.g., `marv.toml` or `marv.json`), overlays environment variables
   (`MARV_*`), and overlays command-line arguments. This produces a
   raw configuration object containing (among other things) the list
   of plugin assembly paths.

2. **Discover assemblies**: For each configured plugin path, load the
   assembly into an `AssemblyLoadContext`. Scan for types that inherit
   from `MarvPlugin`.

3. **Build dependency graph**: Read `[DependsOn]` and
   `[ConsumesService]` attributes from each discovered plugin type.
   Construct a directed graph of plugin dependencies.

4. **Topological sort**: Sort the graph. If there is a cycle, report
   it and fail startup. If a required dependency is missing, report
   it and fail startup. Optional dependencies participate in ordering
   (if present, the provider loads first) but do not block startup if
   absent.

#### Phase 2: DI Container Build

5. **Core service registration**: Register core services into a fresh
   `IServiceCollection`: logging, configuration, `IIrcConnection`,
   `ICapabilityManager`, `IChannelStore`, `IUserStore`, `IBot`.

6. **Plugin service registration**: For each plugin type (in
   dependency order), call its static `ConfigureServices` method,
   passing the `IServiceCollection`. Plugins register their service
   implementations and configuration bindings here. This method is
   static — no plugin instance exists yet.

7. **Plugin type registration**: Register each plugin type itself as
   a singleton in the container.

8. **Build container**: Call `BuildServiceProvider()` to create the
   single `IServiceProvider`.

9. **Resolve plugins**: Resolve each plugin type from the container.
   Constructor injection provides configuration, services, and the
   `IBot` facade.

10. **Initialize plugins**: Call `OnLoadAsync()` on each plugin in
    dependency order.

#### Runtime

11. **Connect**: Establish the IRC connection, negotiate capabilities,
    authenticate, join channels.

12. **Notify plugins**: Call `OnConnectedAsync()` on each plugin.

13. **Message loop**: Process messages until shutdown.

14. **Shutdown**: Call `OnDisconnectedAsync()` then `OnUnloadAsync()` on
    each plugin in reverse dependency order. Dispose the DI container.

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

### Registration

A plugin provides a service by:

1. Defining an interface (e.g., `IAuthorizationService`). This
   interface can live in:
   - The plugin's own assembly (if consumers are expected to reference
     it)
   - A separate contracts assembly (if the interface should be
     decoupled from the implementation)
   - `Marv.Core` itself (for services that are fundamental enough to
     be part of the core API)

2. Implementing the interface in the plugin assembly.

3. Registering the implementation in `ConfigureServices`:
   ```csharp
   public static void ConfigureServices(IServiceCollection services)
   {
       services.AddSingleton<IAuthorizationService, AuthorizationService>();
   }
   ```

4. Declaring the provided service via attribute on the plugin class:
   ```csharp
   [ProvidesService(typeof(IAuthorizationService))]
   public class AuthPlugin : MarvPlugin { ... }
   ```

The `[ProvidesService]` attribute serves two purposes: it enables the
dependency sorter to know which plugin provides which service, and it
enables diagnostic tooling to report the service → plugin mapping.

### Discovery and Consumption

A plugin consumes a service by:

1. Declaring the dependency via constructor injection:
   ```csharp
   public class GreetPlugin : MarvPlugin
   {
       public GreetPlugin(IAuthorizationService auth) { ... }
   }
   ```

2. Declaring the dependency via attribute for the dependency sorter:
   ```csharp
   [ConsumesService(typeof(IAuthorizationService))]
   public class GreetPlugin : MarvPlugin { ... }
   ```

For **optional** dependencies:

```csharp
[ConsumesService(typeof(IAuthorizationService), Required = false)]
public class GreetPlugin : MarvPlugin
{
    public GreetPlugin(IAuthorizationService? auth = null) { ... }
}
```

When the service is not registered, the constructor receives `null`
and the plugin operates with reduced functionality.

### Load Order

The dependency sorter builds a graph from:

- `[DependsOn(typeof(OtherPlugin))]` — direct plugin dependency
- `[ConsumesService(typeof(IFoo))]` — resolved to the plugin with
  `[ProvidesService(typeof(IFoo))]`

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
- Which services each plugin provides
- Which services each plugin consumes (and whether optional)
- Any plugins that were skipped and why

---

## Configuration

Configuration is layered, with later sources overriding earlier ones:

1. **Default values** (hardcoded in configuration classes)
2. **Configuration file** (`marv.toml` / `marv.json` — format TBD)
3. **Environment variables** (`MARV_*` prefix, double-underscore for
   nesting: `MARV_IRC__SERVER`)
4. **Command-line arguments** (`--irc:server=irc.example.com`)

This uses the standard `Microsoft.Extensions.Configuration` stack.

### Plugin Configuration

Each plugin declares a configuration class (a record or POCO):

```csharp
public record GreetPluginConfig
{
    public string GreetMessage { get; init; } = "Hello, {nick}!";
    public bool GreetOnJoin { get; init; } = true;
}
```

During `ConfigureServices`, the plugin binds its configuration
section:

```csharp
public static void ConfigureServices(IServiceCollection services)
{
    services.AddOptions<GreetPluginConfig>()
        .BindConfiguration("Plugins:Greet");
}
```

The plugin receives `IOptions<GreetPluginConfig>` via constructor
injection. Configuration is validated at startup before the bot
connects.

---

## Error Handling

- **Parse errors**: Malformed messages from the server are logged and
  skipped — they never crash the message processor.
- **Plugin handler exceptions**: Caught by the event dispatcher,
  logged with the plugin name and event type, and do not affect other
  plugins or the message loop.
- **Connection loss**: The connection layer signals disconnection. The
  bot notifies plugins via `OnDisconnectedAsync`, then begins
  reconnection with exponential backoff.
- **Startup failures**: Missing required plugins/services, config
  validation errors, and dependency cycles all produce clear error
  messages and prevent the bot from starting.
