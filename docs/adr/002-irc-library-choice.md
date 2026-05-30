# ADR-002: IRC Library Choice

**Status**: Proposed  
**Date**: 2026-05-30

## Context

Marv needs to parse IRC messages, manage a TCP/TLS connection,
negotiate IRCv3 capabilities, and track channel/user state. We
evaluated whether to use an existing C# IRC library or build our own
protocol layer.

Five libraries were evaluated (see `docs/research.md` section 1):

| Library | Target | IRCv3 | Maintained | Verdict |
|---|---|---|---|---|
| ChatSharp | .NET Framework | Partial | No | Unsuitable |
| IRC.NET (IrcDotNet) | .NET Framework 4.0 | None | No | Unsuitable |
| NetIRC | .NET Standard 2.0 | None | Low | Unsuitable |
| IrcNet (NowaLone) | .NET 6+ | Unclear | Too new | Unsuitable |

## Decision

**Build our own IRC protocol layer**, consisting of:

1. An `IrcParser` for parsing raw messages into `IrcMessage` records,
   validated against the ircdocs/parser-tests test vectors.
2. An `IrcSerializer` for converting `IrcMessage` back to wire format.
3. A `CaseMapping` utility implementing RFC 1459, strict-RFC 1459, and
   ASCII case folding.
4. An `IIrcConnection` managing TCP/TLS, reconnection, rate limiting,
   and PING/PONG.
5. A `CapabilityEngine` for IRCv3 CAP negotiation and SASL
   authentication.
6. State tracking for channels, users, and modes.

We do not take a dependency on any existing IRC library.

## Rationale

**No suitable library exists.** The C# IRC library ecosystem is
fragmented between abandoned projects targeting legacy .NET and
immature projects with unclear IRCv3 coverage. None of the candidates
meet Marv's requirements:

- Target .NET 10 (or at least .NET Standard 2.1+)
- Comprehensive IRCv3 capability support (message-tags, labeled-
  response, batch, echo-message, etc.)
- Async/await-first API
- Active maintenance

**The IRC protocol is tractable to implement.** The wire protocol is
text-based with well-documented parsing rules. Community-maintained
test vectors (ircdocs/parser-tests) provide comprehensive coverage for
the parser. The hard parts — capability negotiation, state tracking,
mode parsing — are not handled well by any existing library anyway.

**Full control over the async model.** Building our own connection
layer lets us use `System.Threading.Channels` for internal message
passing and control the threading model precisely (see ADR-004).
Wrapping an existing library's event model would add friction and
limit our design options.

**No maintenance risk from upstream.** Given that every candidate
library is either abandoned or has zero community adoption, depending
on one would add risk rather than reduce it.

## Consequences

- We own the parser, connection management, and capability negotiation
  code. This is more initial work but eliminates external dependency
  risk.
- We must validate the parser against ircdocs/parser-tests and
  maintain correctness as the IRC protocol evolves.
- The common failure modes documented in `docs/research.md` section 5
  must be addressed in our implementation and test suite.
- If a high-quality C# IRC library emerges in the future, the
  protocol layer is internal to `Marv.Core` and could be replaced
  without affecting the plugin API.
