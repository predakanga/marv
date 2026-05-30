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

**Single-threaded message processing with dedicated I/O tasks,
connected by `System.Threading.Channels`.**

### Architecture

Four long-lived async tasks:

1. **Read loop**: Reads raw lines from the TCP stream, parses them
   into `IrcMessage`, and writes them to an inbound
   `Channel<IrcMessage>`. This task does no processing beyond parsing.

2. **Message processor**: Reads from the inbound channel. For each
   message:
   - Handles protocol-level concerns (PING/PONG, CAP negotiation)
   - Updates state tracking (channels, users, modes)
   - Translates raw messages into typed events
   - Dispatches events to plugin handlers, sequentially

3. **Rate limiter**: Accepts outbound messages from any task and
   releases them to the outbound channel at a rate that respects the
   server's flood limits (token bucket algorithm).

4. **Write loop**: Reads from the outbound channel and writes
   serialized messages to the TCP stream.

### Concurrency Guarantees

- **Plugin event handlers run sequentially on the message processor
  task.** Two handlers never run concurrently. A handler can safely
  read `IBot` state (channels, users) without synchronization.

- **`IBot.SendAsync` (and its variants) is the only thread-safe entry
  point.** It writes to the rate limiter's input channel, which is
  safe for concurrent writers.

- **State query methods on `IBot`** (`GetChannel`, `GetUser`,
  `Channels`) are only safe from event handlers. Calling them from a
  `Task.Run` background task is a race condition.

### Plugin Background Work

A plugin that needs to do slow work (HTTP requests, database queries)
should:

1. Start the work with `Task.Run` or an async call
2. When the work completes, use `IBot.SendAsync` to send any resulting
   messages
3. Not access `IBot` state queries from the background task

If a plugin needs to update its own state based on background work,
it should use its own synchronization (e.g., a `Channel<T>` that the
event handler drains, or a `ConcurrentDictionary` for simple lookups).

## Rationale

### Why single-threaded message processing

**Simplicity for plugin authors.** If plugin handlers could run
concurrently, every plugin would need to handle its own
synchronization. This is the #1 source of bugs in concurrent systems
and is antithetical to the DX design goal.

**Sequential event ordering.** IRC events have a natural ordering
(join before message, message before part). Running handlers
concurrently could deliver events out of order.

**State consistency.** The bot's channel/user state is updated between
handler calls. Sequential processing means a handler always sees
consistent, up-to-date state.

**Acceptable performance.** IRC is a low-throughput protocol (a busy
channel might produce a few messages per second). The message
processor will never be the bottleneck. If a plugin handler blocks
for too long, that is the plugin's bug — it should offload slow work.

### Why System.Threading.Channels

- **Bounded memory**: Channels can be bounded, providing natural
  backpressure.
- **Async-native**: `ReadAsync` / `WriteAsync` integrate with
  `async/await` and `CancellationToken`.
- **High performance**: Lock-free for single-producer/single-consumer
  channels, minimal allocation.
- **Standard .NET**: No third-party dependency, familiar API.

### Why not Rx (System.Reactive)

Rx provides powerful composition operators (merge, buffer, throttle)
but adds a dependency and a learning curve. The message flow in Marv
is simple: one inbound stream, processed sequentially, with outbound
messages queued. Channels are sufficient and more predictable.

### Why not a thread pool / Task.Run per message

Dispatching each message to the thread pool would enable concurrent
handler execution but would:

- Require every plugin to be thread-safe
- Make event ordering non-deterministic
- Complicate state tracking (need locks or concurrent collections
  throughout)
- Add no meaningful throughput improvement for IRC workloads

## Consequences

- A misbehaving plugin handler that blocks the message processor task
  will block all other plugins and stall state tracking. Mitigation:
  document that handlers must not block, and log warnings if a handler
  takes longer than a configurable threshold.

- Plugins that need concurrent processing must manage it themselves
  (via `Task.Run`, `Channel<T>`, etc.). The framework provides
  `IBot.SendAsync` as the thread-safe re-entry point.

- The rate limiter prevents any plugin (or combination of plugins)
  from flooding the server, even if multiple plugins send messages
  concurrently.

- Reconnection is handled by tearing down all four tasks and
  restarting them. Plugins are notified via `OnDisconnectedAsync` /
  `OnConnectedAsync`.
