# CS-010: Expose Case Mapping to Plugins

**Source:** Plugin DX feedback
**Scope:** Core (IBot / IServerInfo interface)
**Complexity:** Small
**Breaking changes:** Additive only

---

## Problem

IRC nick and channel comparisons are case-insensitive, but the rules are
server-specific — the `CASEMAPPING` ISUPPORT token selects between `rfc1459`
(where `[]\` equal `{}|`), `strict-rfc1459` (also `^` equals `~`), and
`ascii` (A-Z only). The bot uses this internally for its `Channels` and
`Users` dictionaries, but plugins have no convenient way to perform the
same comparisons.

Currently a plugin that wants to compare two nicks or check channel
membership must either:

- Use `string.Equals(..., OrdinalIgnoreCase)` — **wrong** on rfc1459
  servers where `nick[away]` and `nick{away}` are the same user.
- Reach into `Marv.Core.Protocol.CaseMapping` — an internal namespace
  that plugin authors shouldn't need to know about.
- Use `IBot.Users` / `IBot.Channels` dictionary lookups as a workaround,
  which only works for exact-match lookups on known entities.

## Changes

### 1. Add a case-mapping comparer to `IBot`

```csharp
// On IBot:
IEqualityComparer<string> CaseComparer { get; }
```

Returns an `IEqualityComparer<string>` that uses the server's current
case mapping. Plugins use it for:

- **Equality checks:** `Bot.CaseComparer.Equals(nick1, nick2)`
- **Dictionaries:** `new Dictionary<string, T>(Bot.CaseComparer)`
- **HashSets:** `new HashSet<string>(Bot.CaseComparer)`

The comparer is already created internally by `IrcBot` (via
`CaseMapping.GetComparer`). This change simply exposes it.

### 2. Implement in `IrcBot`

`IrcBot` already calls `CaseMapping.GetComparer(_serverInfo.CaseMapping)`
in many places. Store the result and expose it via the `CaseComparer`
property. Update it when the server sends a new `CASEMAPPING` token
(rare but possible with some bouncers).

### 3. Update MockBot (Marv.Testing)

`MockBot.Create()` should set `CaseComparer` to return
`StringComparer.OrdinalIgnoreCase` (close enough for tests, and what
most servers approximate).

### 4. Update PLUGIN_API.md

Add `CaseComparer` to the IBot table in §5.

## Design decisions

**Why on `IBot` instead of `IServerInfo`?** `IBot` is the primary plugin
interface and already has `Channels`/`Users` dictionaries that use the
comparer. Exposing it alongside those is the natural location. Plugins
shouldn't need to think about where the comparer comes from — it's "the
way this bot compares IRC identifiers."

**Why `IEqualityComparer<string>` instead of helper methods like
`NickEquals(a, b)`?** The comparer is more general — plugins can use it
with standard .NET collections and LINQ. It's also the type the bot
already uses internally, so no new abstraction is needed.

**Why not also expose `CaseMapping.Fold()`?** Folding to lowercase is
occasionally useful, but it's less commonly needed than comparison, and
the comparer covers the primary use case. If demand arises, `Fold` can
be added later without breaking changes.

## Impact

- **Correctness:** Plugins that compare nicks/channels will use the
  right case mapping instead of guessing.
- **Simplicity:** One property, zero new types.
- **API surface:** 1 new property on `IBot`.
