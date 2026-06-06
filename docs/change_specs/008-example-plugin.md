# CS-008: Non-Trivial Example Plugin

**Source:** `downstream_suggestions/ai_enablers.md` §3
**Scope:** Example plugins
**Complexity:** Medium
**Depends on:** CS-003 (handler filters), CS-005 (filter pipeline),
CS-006 (test infrastructure), CS-009 (bot action methods),
CS-010 (case mapping)
**Breaking changes:** None (new plugin)

---

## Problem

The bundled example plugins (Greet, CannedResponses) demonstrate basics but
don't cover patterns that real plugins need. Of the documented API patterns,
the examples only cover a subset. Plugin authors encountering uncovered
patterns must read source code, which is expensive and error-prone.

### Coverage gaps

| Pattern | Covered? |
|---|---|
| `[OnEvent]` with part/kick/quit | No |
| `[OnRawMessage]` | No |
| `[OnInterval]` | No |
| `[DependsOn]` | No |
| Channel-specific logic | No |
| Direct message handling (`ctx.IsDirect`) | No |
| `Bot.SendNoticeAsync` | No |
| `Bot.SendAndAwaitAsync` | No |
| Multiple `[OnCommand]` on one method | No |
| Handler dispatch filters (CS-003) | No |
| `IFilteringAttribute` / `FilterEvaluator<T>` (CS-005) | No |
| `Bot.KickAsync`, `SetModeAsync`, etc. (CS-009) | No |
| `Bot.CaseComparer` (CS-010) | No |
| Testing with `Marv.Testing` builders (CS-006) | Not in examples |

## Changes

### 1. Add `Marv.Plugins.Moderation` example plugin

A moderation-themed plugin that demonstrates the gaps. Structure:

**ModerationPlugin.cs** — main plugin class:
- `[OnCommand("kick", ChannelOnly = true)]` — kick a user via
  `Bot.KickAsync` (CS-009)
- `[OnCommand("ban", ChannelOnly = true)]` + `[OnCommand("b", ChannelOnly = true)]`
  on the same method — multiple aliases via stacked attributes, uses
  `Bot.SetModeAsync` for +b (CS-009)
- `[OnCommand("mute", ChannelOnly = true)]` — set +q mode via
  `Bot.SetModeAsync` (CS-009)
- `[OnEvent]` for `UserJoinedEvent` — welcome message via
  `Bot.SendNoticeAsync`
- `[OnEvent]` for `UserKickedEvent` — audit log
- `[OnInterval(Minutes = 5)]` — periodic cleanup of expired bans
- `[DependsOn(typeof(AuthPlugin))]` — depends on auth plugin for
  authorization checks
- Uses `Bot.CaseComparer` (CS-010) for nick comparisons in ban tracking
- Uses handler dispatch filters from CS-003 (`ChannelOnly = true`,
  `DirectOnly = true`)

**RequireAuthAttribute.cs** — custom filter attribute (CS-005):
- Implements `IFilteringAttribute` pointing to a `RequireAuthEvaluator`
- `RequireAuthEvaluator` extends `FilterEvaluator<RequireAuthAttribute>`
- Checks `IAuthorizationService` and sends a denial reply via `IBot`
- Applied to kick/ban/mute commands declaratively

**ModerationAdminCommands.cs** — handler group:
- `[HandlerGroup(typeof(ModerationPlugin))]`
- `[OnCommand("modstats", DirectOnly = true)]` — DM-only stats command
- `[OnRawMessage("INVITE")]` — auto-join on invite (demonstrates raw
  message handling)
- Uses `Bot.SendAndAwaitAsync` to query WHO information

**ModerationConfig.cs** — typed configuration:
- `[PluginConfig(Section = "Moderation")]`
- `AllowedChannels` — list of channels the plugin operates in
- `BanDurationMinutes` — default ban duration
- `AuditChannel` — channel for audit messages

**ModerationPluginTests.cs** — test file using `Marv.Testing` (CS-006):
- Uses `PluginTestHarness<ModerationPlugin>` for setup
- Uses `CommandContextBuilder` for command handler tests
- Uses `EventBuilder<T>` for event handler tests
- Uses `MockBot`, `MockUser`, `MockChannel` for mock objects
- Tests for: command handling, event handling, channel filtering,
  authorization filter (deny + allow), interval handler, case-insensitive
  nick tracking via `CaseComparer`

### 2. Update project structure

- Add to `Marv.slnx`
- The Makefile already discovers plugins via a `src/plugins/*/` glob, so
  no Makefile changes needed
- Add test project or tests within existing `Marv.Plugins.Tests`

## Design notes

- The plugin should be **realistic but self-contained** — no external
  dependencies (no HTTP, no database). It demonstrates API patterns, not
  production moderation logic.
- Keep the total size under ~250 lines for the plugin + ~150 lines for
  tests. Large enough to cover the gaps, small enough to read in one pass.
- Inline comments should explain the "why" of pattern choices, since the
  example serves as documentation.

## Impact

- **Pattern coverage:** Covers nearly all documented API patterns. The
  remaining gaps (service provision/consumption) are already covered by
  Auth/AuthConsumer.
- **AI context:** An LLM reading this one plugin + Greet + the
  `PLUGIN_API.md` reference covers all patterns needed for real-world
  plugin authoring.
