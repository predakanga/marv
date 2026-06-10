# CS-020: Bot Statistics Property — COMPLETED

**Source:** Downstream feature request
**Scope:** Core (IBot interface)
**Complexity:** Medium
**Breaking changes:** Additive only (new interface member)
**Status:** Completed

---

## Problem

There is no way for plugins to inspect operational statistics about the bot.
Common needs include:

- Uptime (time since connection was established)
- Bytes and lines sent/received
- Commands executed (handler invocations)

Operators want dashboards, status commands, and monitoring plugins. Today
each plugin would have to track its own subset of these numbers independently,
and most (bytes/lines on the wire) aren't even observable from plugin code.

## Changes

### 1. Define `IBotStatistics` in `Marv.Core.Platform`

```csharp
/// <summary>
/// Read-only view of operational statistics for the current connection.
/// All counters reset when the bot reconnects. All properties are
/// thread-safe and may be read from any thread at any time — this is
/// important for OpenTelemetry observable instrument callbacks which
/// run on arbitrary threads.
/// </summary>
public interface IBotStatistics
{
    /// <summary>When the current connection was established (UTC).</summary>
    DateTimeOffset ConnectedAt { get; }

    /// <summary>Time elapsed since the connection was established.</summary>
    TimeSpan Uptime { get; }

    /// <summary>Total bytes received from the server.</summary>
    long BytesReceived { get; }

    /// <summary>Total bytes sent to the server.</summary>
    long BytesSent { get; }

    /// <summary>Total IRC lines received from the server.</summary>
    long LinesReceived { get; }

    /// <summary>Total IRC lines sent to the server.</summary>
    long LinesSent { get; }

    /// <summary>Total handler invocations (commands, events, regex matches, etc.).</summary>
    long HandlersInvoked { get; }
}
```

### 2. Add `Statistics` property to `IBot`

```csharp
/// <summary>Operational statistics for the current connection.</summary>
IBotStatistics Statistics { get; }
```

### 3. Implement `BotStatistics` in `Marv.Core.Irc`

An internal mutable class that `IrcBot` owns. Uses `Interlocked` for
thread-safe counter increments. Reset in `RunAsync` when a new connection
starts.

Wire up counters:
- **BytesReceived / BytesSent**: Increment in `IrcConnection`'s read/write
  loops, where the raw byte buffer length is known. Either expose mutable
  counters on `IrcConnection` that `IrcBot` reads, or pass the statistics
  object into `IrcConnection`.
- **LinesReceived**: Increment when a parsed `IrcMessage` is written to the
  inbound channel.
- **LinesSent**: Increment when an `IrcMessage` is written to the outbound
  channel (in `SendRawAsync`).
- **HandlersInvoked**: Increment in the handler dispatch loop (where
  `HandlerGroup.InvokeAsync` is called).
- **ConnectedAt**: Set once at connection establishment in `RunAsync`.
- **Uptime**: Computed as `DateTimeOffset.UtcNow - ConnectedAt`.

### 4. Register in DI

`IBotStatistics` is accessible via `IBot.Statistics`, so no separate DI
registration is needed. However, if plugins want to inject it directly
(e.g. services that don't have an `IBot` reference), register the
singleton in the service collection.

### 5. Update `MockBot` (Marv.Testing)

Ensure `MockBot.Create()` stubs `Statistics` to return a sensible default
(zeroed counters, `ConnectedAt` = `DateTimeOffset.UtcNow`).

### 6. Update PLUGIN_API.md

Add `Statistics` to the `IBot` property table in §5 and document
`IBotStatistics` in §14 (Platform Types).

## Design decisions

**Why a dedicated interface rather than individual properties on IBot?**
Grouping statistics into their own interface keeps `IBot` focused on actions
and state. It also makes it easy to snapshot, serialize (for a stats endpoint),
or mock independently.

**Why not use OpenTelemetry / `System.Diagnostics.Metrics` directly in
core?** The core should have zero dependency on OTel packages. Instead,
`IBotStatistics` exposes simple properties backed by `Interlocked` fields,
and a metrics plugin can wrap them with `ObservableCounter` /
`ObservableGauge` instruments:

```csharp
// In a metrics plugin — core has no OTel dependency
var meter = new Meter("Marv.Metrics");
meter.CreateObservableCounter("irc.lines.sent",
    () => _bot.Statistics.LinesSent);
meter.CreateObservableCounter("irc.lines.received",
    () => _bot.Statistics.LinesReceived);
meter.CreateObservableCounter("irc.bytes.sent",
    () => _bot.Statistics.BytesSent);
meter.CreateObservableCounter("irc.bytes.received",
    () => _bot.Statistics.BytesReceived);
meter.CreateObservableCounter("irc.handlers.invoked",
    () => _bot.Statistics.HandlersInvoked);
meter.CreateObservableGauge("irc.uptime.seconds",
    () => _bot.Statistics.Uptime.TotalSeconds);
```

This keeps the layering clean: core owns the counters, `IBotStatistics`
provides the read API, and any OTel/Prometheus export is a plugin concern.
The same properties serve simple use cases (`!stats` command) and
production observability (Prometheus scrape endpoint) without duplication.

All `IBotStatistics` properties are safe to read from any thread because
the backing fields use `Interlocked` — this matters because OTel
observable instrument callbacks run on the collection thread, not the
bot's event loop.

**Why reset on reconnect?** Per-connection statistics are simpler and more
useful than cumulative totals. Operators who want historical data can
persist snapshots from a plugin using `[OnInterval]`.

**Why not include per-command or per-plugin breakdowns?** That's a concern
for a metrics plugin. A plugin can create its own `Meter` with additional
instruments beyond what `IBotStatistics` provides. The core stats should
be connection-level aggregates that are cheap to maintain.

## Testing

- **Unit test:** Verify counter increments after sends/receives using
  `MockBot` or a test `IrcConnection`.
- **Unit test:** Verify counters reset on new connection.
- **Unit test:** Verify properties are readable from a non-bot thread
  (simulates OTel collection callback).
- **Integration test:** Connect to ngircd, exchange messages, check that
  `Statistics.LinesReceived > 0` and `Statistics.LinesSent > 0`.

## Impact

- **Plugin DX:** Plugins can build status commands, dashboards, and
  monitoring with zero protocol knowledge. A metrics plugin can expose
  all counters as OTel instruments without internal access.
- **API surface:** One new property on `IBot`, one new interface.
- **Performance:** Counter increments use `Interlocked`; negligible cost.
- **Dependencies:** No new dependencies in core. OTel packages are only
  needed by a metrics plugin.
