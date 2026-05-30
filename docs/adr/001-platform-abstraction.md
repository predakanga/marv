# ADR-001: Platform Abstraction

**Status**: Proposed  
**Date**: 2026-05-30

## Context

Marv targets IRC exclusively and does not need to support other chat
platforms. However, the raw IRC protocol is a poor API surface for
plugin authors — it requires knowledge of protocol details, manual
string parsing, and careful state tracking.

We need to decide how much abstraction to place between the IRC wire
protocol and the plugin API.

The spectrum of options:

1. **No abstraction**: Plugins work with raw `IrcMessage` objects and
   must parse parameters, track state, and handle protocol quirks
   themselves.

2. **Thin IRC-specific models**: Typed models for IRC concepts
   (channels, users, messages) that directly reflect IRC semantics
   but hide parsing details.

3. **Platform-agnostic abstraction**: Generic chat concepts
   (conversations, participants, messages) designed for
   multi-platform portability, as frameworks like Errbot do.

## Decision

**Option 2: Thin IRC-specific models.**

We model IRC's entities — channels, users, messages, modes,
capabilities — as first-class types with typed properties and
consistent APIs, but we do not abstract away IRC's semantics. A
channel is an `IChannel` (not a "conversation"), a user is an `IUser`
(not a "participant"), and capabilities are IRCv3 capabilities (not
generic "platform features").

Plugins work with these typed models in event handlers and queries.
Raw `IrcMessage` access is always available via the `RawMessage`
property on events, and plugins can subscribe to `RawMessageEvent` for
protocol-level handling.

## Rationale

**Why not option 1 (no abstraction)**: Forcing every plugin to parse
PRIVMSG parameters, track nick changes, handle case mapping, and
manage channel membership would violate the DX design goal. The same
state-tracking code would be duplicated across every non-trivial
plugin.

**Why not option 3 (platform-agnostic)**: Marv will only support IRC.
Building a platform-agnostic abstraction layer adds complexity without
delivering value:

- IRC-specific features (modes, prefixes, CTCP, capabilities) would
  need to leak through the abstraction anyway, creating a worst-of-
  both-worlds API.
- Plugin authors targeting Marv are writing for IRC. An abstraction
  that hides IRC semantics makes their job harder, not easier.
- If multi-platform support is ever desired, it would be better to
  build it as a separate project that composes protocol-specific bots,
  rather than burdening the IRC bot with abstractions it doesn't need.

**Why option 2 works**: IRC's concepts are well-defined and stable.
Channels, users, modes, and messages have clear semantics that map
directly to typed models. The abstraction removes boilerplate (parsing,
state tracking, case mapping) while preserving the mental model that
IRC-knowledgeable plugin authors already have.

## Consequences

- Plugin authors must understand IRC concepts (channels, nicks, modes)
  to be productive. This is acceptable because anyone writing an IRC
  bot plugin is expected to understand IRC.
- If Marv ever needs to support a non-IRC platform, the plugin API
  would need significant changes. We accept this tradeoff — YAGNI.
- The raw `IrcMessage` escape hatch ensures that no IRC functionality
  is inaccessible, even if the typed models don't cover every edge
  case.
- The typed models serve as a natural place to centralize correct
  handling of protocol quirks (case mapping, mode parsing, message
  length limits).
