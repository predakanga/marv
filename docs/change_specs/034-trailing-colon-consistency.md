# CS-034: Always Use Colon on Trailing Parameters — COMPLETED

**Source:** GitHub issue #10
**Scope:** Core
**Complexity:** Small
**Breaking changes:** None — wire format change is backwards-compatible with all IRC parsers
**Status:** Completed

---

## Problem

`IrcSerializer.Serialize()` currently omits the `:` prefix on the last
parameter when it's not strictly required (i.e. when the parameter
contains no spaces, doesn't start with `:`, and is non-empty). While
this is technically correct per RFC 1459, some IRC software does not
handle the colon-less form correctly, causing interoperability issues.

## Changes

### 1. Always prefix the last parameter with `:`

In `IrcSerializer.Serialize()` (lines 55–61), replace the conditional
colon logic:

```csharp
// Last parameter needs trailing prefix if it contains spaces,
// starts with ':', or is empty
if (i == message.Parameters.Count - 1 &&
    (param.Contains(' ') || param.StartsWith(':') || param.Length == 0))
{
    sb.Append(':');
}
```

With an unconditional colon on the last parameter:

```csharp
if (i == message.Parameters.Count - 1)
{
    sb.Append(':');
}
```

This is a simplification — the code becomes shorter and the behaviour
more predictable.

### 2. Update serializer tests

Update `IrcSerializerTests` to expect the colon on all trailing
parameters. The "simple params without colon" test case will need
updating to expect e.g. `"FOO bar :baz"` instead of `"FOO bar baz"`.

The round-trip test should continue to pass since the parser already
handles both forms.

## Design decisions

- **Always-colon, not configurable:** Adding a toggle between strict
  and compatible modes would add complexity for negligible benefit.
  Always using the colon is universally compatible — every IRC parser
  must handle it. There is no practical reason to prefer the colon-less
  form.
- **No parser changes needed:** `IrcParser` already handles both forms
  correctly. Only the serializer output changes.

## Testing

- Unit test: verify all serialized messages with parameters include `:`
  before the last parameter.
- Unit test: round-trip (parse → serialize → parse) continues to
  produce equivalent messages.
- Manual test: connect to an IRC server and verify normal operation
  (JOIN, PRIVMSG, NICK, etc. all work correctly).

## Impact

- **Plugin API:** No changes. Plugins interact with `IrcMessage` objects,
  not raw wire format.
- **DX:** None — this is an internal protocol detail.
- **Risk:** Very low. The colon-prefixed form is the more common
  convention across IRC implementations and is guaranteed to be parsed
  correctly by all compliant software.
