# CS-002: Handler Dispatch Filters

**Source:** `downstream_suggestions/improvements.md` §1
**Scope:** Marv.Core
**Complexity:** Small
**Breaking changes:** None (additive properties on existing attributes)

---

## Problem

Nearly every command and regex handler begins with a channel/DM guard:

```csharp
if (ctx.IsDirect) return;          // channel-only handler
if (!ctx.IsDirect) return;         // DM-only handler
if (ctx.Channel?.Name != "#ops")   // channel-specific handler
    return;
```

This is repetitive boilerplate that the framework can handle declaratively.

## Changes

### 1. Add filter properties to `OnCommandAttribute`

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class OnCommandAttribute(string command) : Attribute
{
    public string Command { get; } = command;

    /// <summary>
    /// If true, handler only fires for channel messages (skips DMs).
    /// </summary>
    public bool ChannelOnly { get; init; }

    /// <summary>
    /// If true, handler only fires for direct/private messages (skips channel).
    /// </summary>
    public bool DirectOnly { get; init; }

    /// <summary>
    /// If set, handler only fires when the message is in this channel.
    /// Case-insensitive comparison using the server's case mapping.
    /// </summary>
    public string? Channel { get; init; }
}
```

### 2. Add the same properties to `OnRegexAttribute`

Same three properties: `ChannelOnly`, `DirectOnly`, `Channel`.

### 3. Apply filters in dispatch

**`MarvPlugin.DispatchCommandHandlers`** — after matching the command name
and before creating the `CommandContext`, check the filter properties:

```csharp
if (handler.ChannelOnly && msgEvt.Channel is null)
    continue;
if (handler.DirectOnly && msgEvt.Channel is not null)
    continue;
if (handler.Channel is not null
    && !string.Equals(msgEvt.Channel?.Name, handler.Channel,
        StringComparison.OrdinalIgnoreCase))
    continue;
```

Same logic in `DispatchRegexHandlers`.

### 4. Store filter values in registration records

The existing `CommandRegistration` and `RegexRegistration` records need the
filter values copied from the attribute at discovery time:

```csharp
private sealed record CommandRegistration(
    object Target, MethodInfo Method, string Command,
    bool ChannelOnly, bool DirectOnly, string? Channel);
```

### 5. Validation at discovery time

Warn (via logger) if both `ChannelOnly` and `DirectOnly` are set on the same
attribute — the handler would never fire. Similarly, warn if `Channel` is set
alongside `DirectOnly` (contradictory).

## Usage

```csharp
[OnCommand("ban", ChannelOnly = true)]
public async Task HandleBan(CommandContext ctx, CancellationToken ct) { ... }

[OnCommand("identify", DirectOnly = true)]
public async Task HandleIdentify(CommandContext ctx, CancellationToken ct) { ... }

[OnRegex(@"https?://\S+", Channel = "#links")]
public async Task HandleUrl(RegexMatchContext ctx, CancellationToken ct) { ... }
```

## Case mapping consideration

The `Channel` filter should ideally use the server's case mapping (via
`IServerInfo.CaseMapping`) for comparison rather than
`StringComparison.OrdinalIgnoreCase`. However, at handler discovery time the
server connection may not be established. Two options:

1. **Simple:** Use `OrdinalIgnoreCase`. Works for ASCII channel names (the
   vast majority). Document the limitation.
2. **Correct:** Defer comparison to dispatch time, reading case mapping from
   `Bot.ServerInfo`. Slightly more complex but handles edge cases like
   `{|}` ↔ `[|\]` mapping.

**Recommendation:** Start with option 1. The case mapping edge cases affect
channels with `{|}~` characters, which are rare. Can be refined later.

## Impact

- **Plugin API:** Additive properties on existing attributes. No existing
  code changes needed — default values (`false`/`null`) preserve current
  behavior.
- **Tests:** Add unit tests for each filter combination.
