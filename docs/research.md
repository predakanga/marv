# Research Summary

Research conducted 2026-05-30 to inform the design of Marv, a C# IRC bot
targeting .NET 10. This document covers IRC libraries, IRCv3 capabilities,
existing bot architectures, plugin service patterns, and common failure modes.

---

## 1. C# IRC Libraries on NuGet

### Candidates Evaluated

#### ChatSharp

- **Repository**: <https://git.sr.ht/~sircmpwn/ChatSharp>
- **NuGet**: `ChatSharp` (v1.0.2)
- **License**: MIT
- **Target**: .NET Framework (not .NET Standard or modern .NET)
- **IRCv3 support**: Partial. Supports CAP negotiation, `msgid`, bot mode,
  `no-implicit-names`, SASL (v3.2), and STS. Does **not** support
  `message-tags`, `account-tag`, `labeled-response`, `echo-message`,
  `batch`, `server-time`, `multi-prefix`, `extended-join`, `away-notify`,
  `account-notify`, `cap-notify`, `invite-notify`, `setname`,
  `standard-replies`, `userhost-in-names`, or WHOX.
- **Maintenance**: Last commit appears to be several years old. The project
  is listed on the official IRCv3 libraries page but has not kept pace with
  the spec.
- **API quality**: Event-driven, no dependencies. Straightforward for basic
  bots but lacks async/await patterns and modern .NET idioms.
- **Verdict**: Unsuitable. Missing too many IRCv3 features, targets legacy
  .NET Framework, and appears unmaintained.

#### IRC.NET (IrcDotNet)

- **Repository**: <https://github.com/IrcDotNet/IrcDotNet>
- **NuGet**: `IrcDotNet`
- **License**: MIT
- **Target**: .NET Framework 4.0 / Silverlight 4.0 (with Mono support)
- **IRCv3 support**: None. Implements RFC 1459 and RFC 2812 only.
- **Maintenance**: Effectively abandoned. Multiple forks exist, none with
  significant activity. The original was designed for .NET Framework 4.0
  and Silverlight.
- **API quality**: Comprehensive RFC implementation, but the API is dated
  (event-based, no async/await, heavy use of `EventArgs` patterns).
- **Verdict**: Unsuitable. No IRCv3 support, unmaintained, legacy target
  framework.

#### NetIRC

- **Repository**: <https://github.com/fredimachado/NetIRC>
- **NuGet**: `NetIRC` (v1.1.2)
- **License**: MIT
- **Target**: .NET Standard 2.0 / .NET Framework 4.6.1
- **IRCv3 support**: None documented. Supports standard IRC commands
  (JOIN, PART, PRIVMSG, NICK, KICK, QUIT, TOPIC) and CTCP.
- **Maintenance**: Low activity, 4 stars, 2 forks.
- **API quality**: Async/await first, observable collections for UI binding,
  fluent builder pattern for configuration, custom message handler system.
  The cleanest modern API of the candidates, but limited scope.
- **Verdict**: Interesting API design to draw inspiration from, but no IRCv3
  support makes it unsuitable as a dependency.

#### IrcNet (NowaLone)

- **Repository**: <https://github.com/NowaLone/IrcNet>
- **NuGet**: Multiple packages (`NowaLone.IrcNet.Parser.V3`,
  `NowaLone.IrcNet.Parser.Rfc1459`, `NowaLone.IrcNet.Client`, etc.)
- **License**: MIT
- **Target**: .NET Standard 2.0+, .NET Framework 4.6.1+, .NET 6+
- **IRCv3 support**: Has a dedicated V3 parser and V3 client extensions
  package. Specific IRCv3 features supported are not well-documented.
- **Maintenance**: Very new (first release Jan 2025, latest May 2025). Only
  1 star, 0 forks. Essentially a single-developer project with no community
  adoption.
- **API quality**: Well-structured package decomposition (abstractions,
  parsers, client, extensions). Uses `IrcClientWebSocket` with
  `OnMessageReceived` events and `SendAsync`. The modular package structure
  is appealing but the WebSocket-based client is unusual for IRC.
- **Verdict**: Too immature and unproven. The package decomposition is
  interesting for reference, but adopting a library with zero community
  adoption and unclear IRCv3 coverage is risky.

### Recommendation

**None of the existing C# IRC libraries are suitable** for Marv's
requirements. The landscape is fragmented between abandoned projects
targeting legacy .NET (ChatSharp, IRC.NET) and immature projects with
unclear IRCv3 coverage (IrcNet/NowaLone).

**Recommended approach**: Build on a minimal IRC message parser (either our
own or adapted from the IRCv3-compliant parsing layer in IrcNet) and
implement connection management and capability negotiation ourselves. This
gives us:

- Full control over IRCv3 capability negotiation
- Modern .NET 10 / C# 13 idioms (async/await, `System.Threading.Channels`)
- No dependency on unmaintained or immature libraries
- Ability to use the [ircdocs/parser-tests](https://github.com/ircdocs/parser-tests)
  test vectors to validate our parser

The IRC protocol is text-based and relatively simple to parse correctly
(with known edge cases — see section 5). The hard part is capability
negotiation and state management, which none of the existing libraries
handle well enough anyway.

---

## 2. IRCv3 Capabilities Relevant to a Bot

### Must-Have Capabilities

| Capability | Why |
|---|---|
| `message-tags` | Foundation for all tag-based features. Allows receiving and sending arbitrary tags. |
| `server-time` | Accurate timestamps on messages. Essential for logging and event ordering. |
| `sasl` | Secure authentication to services (NickServ) during connection registration, before joining channels. Avoids race conditions with post-connect auth. |
| `multi-prefix` | Receive all prefix modes (e.g. `@+`) in NAMES/WHO, not just the highest. Required for accurate channel state tracking. |
| `account-tag` | Every message carries the sender's services account name. Enables account-based authorization in plugins without WHOIS queries. |
| `echo-message` | Server echoes our own PRIVMSGs/NOTICEs back. Essential for confirming message delivery and maintaining accurate channel logs. |
| `cap-notify` | Server notifies when capabilities become available/unavailable at runtime. Required for robust capability management. |
| `batch` | Groups related messages (e.g. WHOIS replies, netsplit/netjoin). Allows plugins to process related messages atomically. |
| `labeled-response` | Correlate sent commands with server responses. Critical for any command that expects a reply (e.g. WHO, WHOIS). Without this, responses from concurrent commands can be ambiguous. |
| `bot` (bot-mode) | Marks the bot with a `B` flag so users and servers know it is a bot. Increasingly required by networks. |

### Should-Have Capabilities

| Capability | Why |
|---|---|
| `account-notify` | Notified when users log in/out of services accounts. Keeps account-based auth state current without polling. |
| `away-notify` | Notified when users change away status. Useful for plugins that care about user presence. |
| `extended-join` | JOIN messages include the user's account name and realname. Eliminates WHOIS on join. |
| `invite-notify` | Notified when users are invited to channels the bot is in. |
| `userhost-in-names` | NAMES replies include full `nick!user@host` masks. Eliminates WHO queries for host info. |
| `chghost` | Notified when a user's host/ident changes (e.g. after cloaking). Keeps internal state accurate. |
| `setname` | Notified when a user changes their realname. |
| `standard-replies` | Servers can send standardized FAIL/WARN/NOTE responses. Better error handling. |
| `monitor` | Watch for specific nicks coming online/offline. Useful for admin notification plugins. |

### Not Relevant for Marv

| Capability | Why not |
|---|---|
| `chathistory` / `event-playback` | Marv explicitly does not handle historic messages. |
| `tls` (STARTTLS) | We will connect with TLS from the start, not upgrade. |
| `multiline` | Nice-to-have at best; most bot messages are short. |
| `read-marker` | Intended for multi-client users, not bots. |
| `message-redaction` | Not relevant for a bot that does not maintain chat history. |
| `metadata-2` | Not widely deployed yet. |
| `channel-rename` | Rare operation; basic handling sufficient. |

### Important Tags

| Tag | Purpose |
|---|---|
| `account` | Sender's account name (from `account-tag` cap). |
| `batch` | Batch identifier for grouped messages. |
| `bot` | Marks messages from bots. |
| `label` | Correlates requests with responses (from `labeled-response` cap). |
| `msgid` | Unique message ID assigned by the server. Useful for deduplication. |
| `time` | ISO 8601 timestamp (from `server-time` cap). |
| `+reply` | Client-to-client: marks a message as a reply to a specific `msgid`. |

---

## 3. Existing IRC Bot Architectures

### Sopel (Python)

Sopel is the most relevant reference for Marv's design, being a mature,
well-documented IRC bot framework with a strong plugin system.

**Plugin structure**:
- Plugins are Python modules containing decorated callables.
- Optional lifecycle hooks: `setup()` (called before connection),
  `shutdown()` (called after disconnection), `configure()` (interactive
  setup wizard).
- Callables receive `(bot, trigger)` — the bot object for sending messages
  and accessing state, and the trigger containing the matched message.

**Event handling**:
- Decorator-based: `@rule()` (regex match), `@command()` (named command),
  `@event()` (IRC event type), `@ctcp()` (CTCP messages).
- `@interval()` decorator for periodic tasks (no message trigger needed).
- Rate limiting decorators: `@rate()`, `@rate_user()`, `@rate_channel()`.

**Inter-plugin communication**:
- `bot.memory` — runtime dict-like shared memory. Any plugin can read/write.
  No formal contracts or typing; just convention.
- `bot.db` — persistent SQLite database. Plugins share tables.
- No formal dependency declaration between plugins. Plugins assume
  shared state exists or check for it.

**Access control**:
- Built-in decorators: `@require_admin()`, `@require_privilege()`,
  `@require_account()`, `@require_owner()`.
- Channel privilege integration for op/voice checks.

**Strengths**: Very easy to write plugins. Good decorator API. Mature.
**Weaknesses**: No typed inter-plugin contracts. Shared memory is
convention-based and fragile. No way to declare "this plugin requires
that plugin." Synchronous architecture.

### Limnoria / Supybot (Python)

Fork of Supybot, the original extensible Python IRC bot.

**Architecture**:
- Synchronous main loop (`drivers.run()` in a loop).
- Two driver types: Socket driver (IRC connection) and schedule driver
  (periodic tasks like cron).
- All messages flow through `irclib.Irc`, which maintains a callback
  registry of plugins.

**Plugin dispatch**:
- Uses `IrcCommandDispatcher` — method dispatch based on IRC command name
  (e.g., `doTopic()` handles TOPIC messages, `doPrivmsg()` handles PRIVMSG).
- `inFilter` / `outFilter` methods on plugins intercept messages before
  receipt / before sending.

**State management**:
- Plugins maintain their own state. No shared memory system like Sopel.
- Each plugin can have its own database.

**Strengths**: Well-tested, huge plugin ecosystem, granular ACL system.
**Weaknesses**: Synchronous, complex callback system, steeper learning
curve than Sopel, no typed inter-plugin services.

### Errbot (Python, multi-platform)

A chatbot framework supporting Slack, IRC, XMPP, Telegram, and more.

**Architecture**:
- Backend abstraction: each chat platform is a "backend" implementing a
  common interface. Plugins are platform-agnostic.
- Plugins inherit from `BotPlugin` and use `@botcmd` decorator.
- Built-in persistent storage: `self['key'] = value` (dict-like).
- Plugin management via chat commands (!repos install, !plugin enable).

**Relevance to Marv**: Errbot's backend abstraction is interesting as a
reference for how to separate protocol handling from plugin logic, even
though Marv only targets IRC. The `BotPlugin` base class with built-in
storage and command decorators is a good DX pattern.

### Chaskis (C#, .NET)

The only C# IRC bot framework found. Uses a plugin-based architecture.

- Last release: v0.31.0 (January 2021) — appears abandoned.
- Targets .NET Core.
- Plugin-based, but detailed architecture documentation is sparse.
- No evidence of IRCv3 support.

**Relevance**: Demonstrates that C# IRC bot frameworks exist but confirms
the ecosystem is immature. The wiki (if still accessible) may have useful
patterns.

### Key Design Patterns Across Frameworks

1. **Decorator/attribute-based event registration**: Every modern framework
   uses decorators (Python) or attributes (C#) to declare what events a
   handler responds to. This is clearly the right pattern.

2. **Bot object as service facade**: Plugins receive a `bot` object that
   provides sending capabilities and state access. This keeps plugins
   decoupled from protocol details.

3. **Configuration via bot object (anti-pattern)**: All studied frameworks
   have plugins access configuration through the bot object (e.g.
   `bot.config`, `self.config` from a base class). This couples plugins
   to the framework's configuration plumbing and makes configuration
   requirements implicit. **For Marv, we will not follow this pattern.**
   Instead, plugins will declare a typed configuration class, which will
   be injected via constructor — making configuration requirements
   explicit, validated at startup, and testable without a bot instance.

4. **Trigger/context object**: The matched message, parsed into a
   convenient form with sender info, channel, match groups, etc.

5. **Lifecycle hooks**: setup/teardown at plugin level, tied to bot
   lifecycle rather than individual messages.

6. **Missing pattern**: None of the frameworks studied have typed
   inter-plugin service contracts. They all use shared mutable state
   (dicts, databases) or assume co-residency. This is Marv's opportunity
   to do better.

---

## 4. Plugin Architectures with Inter-Plugin Services

### The Problem

We want plugins to be able to:
1. **Provide services** (e.g., an auth plugin exposes `IAuthorizationService`)
2. **Consume services** from other plugins (e.g., a moderation plugin uses
   `IAuthorizationService` to check permissions)
3. **Handle optional dependencies** gracefully (the moderation plugin works
   without auth, just with reduced functionality)
4. **Load in correct order** based on dependencies

### Approach 1: .NET `IServiceProvider` (Microsoft DI)

**How it works**: Plugins register services in a `IServiceCollection`
during a configuration phase. The DI container is built once. Plugins
receive dependencies via constructor injection.

**Pros**:
- Standard .NET pattern; familiar to C# developers
- Well-tested, high-performance implementation
- Supports scoped, singleton, and transient lifetimes
- Constructor injection makes dependencies explicit and testable

**Cons**:
- Container is built once and is immutable — adding services after build
  requires rebuilding the container
- Plugin load order must be determined before container build
- Optional dependencies are awkward (you must register a null/no-op
  implementation or use `IServiceProvider.GetService<T>()` which returns
  null)
- No built-in concept of "this service is provided by plugin X"

**Suitability**: Good foundation, but needs augmentation for plugin
lifecycle management.

### Approach 2: Explicit Service Registry

**How it works**: A custom `IServiceRegistry` managed by the bot core.
Plugins call `registry.Register<IAuthService>(this, implementation)` during
setup and `registry.Get<IAuthService>()` to consume. The registry
understands plugin identity and lifecycle.

**Pros**:
- Can track which plugin provides which service
- Can handle late registration and optional dependencies naturally
- Can provide diagnostics ("auth service provided by AuthPlugin v1.2")
- Can enforce single-provider-per-interface or allow multiple providers

**Cons**:
- Service locator anti-pattern (hides dependencies)
- Must be built and maintained ourselves
- Less familiar to .NET developers
- Testing requires mocking the registry

### Recommended Approach: Hybrid

Use Microsoft's `IServiceCollection` / `IServiceProvider` as the underlying
mechanism, but wrap it in a plugin-aware layer. **There is a single DI
container for the entire application** — core services (configuration,
logging, IRC client, bot) and plugin-contributed services all live in one
`IServiceCollection` which builds one `IServiceProvider`. There is no
separate container for plugins.

The startup sequence has two distinct phases:

1. **Bootstrap phase** (before DI): Read configuration from files,
   environment variables, and command-line arguments. Determine which
   plugin assemblies to load. This phase uses no DI — it runs before the
   container exists. The plugin list comes from core configuration read
   during bootstrap, so there is no circular dependency.

2. **DI phase** (building the single container):
   a. **Assembly loading**: Load plugin assemblies discovered during
      bootstrap, discover plugin types.
   b. **Dependency sort**: Topological sort based on declared dependencies
      (`[DependsOn(typeof(AuthPlugin))]` attributes on plugin classes)
      and constructor parameter inspection (non-core types are inferred
      as service dependencies; `[OptionalService]` marks optional ones).
   c. **Service registration**: Each plugin gets a chance to register
      services into the shared `IServiceCollection` via a static or
      type-level method (e.g. `ConfigureServices(IServiceCollection)`).
      This does not require constructing the plugin. Core services
      (configuration, logging, IRC client) are also registered here.
      Plugins are called in dependency order.
   d. **Container build**: The single `IServiceProvider` is built.
   e. **Plugin construction**: Plugin instances are resolved from the
      container — configuration and services injected via constructor.

3. **Runtime**: Plugins can query for optional services via
   `IServiceProvider.GetService<T>()` (returns null if not registered).

This gives us:
- A single, standard .NET DI container for the whole application
- Familiar .NET DI patterns for most use cases
- Explicit dependency declarations for load ordering
- Optional dependencies via nullable injection or runtime resolution
- Plugin identity tracking via a thin metadata layer on top

### Load Order and Optional Dependencies

**Topological sort**: Declare dependencies as attributes. Before loading,
build a dependency graph and topologically sort it. Cycles are a
configuration error. Missing required dependencies prevent startup.
Optional dependencies are ordered-if-present but don't block.

**Graceful degradation**: Plugins should declare whether a service
dependency is required or optional. Required = fail to load if not met.
Optional = load but potentially with reduced functionality. Optional
dependencies are expressed as nullable constructor parameters marked
with `[OptionalService]`.

---

## 5. Common Failure Modes in IRC Bot Implementations

Based on the [ircdocs/parser-tests](https://github.com/ircdocs/parser-tests)
test suite, the [Modern IRC documentation](https://modern.ircdocs.horse/),
implementation guides, and issue trackers of IRC libraries, the following
edge cases and failure modes are most common.

### Message Parsing

1. **Trailing parameter not treated as normal parameter**: Many parsers
   create a separate field for the trailing parameter (after `:`). The
   correct behavior is to always add it to the parameter list as a normal
   parameter. Creating a separate `trailing` field leads to bugs when
   commands have varying parameter counts.

2. **Tags on unexpected messages**: Once `message-tags` or `server-time` is
   enabled, ANY message from the server can have tags. Parsers that only
   handle tags on PRIVMSG will break on tagged CAP, AUTHENTICATE, JOIN,
   etc.

3. **Tag value escaping**: Backslash escaping in tag values follows specific
   rules (`\:` → `;`, `\s` → space, `\\` → `\`, `\r` → CR, `\n` → LF).
   A trailing backslash with no escape character should produce no output
   character. An invalid escape like `\b` should drop the backslash
   (producing `b`).

4. **Multiple spaces between parameters**: The spec says parameters are
   separated by "one or more" spaces. Parsers that split on single space
   will produce empty parameters. Parsers that are too strict will reject
   valid messages.

5. **Empty trailing parameter**: `:` with nothing after it is a valid empty
   trailing parameter, not a parse error.

6. **Source (prefix) parsing**: The source prefix `nick!user@host` can have
   missing components (server-originated messages have no `!user@host`).
   Parsers must handle `nick`, `nick@host`, `nick!user@host`, and bare
   server names.

### Case Mapping

7. **CASEMAPPING inconsistency**: Servers advertise their case mapping via
   ISUPPORT (`CASEMAPPING=rfc1459|ascii|strict-rfc1459`). Under `rfc1459`,
   `{`, `|`, `}` are the lowercase equivalents of `[`, `\`, `]`. Under
   `strict-rfc1459`, `~` and `^` are additionally equivalent. Under `ascii`,
   only A-Z/a-z are equivalent. Bots that use standard `ToLower()` for nick
   comparison will have subtle bugs. **This must be handled correctly for
   all nick and channel name comparisons**.

8. **CASEMAPPING can change**: The server sends ISUPPORT during registration,
   but it can technically resend it. The bot should use the server's
   advertised mapping, not a hardcoded default.

### Connection Management

9. **PING/PONG timeout**: Servers send PING periodically; failing to PONG
   within the timeout causes disconnection ("Ping timeout"). Bots must
   handle PING at the protocol level, not in a plugin.

10. **Excess flood**: Sending too many messages too quickly causes the server
    to disconnect the client ("Excess Flood"). Bots need rate limiting /
    message queue with throttling. A common approach is a token bucket or
    leaky bucket algorithm.

11. **Reconnection with backoff**: Network disruptions are common. Bots
    should reconnect with exponential backoff to avoid being banned for
    rapid reconnection. Some networks also require different nick on
    reconnection if the old session hasn't timed out (nick collision).

12. **Registration race conditions**: Without SASL, authenticating to
    NickServ after connecting creates a window where the bot is
    unauthenticated. During this window, joining channels with `+r`
    (registered-only) will fail. SASL solves this.

### Channel and User State

13. **MODE parsing complexity**: Channel modes have varying parameter counts
    depending on the mode type (A/B/C/D as defined in ISUPPORT
    `CHANMODES`). Parsing `MODE #channel +ov nick1 nick2` requires knowing
    that `o` and `v` are type B (always have a parameter). Getting this
    wrong causes state tracking to desynchronize.

14. **NAMES/WHO state initialization**: When joining a channel, the bot must
    process NAMES (RPL_NAMREPLY/RPL_ENDOFNAMES) to build the initial user
    list. With `multi-prefix`, each name can have multiple prefix
    characters. Without `userhost-in-names`, the bot only gets nicks (no
    hostmasks).

15. **Nick changes affecting state**: When a user changes nick, all channel
    membership records must be updated. This is a common source of state
    desynchronization.

16. **QUIT vs PART**: QUIT removes a user from all channels; PART removes
    from one. Bots that only handle PART will have stale user lists.

### Encoding

17. **Character encoding**: IRC has no standard encoding. Messages can be
    UTF-8, Latin-1, or arbitrary bytes. Bots should default to UTF-8 but
    handle invalid sequences gracefully (replace or skip, never crash).

### Protocol Quirks

18. **Numeric replies are strings**: Despite being called "numerics," IRC
    reply codes are transmitted as three-character strings (e.g., `"001"`),
    not integers. Leading zeros matter.

19. **Maximum message length**: IRC messages are limited to 512 bytes
    (including CR-LF), or 8191 bytes with the `message-tags` spec (tags
    occupy a separate 8191-byte budget from the 512-byte message budget).
    Bots must split messages that exceed these limits.

20. **CAP negotiation subtleties**: `CAP LS 302` (IRCv3.2) enables
    multi-line capability lists and capability values. Bots must handle
    both single-line (`CAP * LS :cap1 cap2`) and multi-line
    (`CAP * LS * :cap1 cap2` followed by `CAP * LS :cap3`) responses.
    The `*` indicates "more to follow."

### Test Resources

- **Parser test vectors**: <https://github.com/ircdocs/parser-tests> —
  YAML test files covering message splitting, tag parsing, mask matching,
  and hostname validation. Tests aggregated from Mozilla, grawity, and
  community contributions.
- **Modern IRC docs**: <https://modern.ircdocs.horse/> — living document
  describing the IRC protocol as actually implemented (as opposed to the
  dated RFCs).
- **Implementation guide**: <https://modern.ircdocs.horse/impl.html> —
  specifically addresses common implementation mistakes.

---

## Sources

- [IRCv3 Libraries](https://ircv3.net/software/libraries)
- [IRCv3 Registry](https://ircv3.net/registry)
- [IRCv3 Message Tags Spec](https://ircv3.net/specs/extensions/message-tags.html)
- [IRCv3 Labeled Response Spec](https://ircv3.net/specs/extensions/labeled-response.html)
- [IRCv3 Bot Mode Spec](https://ircv3.net/specs/extensions/bot-mode)
- [ChatSharp on SourceHut](https://git.sr.ht/~sircmpwn/ChatSharp)
- [IRC.NET on GitHub](https://github.com/IrcDotNet/IrcDotNet)
- [NetIRC on GitHub](https://github.com/fredimachado/NetIRC)
- [IrcNet (NowaLone) on GitHub](https://github.com/NowaLone/IrcNet)
- [Sopel Documentation](https://sopel.chat/docs/)
- [Sopel Plugin Anatomy](https://sopel.chat/docs/plugin/anatomy.html)
- [Limnoria Architecture](https://docs.limnoria.net/develop/architecture.html)
- [Errbot on GitHub](https://github.com/errbotio/errbot)
- [Chaskis on GitHub](https://github.com/xforever1313/Chaskis)
- [ircdocs/parser-tests](https://github.com/ircdocs/parser-tests)
- [Modern IRC Documentation](https://modern.ircdocs.horse/)
- [Modern IRC Implementation Guide](https://modern.ircdocs.horse/impl.html)
- [.NET Dependency Injection Guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines)
