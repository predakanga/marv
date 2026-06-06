# CS-007: Plugin API Documentation — COMPLETED

**Source:** `downstream_suggestions/ai_enablers.md` §1, §2, §5, §7, §8
**Scope:** Documentation
**Complexity:** Medium
**Breaking changes:** N/A
**Status:** Completed

---

## Problem

Plugin authors (human and AI) must currently piece together the API from
multiple sources: `plugin-api-draft.md` (partially stale), `architecture.md`
(internal-focused), source code XML docs (scattered), and example plugins
(incomplete coverage). The draft doc has known inaccuracies (missing
`ILoggerFactory` in constructor, nonexistent test fakes, incorrect attribute
syntax).

The downstream project estimates ~2843 lines of reading to build a plugin
from scratch. A consolidated reference could reduce this to ~430 lines — a
6x reduction in context cost.

## Changes

### 1. Create `docs/PLUGIN_API.md` — quick reference

A single, authoritative document covering the full plugin API surface.
Structure:

1. **Minimal plugin skeleton** — compilable, copy-pasteable, including
   current constructor signature with `ILoggerFactory`
2. **MarvPlugin constructor signature** — exact, current
3. **Handler attributes table** — attribute → expected method signature →
   when it fires → available properties (including the new filter properties
   from CS-003)
4. **CommandContext / RegexMatchContext fields** — table of all properties
5. **IBot method reference** — table: method → signature → what it does
6. **Event type catalog** — table: event type → properties → when it fires
   (covers all 19 event types across connection, message, user, channel,
   batch categories)
7. **IrcColor / IrcFormat usage** — one stateful example, one wrap example
8. **Configuration pattern** — `[PluginConfig]` → `IOptions<T>` injection
9. **Service pattern** — `[ProvidesService]` → `ConfigureServices` → consumer
10. **Handler groups** — when to use, constructor injection via
    `IPluginActivator`
11. **Testing patterns** — how to create contexts, mock `IBot` with
    NSubstitute (or Marv.Testing builders if CS-006 is implemented)
12. **Available services catalog** — what's in the DI container by default
    vs. what plugins register

Target length: ~200-250 lines.

### 2. Fix or archive `plugin-api-draft.md`

Two options:

- **Option A (recommended):** Archive to `docs/archive/plugin-api-draft.md`
  with a note pointing to `PLUGIN_API.md`. Eliminates drift risk.
- **Option B:** Update in-place. Risk: two documents to maintain.

### 3. Event type catalog

Include as part of `PLUGIN_API.md` §6. Full table of all event types with
properties and trigger conditions. Based on current source in
`Marv.Core/Events/`:

| Category | Events |
|---|---|
| Connection | ConnectedEvent, ReadyEvent, DisconnectedEvent, CapabilitiesChangedEvent |
| Message | MessageEvent, NoticeEvent, ActionEvent, CtcpEvent |
| User | UserJoinedEvent, UserPartedEvent, UserKickedEvent, UserQuitEvent, NickChangedEvent, AccountChangedEvent, AwayChangedEvent, HostChangedEvent |
| Channel | TopicChangedEvent, ModeChangedEvent, InviteReceivedEvent |
| Batch | BatchStartEvent, BatchEndEvent |
| Raw | RawMessageEvent |

### 4. Available services catalog

Include as part of `PLUGIN_API.md` §12. Three tiers:

**Always available (registered by Marv host):**
- `IBot`, `IPluginActivator`, `ILoggerFactory`, `IOptions<T>`,
  `IHostApplicationLifetime`
- `IHttpClientFactory` (after CS-002)

**Available if registered by a plugin:**
- `IAuthorizationService` (from `Marv.Plugins.Auth`)

**Standard .NET services:**
- `ILogger<T>` (via `ILoggerFactory`)
- `IConfiguration` (from host)

### 5. Rename `plugin-api-draft.md`

If not archived (option B above), rename to `plugin-api.md` to signal that
it's authoritative, not provisional.

### 6. CLAUDE.md template for plugin projects

Add `docs/PLUGIN_PROJECT_CLAUDE.md` — a template that downstream projects
can copy into their repo when using Marv as a submodule or package reference.
Contents:

- Statement that Marv source is read-only
- Pointer to `PLUGIN_API.md`
- Quick constructor pattern (current, exact)
- Key `using` statements
- How to create a new plugin assembly

Target length: ~30 lines.

## Impact

- **AI token cost:** ~6x reduction in context needed to build a plugin.
- **Human DX:** Single reference document instead of cross-referencing
  multiple sources.
- **Maintenance:** One source of truth is easier to keep current than four.

## Maintenance note

`PLUGIN_API.md` must be updated whenever the plugin API changes. Consider
adding a note in `CLAUDE.md` (the project's AI instructions) requiring that
plugin API changes also update `PLUGIN_API.md`.
