# CS-021: Message Queue Management

**Source:** Downstream feature request
**Scope:** Core (IBot interface)
**Complexity:** Small
**Breaking changes:** Additive only (new interface members)
**Status:** Pending

---

## Problem

The bot's outbound message queue (`IrcConnection._outboundChannel`) is a
bounded `Channel<IrcMessage>` with a capacity of 512. Plugins have no
visibility into or control over this queue. Two common needs:

1. **Inspect queue depth** — a monitoring plugin or status command wants to
   show how backed up the outbound queue is (e.g. after a flood of messages).
2. **Clear the queue** — an operator or admin plugin wants to abort a
   large batch of pending messages (e.g. a plugin accidentally enqueued
   hundreds of messages, or the bot is being shut down gracefully).

Today there is no way to do either without reaching into `IrcConnection`
internals.

## Changes

### 1. Add queue inspection/management to `IBot`

```csharp
/// <summary>
/// Number of messages currently waiting in the outbound send queue.
/// </summary>
int OutboundQueueCount { get; }

/// <summary>
/// Discards all messages currently waiting in the outbound send queue.
/// Messages already handed to the rate limiter or TCP write are not affected.
/// </summary>
Task ClearOutboundQueueAsync(CancellationToken ct);
```

### 2. Expose queue state on `IrcConnection`

`IrcConnection` already holds the `_outboundChannel`. Add:

```csharp
/// <summary>Number of items waiting in the outbound channel.</summary>
public int OutboundQueueCount =>
    _outboundChannel?.Reader.Count ?? 0;
```

For clearing, the simplest approach is to drain the channel by reading and
discarding all currently available items:

```csharp
/// <summary>Drains all pending messages from the outbound channel.</summary>
public int DrainOutboundQueue()
{
    if (_outboundChannel is null) return 0;
    var drained = 0;
    while (_outboundChannel.Reader.TryRead(out _))
        drained++;
    return drained;
}
```

This is safe because `SingleWriter = false` on the outbound channel, so
reads from any thread are valid as long as we respect the `SingleReader`
constraint. Since the write loop is the single reader, we need to
coordinate: either temporarily pause the write loop, or accept that a
small number of messages may be sent between the drain and the next
send. Given that this is an operator-initiated emergency action, the
race is acceptable and documented.

**Alternative:** Replace the channel entirely by creating a new
`Channel<IrcMessage>` and swapping the reference. This guarantees no
stragglers but adds complexity around the write loop's reader reference.
The drain approach is simpler and sufficient.

### 3. Implement in `IrcBot`

```csharp
public int OutboundQueueCount => _connection?.OutboundQueueCount ?? 0;

public Task ClearOutboundQueueAsync(CancellationToken ct)
{
    var drained = _connection?.DrainOutboundQueue() ?? 0;
    _logger.LogInformation("Drained {Count} messages from outbound queue", drained);
    return Task.CompletedTask;
}
```

### 4. Update MockBot (Marv.Testing)

`MockBot.Create()` returns an NSubstitute mock, so the new members are
automatically stubbed. `OutboundQueueCount` returns `0` by default.

### 5. Update PLUGIN_API.md

Add the new members to the IBot table in §5.

## Design decisions

**Why not expose the `ChannelWriter<IrcMessage>` directly?** That would
let plugins bypass rate limiting, inject malformed messages, or complete
the channel. The bot should remain the gatekeeper for outbound traffic.

**Why `Task` return for clear instead of `void`?** Future implementations
might need to coordinate with the write loop asynchronously (e.g. pausing
it during the drain). The `Task` signature allows this without a breaking
change.

**Why not a priority queue or reordering API?** Over-engineering for the
stated need. Inspection and clearing cover the real use cases. Plugins
that need message prioritization can implement their own internal queue
and feed messages to the bot one at a time.

## Testing

- **Unit test:** Enqueue several messages (via `SendMessageAsync` with a
  blocked/slow connection mock), verify `OutboundQueueCount` reflects the
  pending count.
- **Unit test:** Enqueue messages, call `ClearOutboundQueueAsync`, verify
  count drops to 0.
- **Integration test:** Send a burst of messages, verify queue count is
  observable and eventually drains to 0.

## Impact

- **Plugin DX:** Admin plugins and status commands can monitor and manage
  the outbound queue.
- **API surface:** One new property, one new method on `IBot`.
- **Risk:** Low — read-only inspection has no side effects; clearing is
  an explicit operator action.
