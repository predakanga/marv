# CS-008: Non-Trivial Example Plugin

**Source:** `downstream_suggestions/ai_enablers.md` §3
**Scope:** Example plugins
**Complexity:** Medium
**Depends on:** CS-003 (handler filters), CS-005 (filter pipeline)
**Breaking changes:** None (new plugin)

---

## Problem

The bundled example plugins (Greet, CannedResponses) demonstrate basics but
don't cover patterns that real plugins need. Of 19 documented API patterns,
the examples only cover 7. Plugin authors encountering uncovered patterns
must read source code, which is expensive and error-prone.

### Coverage gaps

| Pattern | Covered? |
|---|---|
| `[OnEvent]` with part/kick/quit | No |
| `[OnRawMessage]` | No |
| `[OnInterval]` | No |
| `[DependsOn]` | No |
| Channel-specific logic | No |
| Direct message handling (`ctx.IsDirect`) | No |
| `Bot.SendRawAsync` with `IrcMessage` | No |
| `Bot.SendNoticeAsync` | No |
| `Bot.SendAndAwaitAsync` | No |
| Multiple `[OnCommand]` on one method | No |
| Handler dispatch filters (CS-003) | No (doesn't exist yet) |
| `IFilteringAttribute` usage (CS-005) | No (doesn't exist yet) |
| Testing with NSubstitute / Marv.Testing | Not in examples |

## Changes

### 1. Add `Marv.Plugins.Moderation` example plugin

A moderation-themed plugin that demonstrates the gaps. Structure:

**ModerationPlugin.cs** — main plugin class:
- `[OnCommand("kick", ChannelOnly = true)]` — kick a user (uses
  `Bot.SendRawAsync` with KICK command)
- `[OnCommand("ban", "b", ChannelOnly = true)]` — multiple aliases on one
  method
- `[OnCommand("mute", ChannelOnly = true)]` — set +q mode via
  `Bot.SendRawAsync`
- `[OnEvent]` for `UserJoinedEvent` — welcome message via
  `Bot.SendNoticeAsync`
- `[OnEvent]` for `UserKickedEvent` — audit log
- `[OnInterval(Minutes = 5)]` — periodic cleanup of expired bans
- `[DependsOn(typeof(AuthPlugin))]` — depends on auth plugin
- Uses handler dispatch filters from CS-003 (`ChannelOnly = true`)
- Uses `IFilteringAttribute` from CS-005 for authorization (if implemented)
  or inline auth checks as fallback

**ModerationAdminCommands.cs** — handler group:
- `[HandlerGroup(typeof(ModerationPlugin))]`
- `[OnCommand("modstats", DirectOnly = true)]` — DM-only stats command
- Uses `Bot.SendAndAwaitAsync` to query WHO information

**ModerationConfig.cs** — typed configuration:
- `[PluginConfig(Section = "Moderation")]`
- `AllowedChannels` — list of channels the plugin operates in
- `BanDurationMinutes` — default ban duration
- `AuditChannel` — channel for audit messages

**ModerationPluginTests.cs** — test file:
- Demonstrates NSubstitute test pattern (or Marv.Testing builders if
  CS-006 is available)
- Tests for command handling, event handling, channel filtering
- Tests for authorization (filter attribute or inline check)

### 2. Update example project structure

Ensure the moderation plugin is:
- Listed in the solution file (`Marv.slnx`)
- Included in the Makefile's build/copy targets
- Referenced in `PLUGIN_API.md` (CS-007) as the go-to example

## Design notes

- The plugin should be **realistic but self-contained** — no external
  dependencies (no HTTP, no database). It demonstrates API patterns, not
  production moderation logic.
- Keep the total size under ~250 lines for the plugin + ~100 lines for
  tests. Large enough to cover the gaps, small enough to read in one pass.
- Inline comments should explain the "why" of pattern choices, since the
  example serves as documentation.

## Impact

- **Pattern coverage:** Raises coverage from 7/19 to ~17/19. The remaining
  gaps (service provision/consumption with DB/HTTP) are already partially
  covered by Auth/AuthConsumer.
- **AI context:** An LLM reading this one plugin + Greet covers nearly all
  patterns needed for real-world plugin authoring.
