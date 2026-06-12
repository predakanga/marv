# CS-026: Connection-Scoped DI Services

**Source:** GitHub issue #1
**Scope:** Core (Plugin API, DI container setup)
**Complexity:** Medium-Large
**Breaking changes:** Yes — core services move from singleton to scoped registration
**Status:** Pending

---

## Problem

Plugins are implicitly scoped to each IRC connection: `MarvBotService`
calls `PluginManager.InstantiatePlugins` at the top of every reconnect
loop, and `UnloadPluginsAsync` on disconnect. However, the DI services
that plugins depend on — `IBot`, `IServerInfo`, `ICapabilityManager`,
`IBotStatistics` — are registered as singletons via `AddMarv`. This
means:

1. **Stale state across reconnects.** Singleton services survive scope
   boundaries that plugins don't. A plugin that caches a reference to
   `IServerInfo` in `OnLoadAsync` silently holds the same instance across
   reconnects, even though the bot calls `ResetState()` and the
   underlying data is cleared. This works *today* because the object is
   mutated in place, but it's fragile and semantically incorrect.

2. **Scoped services are unusable.** Plugins can call
   `services.AddScoped<T>()` in `ConfigureServices`, but because
   `PluginActivator` resolves from the root `IServiceProvider`, the
   scoped registration is never honoured — the container either throws
   (`ValidateScopes`) or silently creates a singleton-lifetime instance.

3. **No formal connection boundary.** There is no DI-level signal that
   a new connection has begun. Plugins that need per-connection state
   must manage it manually in `OnConnectedAsync`/`OnDisconnectedAsync`.

## Changes

### 1. Create an `IServiceScope` per connection in `MarvBotService`

At the start of each iteration of the reconnect loop in
`MarvBotService.ExecuteAsync`, create a new `IServiceScope` from the
root `IServiceProvider`. Dispose it after disconnect cleanup completes.

```csharp
while (!stoppingToken.IsCancellationRequested)
{
    await using var connectionScope = _serviceScopeFactory.CreateAsyncScope();
    var scopedProvider = connectionScope.ServiceProvider;

    // Use scopedProvider for all resolution within this connection...
}
```

`MarvBotService` should inject `IServiceScopeFactory` instead of (or in
addition to) the services it currently resolves at construction time.
Services that are now scoped (`IrcBot`, etc.) must be resolved from
`scopedProvider` inside the loop rather than captured in the constructor.

### 2. Move connection-specific core services to scoped registration

In `MarvServiceExtensions.AddMarv`, change these registrations from
`AddSingleton` to `AddScoped`:

| Service | Interface(s) | Notes |
|---|---|---|
| `ServerInfo` | `IServerInfo`, concrete | Holds ISUPPORT/005 state |
| `CapabilityManager` | `ICapabilityManager`, concrete | Holds negotiated caps |
| `IrcBot` | `IBot`, concrete | Holds connection, message loop state |
| `BotStatistics` | `IBotStatistics` | Derived from `IrcBot` |

Services that are genuinely application-lifetime (`PluginManager`,
`IPluginActivator`, `IReadOnlyList<PluginDescriptor>`,
`IHttpClientFactory`, `IOptions<MarvConfiguration>`) remain singletons.

The scoped `IrcBot` replaces the current pattern where a single
singleton instance calls `ResetState()` between connections. Each scope
gets a fresh `IrcBot`, eliminating the need for `ResetState()`.

### 3. Update `PluginManager` to accept a scoped `IServiceProvider`

`PluginManager` is a singleton, but `InstantiatePlugins` needs to
resolve from the connection scope. Change `InstantiatePlugins` (and
other per-connection methods) to accept the scoped `IServiceProvider` as
a parameter rather than using the one captured at construction:

```csharp
internal void InstantiatePlugins(
    IReadOnlyList<PluginDescriptor> descriptors,
    IServiceProvider scopedProvider)
{
    // Use scopedProvider for ActivatorUtilities.CreateInstance
}
```

This keeps `PluginManager` as a singleton (so it survives reconnects and
maintains descriptor state) while ensuring plugins receive scoped
dependencies.

### 4. Update `PluginActivator` similarly

`PluginActivator` currently captures `IServiceProvider` in its
constructor. Since it's a singleton, it always holds the root provider.
Either:

- **(a)** Make `PluginActivator` scoped (so it naturally gets the scoped
  provider), or
- **(b)** Remove `PluginActivator` and inline `ActivatorUtilities` usage
  in `PluginManager`, passing the scoped provider directly.

Option (b) is simpler since `PluginActivator` is only used in one place
and the `IPluginActivator` interface is an internal detail, not part of
the plugin API.

### 5. Update `MarvBotService` constructor and connection loop

`MarvBotService` currently injects `IrcBot`, `PluginManager`, etc. as
constructor parameters. After this change:

- Remove `IrcBot` from the constructor — resolve it from the scoped
  provider inside the loop.
- Add `IServiceScopeFactory` to the constructor.
- `IrcConnection` creation stays as-is (it's a local object, not
  DI-managed).
- Pass the scoped provider to `PluginManager.InstantiatePlugins`.

### 6. Update `Marv.Testing` to create scoped test fixtures

`PluginTestHarness` builds an `IServiceProvider` and resolves plugin
dependencies from it. Update it to:

1. Register `MockBot` and other test doubles as scoped services.
2. Create an `IServiceScope` and expose the scoped provider.
3. Ensure the scope is disposed when the harness is disposed.

This ensures test plugins receive services with the same lifetime
semantics as production.

### 7. Update `docs/PLUGIN_API.md`

Document:

- That `IBot`, `IServerInfo`, `ICapabilityManager`, and `IBotStatistics`
  are connection-scoped and receive a fresh instance per connection.
- That plugins can register scoped services via `ConfigureServices` and
  they will be correctly resolved per connection.
- That singleton services survive reconnects while scoped services do
  not.
- Migration guidance for any plugins that stored these services in
  static fields or long-lived caches.

## Design decisions

**Why not make plugins themselves scoped services?** `PluginManager`
controls instantiation order based on the dependency graph. The DI
container does not guarantee construction order, so letting it manage
plugin lifetimes would break the dependency ordering contract.

**Why not introduce a new registration point for scoped services?**
`IServiceCollection` already supports `AddScoped<T>()`. The only reason
it didn't work before was that `PluginActivator` resolved from the root
provider. Fixing the provider is sufficient; no API change needed.

**Why keep `PluginManager` as a singleton?** It holds the descriptor
list and orchestrates the full lifecycle across reconnects. Making it
scoped would mean re-discovering plugins on every reconnect, which is
unnecessary and expensive.

## Testing

- **Unit test:** Verify that `IBot` resolved within a scope is a
  different instance from one resolved in a second scope.
- **Unit test:** Verify that a plugin registering a scoped service via
  `ConfigureServices` receives a fresh instance per connection scope.
- **Unit test:** Verify that a plugin registering a singleton service
  receives the same instance across connection scopes.
- **Unit test:** Verify that `PluginManager.InstantiatePlugins` uses the
  provided scoped provider, not the root provider.
- **Integration test:** Connect, disconnect, reconnect — verify plugins
  receive fresh `IBot`/`IServerInfo` instances on the second connection
  and that the previous instances are not referenced.
- **Marv.Testing:** Verify `PluginTestHarness` creates a scope and that
  `MockBot` is resolved as a scoped service.

## Impact

- **Plugin API:** Breaking change — plugins that stored `IBot` or
  `IServerInfo` references in static fields or singleton services will
  hold stale references after reconnect. This is already a latent bug
  (since the objects are reset), so the fix makes the failure mode
  explicit rather than silent.
- **Plugin DX:** Scoped services now work correctly. Plugins can use the
  standard .NET DI lifetime model without surprises.
- **Core:** `IrcBot.ResetState()` can be removed since each connection
  gets a new instance. This simplifies the bot's internal state
  management.
- **Risk:** Medium — touches the DI registration, the main service loop,
  plugin instantiation, and the test harness. Thorough testing of the
  reconnect path is essential.
