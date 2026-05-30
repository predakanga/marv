# ADR-004: Async / Threading Model

**Status**: Proposed  
**Date**: 2026-05-30

## Context

An IRC bot has several concurrent concerns:

- Reading from the network (blocking I/O)
- Writing to the network (with rate limiting)
- Processing inbound messages (state updates, event dispatch)
- Running plugin handlers (which may do I/O of their own)
- Periodic tasks (timers, health checks)

We need to decide how these concerns are distributed across threads
and tasks, and what concurrency guarantees plugins receive.

## Decision

**Per-plugin tasks with a central message processor, connected by
`System.Threading.Channels`.**

### Architecture

The system uses N+4 long-lived async tasks (where N is the number of
loaded plugins):

1. **Read loop**: Reads raw lines from the TCP stream, parses them
   into `IrcMessage`, and writes them to an inbound
   `Channel<IrcMessage>`. This task does no processing beyond parsing.

2. **Message processor**: Reads from the inbound channel. For each
   message:
   - Handles protocol-level concerns (PING/PONG, CAP negotiation)
   - Updates state tracking (channels, users, modes)
   - Translates raw messages into typed events
   - Fans out each event to every plugin's individual event channel

3. **Plugin tasks** (one per plugin): Each plugin has its own
   `Channel<MarvEvent>` and a dedicated async task. The task reads
   events from the channel and calls `plugin.HandleEventAsync(event,
   ct)` for each one. The core never calls handler methods directly —
   dispatch is the plugin's responsibility (provided by `MarvPlugin`
   via reflection, or custom logic for direct `IPlugin` implementations).

4. **Rate limiter**: Accepts outbound messages from any task and
   releases them to the outbound channel at a rate that respects the
   server's flood limits (token bucket algorithm).

5. **Write loop**: Reads from the outbound channel and writes
   serialized messages to the TCP stream.

### Concurrency Guarantees

- **Within a plugin, `HandleEventAsync` is called sequentially.** The
  core never calls it concurrently with itself for the same plugin.
  This means a plugin author does not need to think about thread
  safety for the plugin's own state.

- **Different plugins run concurrently.** Plugin A's handler for
  an event may be running at the same time as Plugin B's handler for
  the same (or a different) event.

- **State stores are read-safe from any plugin task.** The message
  processor updates state before fanning out events. State models are
  mutable, with individual property reads being atomic and collections
  using `ConcurrentDictionary` for safe concurrent access. Since only
  the message processor writes, there is no write contention. Plugins
  can safely read `IBot.Channels` and `IBot.Users` from their event
  handlers. Cross-property consistency is not guaranteed — see the
  rationale section below.

- **`IBot.SendAsync` (and its variants) is thread-safe.** It writes
  to the rate limiter's input channel, which supports concurrent
  writers.

- **`CancellationToken` propagation**: All async methods accept a
  `CancellationToken`. The bot's top-level token is cancelled on
  shutdown, which drains the channels and allows tasks to exit cleanly.

### Plugin Background Work

A plugin that needs to do slow work (HTTP requests, database queries)
can:

1. Start the work with `Task.Run` or an async call
2. When the work completes, use `IBot.SendAsync` to send any resulting
   messages
3. Safely read `IBot.Channels` and `IBot.Users` (these are read-safe)

If a plugin needs to update its own internal state from background
work, it should use its own synchronization (e.g., a `Channel<T>`
that the event handler drains, or `lock`/`ConcurrentDictionary` for
simple cases).

## Rationale

### Why per-plugin tasks instead of a single dispatcher

**Plugin isolation.** With a single message processor calling all
plugin handlers sequentially, a slow plugin blocks every other plugin
and stalls state tracking. Per-plugin tasks isolate plugins from each
other — a slow handler in Plugin A only delays Plugin A's subsequent
events.

**Natural concurrency.** Plugins are independent units of
functionality. Running them concurrently matches the mental model:
the greeting plugin and the moderation plugin shouldn't need to wait
for each other.

**Sequential within a plugin.** Within a single plugin,
`HandleEventAsync` is called sequentially — never concurrently with
itself. This preserves the key DX benefit: a plugin author doesn't
need to think about thread safety for their own state. The
concurrency boundary is between plugins, not within them.

### Why state stores are read-safe

The message processor updates state (channels, users, modes) before
fanning out events to plugin channels. Since writes happen on one
task and reads happen on plugin tasks, the data structures must
support concurrent reads. State models (`IUser`, `IChannel`) are
mutable objects with individual property reads being atomic
(reference type fields in .NET). Collection-valued properties use
`ConcurrentDictionary`, which supports safe concurrent enumeration.

Cross-property consistency is not guaranteed — a state change could
land between two reads within a handler. This is acceptable because
the window is small and the practical impact is minimal. Plugins
needing strict consistency across multiple properties can copy values
into locals at handler entry.

### Why System.Threading.Channels

- **Bounded memory**: Channels can be bounded, providing natural
  backpressure if a plugin falls behind.
- **Async-native**: `ReadAsync` / `WriteAsync` integrate with
  `async/await` and `CancellationToken`.
- **High performance**: Minimal allocation, lock-free for
  single-producer/single-consumer channels.
- **Standard .NET**: No third-party dependency, familiar API.
- **Fan-out friendly**: The message processor writes to N plugin
  channels — `Channel<T>` handles this efficiently.

### Why not Rx (System.Reactive)

Rx provides powerful composition operators (merge, buffer, throttle)
but adds a dependency and a learning curve. The message flow in Marv
is simple: one inbound stream, state update, fan-out to N plugin
channels. Channels are sufficient and more predictable.

### Why not a thread pool / Task.Run per event

Dispatching each event to the thread pool would mean a plugin's
handlers could run concurrently with each other, requiring every
plugin to be thread-safe. Per-plugin channels preserve sequential
ordering within a plugin without sacrificing inter-plugin concurrency.

## Consequences

- A misbehaving plugin handler that blocks its task will only affect
  that plugin, not others. However, it will miss subsequent events
  (they queue up in its channel). Mitigation: log warnings if a
  handler takes longer than a configurable threshold; bounded channels
  with drop-oldest policy as a safety valve.

- Plugins must not assume ordering relative to other plugins. If
  Plugin A and Plugin B both handle `UserJoinedEvent`, either one
  might process it first.

- State models are mutable with atomic individual property reads.
  Cross-property consistency is not guaranteed during a handler, but
  this is rare in practice. This is an implementation concern inside
  `Marv.Core`, not a plugin concern — plugins just read properties
  normally.

- The rate limiter prevents any plugin (or combination of plugins)
  from flooding the server, even if multiple plugins send messages
  concurrently.

- Reconnection is handled by tearing down all tasks and restarting
  them. Plugins are notified via `OnDisconnectedAsync` /
  `OnConnectedAsync`. On disconnection, all state is discarded:
  pending `SendAndAwaitAsync` calls are cancelled (their
  `TaskCompletionSource` is faulted with a disconnection exception),
  outbound message queues are cleared, and channel/user state stores
  are reset. Plugins should treat `OnDisconnectedAsync` as a signal
  that any cached state references (`IChannel`, `IUser`) are stale.
