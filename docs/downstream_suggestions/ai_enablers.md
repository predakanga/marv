# AI Enablers for Marv

Recommendations for making the Marv framework easier for AI code
generators (LLMs like Claude) to work with. Based on the experience
of building the exploratory plugin set — 16 plugins, ~2500 lines of
plugin code — using Claude Code with Marv as the target framework.

---

## Context: How an LLM Builds a Plugin

When an AI generates a Marv plugin from scratch, it follows roughly
this sequence:

1. Read CLAUDE.md to understand the project
2. Read framework documentation / source to learn the API surface
3. Read example plugins to see real usage patterns
4. Generate the plugin code
5. Compile and fix errors
6. Run tests and fix failures

Steps 2 and 3 are where token cost and errors concentrate. The more
precisely and concisely the framework communicates its API, the fewer
tokens are spent reading source code, and the fewer errors are
introduced through guesswork.

During the exploratory project, the primary friction points were:

- **Learning the constructor signature** — what parameters does a
  MarvPlugin subclass need to forward to `base()`? This changed over
  the course of the project (ILoggerFactory was added), and the docs
  (`plugin-api-draft.md`) still show the old signature without
  `ILoggerFactory`.
- **Discovering available events** — which event types exist and what
  properties do they have?
- **Understanding formatting** — how to use `IrcColor` and
  `IrcFormat` together (stateful vs. wrap-and-reset).
- **Test setup boilerplate** — creating `CommandContext`,
  `RegexMatchContext`, mock `IUser`/`IChannel`/`IBot` for tests.
- **API surface drift** — `plugin-api-draft.md` describes test fakes
  (`CommandContextFake`, `BotFake`) that don't actually exist.

---

## Recommendations

### 1. Add a Plugin Author's Quick Reference (PLUGIN_API.md)

**Problem:** The existing documentation is split across:
- `docs/plugin-api-draft.md` (most comprehensive, but stale in places)
- `docs/architecture.md` (detailed internals, mostly irrelevant to
  plugin authors)
- ADRs (decision rationale, not reference)
- Source code XML doc comments (complete but expensive to read)

An LLM building a plugin from scratch needs to read all of these to
piece together the API. The architecture doc alone is 600 lines, most
of which describe connection management, state tracking, and threading
— none of which a plugin author interacts with.

**Recommendation:** Create a single `PLUGIN_API.md` at the repo root
(or a prominent location in `docs/`) that serves as a quick reference.
This should be a compact, authoritative document that an LLM can read
in one pass and have everything it needs. Structure:

```
1. Minimal plugin skeleton (compilable, copy-pasteable)
2. MarvPlugin constructor signature (exact, current)
3. Available attributes (table: attribute → method signature → when it fires)
4. CommandContext / RegexMatchContext fields (table)
5. IBot method reference (table: method → what it does)
6. Event type catalog (table: event type → properties → when it fires)
7. IrcColor / IrcFormat usage (one stateful example, one wrap example)
8. Configuration pattern (PluginConfig → IOptions<T> injection)
9. Service pattern (ProvidesService → ConfigureServices → consumer constructor)
10. Handler groups pattern (when to use, constructor injection)
11. Testing pattern (how to create a context, mock IBot)
```

**Why this matters for AI:** An LLM's context window is finite. A 200-line
quick reference that covers the full API surface is dramatically cheaper
than reading 600 lines of architecture doc + 800 lines of plugin API
draft + source files. It also eliminates the drift problem — one source
of truth is easier to keep current.

**Why not just XML docs?** XML doc comments are excellent for IDE
tooltips and human browsing, but they're scattered across dozens of
files. An LLM reading `IBot.cs` learns the `IBot` methods but not
the event types. Reading `Attributes.cs` shows the attributes but not
the method signatures they expect. The quick reference puts it all in
one place.

### 2. Keep Documentation in Sync with Code

**Problem:** `plugin-api-draft.md` has several inaccuracies relative to
the current source:

| Doc says | Code actually does |
|---|---|
| `MarvPlugin(IBot bot, IPluginActivator activator)` | `MarvPlugin(IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory)` |
| Test fakes exist: `CommandContextFake`, `BotFake`, `UserFake`, `ChannelFake` | These types don't exist anywhere in the codebase |
| `[OnInterval(minutes: 5)]` | `[OnInterval(Minutes = 5)]` (property initializer, not constructor arg) |
| Some examples omit `ILoggerFactory` from plugin constructors | All real plugins pass `ILoggerFactory` to base |

An LLM that reads this doc and follows it will produce code that
doesn't compile. The compile-fix cycle then costs additional tokens.

**Recommendation:** Either update `plugin-api-draft.md` to match
current code, or replace it with the quick reference from
recommendation 1 and archive the draft. If both documents exist,
they will inevitably drift.

### 3. Add a Complete, Non-Trivial Example Plugin

**Problem:** The bundled example plugins (Greet, CannedResponses) are
good for demonstrating basics but don't cover patterns that real
plugins need:

| Pattern | Covered by examples? |
|---|---|
| `[OnCommand]` | Yes (Greet, CannedResponses) |
| `[OnEvent]` with join | Yes (Greet) |
| `[OnRegex]` | Yes (CannedResponses) |
| `[HandlerGroup]` | Yes (CannedResponses) |
| `[PluginConfig]` | Yes (Greet) |
| Service provision + consumption | Partially (Auth/AuthConsumer, but AuthConsumer is minimal) |
| DB or HTTP service injection | No |
| `[OnEvent]` with part/kick/quit | No |
| `[OnRawMessage]` | No |
| `[OnInterval]` | No |
| `[DependsOn]` | No |
| Channel-specific logic (only act in certain channels) | No |
| Direct message handling (`ctx.IsDirect`) | No |
| `Bot.SendRawAsync` with `IrcMessage` | No |
| `Bot.SendNoticeAsync` | No |
| `Bot.SendAndAwaitAsync` | No |
| Multiple `[OnCommand]` on one method | No |
| Testing with NSubstitute | Not in examples (only in test project) |

An LLM encountering one of the uncovered patterns has to read the
source code to figure it out, which is expensive and error-prone.

**Recommendation:** Add one "kitchen sink" example plugin (or a
focused set of 2-3) that demonstrates the gaps. A good candidate would
be a moderation plugin that:

- Uses `[OnCommand]` with aliases (multiple attributes)
- Checks `ctx.IsDirect` to reject DM usage
- Filters by channel name
- Uses `IUserAuthService` for authorization
- Handles `UserJoinedEvent` and `UserKickedEvent`
- Uses `Bot.SendRawAsync` to send MODE/KICK commands
- Uses `Bot.SendNoticeAsync`
- Uses `[OnInterval]` for periodic cleanup
- Has configuration for allowed channels
- Has a handler group separating admin commands from user commands
- Includes a test file demonstrating the NSubstitute test pattern

This single example would cover most of the gaps above and serve as
the go-to reference for AI-generated plugins.

### 4. Provide Test Infrastructure for Plugin Authors

**Problem:** `plugin-api-draft.md` promises test fakes:

> `Marv.Core` provides test fakes (`CommandContextFake`,
> `ChannelFake`, `UserFake`, `BotFake`) so plugin authors can unit
> test handlers without mocking infrastructure.

These don't exist. The actual test pattern (visible in Marv's own
`GreetPluginTests.cs`) requires:

```csharp
var bot = Substitute.For<IBot>();
var selfUser = Substitute.For<IUser>();
selfUser.Nick.Returns("Marv");
bot.Self.Returns(selfUser);
var activator = Substitute.For<IPluginActivator>();
// ... more boilerplate ...
var evt = new UserJoinedEvent
{
    Channel = channel,
    User = user,
    Timestamp = DateTimeOffset.UtcNow,
    RawMessage = DummyMessage  // required but irrelevant
};
```

Every test needs a dummy `IrcMessage` for the `RawMessage` required
property on every event. Every test needs to mock `IPluginActivator`
even though it's only used internally by `MarvPlugin`. The
`CommandContext` has 6 required properties where most tests only care
about 2-3.

**Recommendation:** Ship a `Marv.Testing` package (or namespace within
`Marv.Core`) with builder/factory helpers:

```csharp
// Instead of 15 lines of NSubstitute setup:
var (plugin, bot) = PluginTestHarness.Create<GreetPlugin>(config);
var ctx = CommandContextBuilder.Create("hello")
    .InChannel("#test")
    .From("alice")
    .Build();
```

This reduces test setup from ~20 lines to ~3 lines per test, which
matters both for human maintainers and for AI — less boilerplate
means fewer opportunities for an LLM to get setup wrong.

### 5. Document the Event Type Catalog

**Problem:** To handle an event, a plugin author needs to know:
1. What event types exist
2. What properties each event has
3. When each event fires

This information is only available by reading the source files in
`Marv.Core/Events/`. There are 15 event types across 5 files. An LLM
must read all 5 files (~200 lines total) to learn the event catalog.

**Recommendation:** Add an event catalog table to the quick reference:

```
| Event Type            | Properties                                      | When                           |
|-----------------------|-------------------------------------------------|--------------------------------|
| MessageEvent          | Channel?, Sender, Text, IsDirect, ReplyTo       | PRIVMSG received               |
| NoticeEvent           | Channel?, Sender, Text, IsDirect                | NOTICE received                |
| ActionEvent           | Channel?, Sender, Text, IsDirect                | CTCP ACTION received           |
| UserJoinedEvent       | Channel, User, Account                          | User joins a channel           |
| UserPartedEvent       | Channel, User, Reason                           | User parts a channel           |
| UserKickedEvent       | Channel, Kicker, Kicked, Reason                 | User is kicked from a channel  |
| UserQuitEvent         | User, Reason, AffectedChannels                  | User disconnects from IRC      |
| NickChangedEvent      | User, OldNick, NewNick                           | User changes nick              |
| ...                   |                                                 |                                |
```

This eliminates the need to read the source files entirely for event
discovery. The table is cheap to keep updated since events change
infrequently.

### 6. Add CLAUDE.md Guidance for Plugin Projects

**Problem:** Marv's `CLAUDE.md` is written for working on Marv itself,
not for building plugins against it. A plugin project that includes Marv
as a submodule needs its own CLAUDE.md that tells the AI:

- Marv is read-only; don't modify it
- Where to find the Marv API documentation
- The plugin constructor pattern (exact base() call signature)
- Which namespaces/types to `using`
- How to create a new plugin assembly

**Recommendation:** Add a `CLAUDE.md` template or section to the Marv
documentation specifically for plugin projects. This would contain:

```markdown
## Marv Plugin API (for AI assistants)

Marv is the bot framework. Do not modify Marv source.
Read `marv/docs/PLUGIN_API.md` for the complete plugin API reference.

### Quick constructor pattern
\```csharp
public class MyPlugin : MarvPlugin
{
    public MyPlugin(IBot bot, IPluginActivator activator,
        ILoggerFactory loggerFactory)
        : base(bot, activator, loggerFactory) { }
}
\```

### Key imports
- Marv.Core.Platform (IBot, IUser, IChannel)
- Marv.Core.Plugin (MarvPlugin, attributes, CommandContext)
- Marv.Core.Events (event types)
- Marv.Core.Formatting (IrcColor, IrcFormat)
- Marv.Core.Protocol (IrcMessage)
```

This gives an AI the essentials in 20 lines rather than requiring it
to read Marv's source.

### 7. Document "Services Available to Plugins"

**Problem:** A plugin's constructor can inject services from the DI
container, but there's no catalog of what's available. During the
exploratory project, discovering that `IHttpClientFactory`,
`IHostApplicationLifetime`, and `ILoggerFactory` were available
required either reading the Marv host setup or guessing-and-compiling.

**Recommendation:** Document the services that Marv registers by
default, split into:

**Always available (registered by Marv):**
| Service | How to inject | Purpose |
|---|---|---|
| `IBot` | Constructor param | Send messages, query state |
| `IPluginActivator` | Constructor param | Create handler group instances |
| `ILoggerFactory` | Constructor param | Create loggers |
| `IOptions<TConfig>` | Constructor param | Plugin configuration |
| `IHostApplicationLifetime` | Constructor param | Graceful shutdown |

**Available if registered by a plugin:**
| Service | Providing plugin | Purpose |
|---|---|---|
| `IAuthorizationService` | Auth | Permission checks |

**Available if the host registers them:**
| Service | Registration | Purpose |
|---|---|---|
| `IHttpClientFactory` | `services.AddHttpClient()` | HTTP requests |

### 8. Fix the `plugin-api-draft.md` Title

**Problem:** The file is called `plugin-api-draft.md` with "draft" in
the name, which signals to both humans and AI that it may be incomplete
or inaccurate. An AI reading this will (correctly) give it less weight
than a file named `plugin-api.md`.

**Recommendation:** Either rename it to `plugin-api.md` after updating
it, or supersede it with the quick reference from recommendation 1.

---

## Cost Analysis

To illustrate the token cost difference, here's a rough estimate of
what an LLM needs to read today vs. with these changes:

### Today (building a plugin from scratch)

| Source | Lines | Purpose |
|---|---|---|
| `plugin-api-draft.md` | 805 | Primary API reference (partially stale) |
| `architecture.md` | 600 | Needed to find gaps in API draft |
| `MarvPlugin.cs` | 440 | Verify actual constructor / dispatch logic |
| `Attributes.cs` | 117 | Verify attribute API |
| `CommandContext.cs` | 46 | Verify context properties |
| `RegexMatchContext.cs` | 41 | Verify regex context properties |
| `IBot.cs` | 55 | Verify bot API |
| `Events/*.cs` (5 files) | ~200 | Discover event types |
| `IrcColor.cs` | 116 | Formatting API |
| `IrcFormat.cs` | 98 | Formatting API |
| `IrcFormatExtensions.cs` | 33 | Formatting API |
| Example plugins (4 files) | ~150 | Usage patterns |
| Example tests (1 file) | 142 | Test patterns |
| **Total** | **~2843** | |

### After (with quick reference + updated examples)

| Source | Lines | Purpose |
|---|---|---|
| `PLUGIN_API.md` | ~200 | Complete API quick reference |
| Example plugin (1 non-trivial) | ~150 | Real-world patterns |
| Example test (1 file) | ~80 | Test patterns |
| **Total** | **~430** | |

That's roughly a **6x reduction** in context needed, with higher
accuracy (no stale docs to reconcile against source).

---

## Summary — Priority Order

| # | Change | Effort | Impact |
|---|---|---|---|
| 1 | Plugin Author's Quick Reference | Medium | Very high — single source of truth, 6x token reduction |
| 2 | Fix doc/code drift | Small | High — eliminates compile errors from following docs |
| 4 | Test infrastructure helpers | Medium | High — reduces test boilerplate for all plugin authors |
| 6 | CLAUDE.md template for plugin projects | Small | Medium — instant context for AI in new projects |
| 3 | Non-trivial example plugin | Medium | Medium — covers pattern gaps |
| 5 | Event type catalog table | Small | Medium — eliminates source file reading |
| 7 | Available services catalog | Small | Low-medium — eliminates guesswork |
| 8 | Rename draft file | Trivial | Low — but easy win |
