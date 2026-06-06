# CS-009: Bot Action Convenience Methods

**Source:** Plugin DX feedback
**Scope:** Core (IBot interface)
**Complexity:** Small-Medium
**Breaking changes:** Additive only (new interface members with default
implementations)

---

## Problem

Plugins that need to perform common IRC actions beyond messaging — kicking
users, setting modes, changing topics, sending invites — must construct raw
`IrcMessage` objects and call `SendRawAsync`:

```csharp
await Bot.SendRawAsync(new IrcMessage("KICK", [channel, nick, reason]), ct);
await Bot.SendRawAsync(new IrcMessage("MODE", [channel, "+o", nick]), ct);
await Bot.SendRawAsync(new IrcMessage("TOPIC", [channel, newTopic]), ct);
await Bot.SendRawAsync(new IrcMessage("INVITE", [nick, channel]), ct);
```

This is error-prone (parameter ordering varies by command, easy to forget
trailing parameters) and forces plugin authors to know IRC protocol details
that the bot should abstract away. The existing `JoinAsync`, `PartAsync`,
`SendMessageAsync` etc. demonstrate the expected abstraction level — these
actions are simply missing from it.

## Changes

### 1. Add action methods to `IBot`

```csharp
// Channel management
Task SetTopicAsync(string channel, string topic, CancellationToken ct);
Task InviteAsync(string nick, string channel, CancellationToken ct);

// Moderation
Task KickAsync(string channel, string nick, string? reason, CancellationToken ct);
Task SetChannelModeAsync(string channel, string modeString, CancellationToken ct);
Task SetChannelModeAsync(string channel, string modeString, string parameter, CancellationToken ct);

// Prefix-mode shortcuts (op, voice, etc.)
Task GiveOpAsync(string channel, string nick, CancellationToken ct);
Task RemoveOpAsync(string channel, string nick, CancellationToken ct);
Task GiveVoiceAsync(string channel, string nick, CancellationToken ct);
Task RemoveVoiceAsync(string channel, string nick, CancellationToken ct);

// Nick
Task ChangeNickAsync(string newNick, CancellationToken ct);
```

Each method constructs the correct `IrcMessage` internally and sends it
through the existing write pipeline (throttle-aware, thread-safe).

### 2. Implement in `IrcBot`

All methods delegate to the existing `SendRawAsync` path. No new protocol
handling or state tracking required — the inbound message processor already
handles the server's responses to these commands (TOPIC replies update
`IChannel.Topic`, MODE replies update `IChannel.Modes`, etc.).

`SetChannelModeAsync` takes a mode string like `"+b"` or `"-i"` plus an
optional parameter. For multi-mode changes (`"+bb mask1 mask2"`), plugins
can use the single-parameter overload with the full mode string, or call
`SendRawAsync` directly.

`GiveOpAsync` / `RemoveOpAsync` / `GiveVoiceAsync` / `RemoveVoiceAsync`
are thin wrappers around `SetChannelModeAsync` that look up the correct
mode character from `IServerInfo.Prefix`. Most servers use `o` for op and
`v` for voice, but the PREFIX ISUPPORT token is authoritative — some
servers use additional prefix modes like `a` (admin), `h` (halfop), or
`q` (owner). These four methods cover the two universally-supported
prefix modes. Plugins needing other prefix modes (halfop, etc.) can use
`SetChannelModeAsync` directly with the mode character from
`Bot.ServerInfo.Prefix`.

### 3. Update MockBot (Marv.Testing)

`MockBot.Create()` already returns an NSubstitute mock of `IBot`, so the
new methods are automatically stubbed. No changes needed unless we want
to add verification helpers.

### 4. Update PLUGIN_API.md

Add the new methods to the IBot table in §5.

## Design decisions

**Why not a fluent builder or ModeBuilder?** The raw
`SetChannelModeAsync(channel, modeString, param)` covers the common
single-mode case cleanly. Multi-mode changes are uncommon enough that
`SendRawAsync` is acceptable. A builder would add API surface without
proportional value.

**Why only op/voice shortcuts, not halfop/admin/owner?** Op (`o`) and
voice (`v`) are present in every server's PREFIX. Other prefix modes are
server-specific and less commonly needed. Plugins targeting those can use
`SetChannelModeAsync` with the mode character looked up from
`Bot.ServerInfo.Prefix`.

**Why not WHOIS/WHO?** These are query commands that return multi-line
responses. They're better served by `SendAndAwaitAsync` which already
handles labeled-response correlation. Adding typed wrappers for queries
is a separate concern.

**Default interface implementations?** No — `IBot` has a single
implementation (`IrcBot`), and default implementations would just call
`SendRawAsync` which is already on the interface. Implementing directly
in `IrcBot` is simpler and avoids the DIM complexity.

## Impact

- **Plugin DX:** Common moderation and channel management actions become
  single method calls instead of raw protocol construction.
- **Safety:** Correct parameter ordering is enforced by the method
  signature rather than relying on the plugin author to remember it.
- **API surface:** 10 new methods on `IBot`. All are thin wrappers.
