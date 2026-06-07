# CS-017: Common HandlerContext Base Class — COMPLETED

**Source:** `TODO.md` item 7
**Scope:** Marv.Core.Plugin
**Complexity:** Small-Medium
**Breaking changes:** Source-compatible, binary-breaking (see Impact)
**Status:** Completed

---

## Problem

`CommandContext` and `RegexMatchContext` share five identical properties
(`Channel`, `Sender`, `IsDirect`, `RawMessage`, `Bot`) and an identical
`ReplyAsync` method. This duplication means:

- Filter evaluators that need the sender or channel must cast
  `HandlerInvocation.Context` to each type separately (see the
  `FilterHandlerAsync` advanced example in CS-005, which switches on
  `CommandContext` vs `RegexMatchContext`).
- Any future context type (e.g., for a webhook or scheduled-message
  handler) must copy the same properties.
- The `Marv.Testing` builders (`CommandContextBuilder`,
  `RegexMatchContextBuilder`) duplicate the same fluent methods.

## Decisions

- Introduce an abstract `HandlerContext` base class with the shared
  properties and `ReplyAsync`.
- `CommandContext` and `RegexMatchContext` inherit from `HandlerContext`.
- `HandlerContext` is not sealed — downstream projects can subclass it for
  custom handler types.
- The `HandlerInvocation.Context` property type remains `object?` (not
  `HandlerContext?`) because interval and raw-message handlers do not
  receive a context with sender/channel. Filter evaluators that want
  the common properties can pattern-match on `HandlerContext` instead
  of switching on each concrete type.

## Changes

### 1. Create `HandlerContext` base class

```csharp
namespace Marv.Core.Plugin;

/// <summary>
/// Base class for handler contexts that carry sender, channel, and message
/// information. Shared by <see cref="CommandContext"/> and
/// <see cref="RegexMatchContext"/>.
/// </summary>
public abstract class HandlerContext
{
    /// <summary>The channel the message was sent in, or null for DMs.</summary>
    public IChannel? Channel { get; init; }

    /// <summary>The user who sent the message.</summary>
    public required IUser Sender { get; init; }

    /// <summary>True if this is a direct (private) message to the bot.</summary>
    public bool IsDirect => Channel is null;

    /// <summary>The underlying IRC message.</summary>
    public required IrcMessage RawMessage { get; init; }

    /// <summary>The bot instance, used for sending replies.</summary>
    public required IBot Bot { get; init; }

    /// <summary>
    /// Sends a reply in context — to the channel if the message was in a
    /// channel, or directly to the sender if it was a private message.
    /// </summary>
    public Task ReplyAsync(string text, CancellationToken ct = default)
    {
        var target = Channel?.Name ?? Sender.Nick;
        return Bot.SendMessageAsync(target, text, ct);
    }
}
```

### 2. Update `CommandContext`

Remove the duplicated properties and inherit from `HandlerContext`:

```csharp
public sealed class CommandContext : HandlerContext
{
    /// <summary>The matched command name (without the prefix).</summary>
    public required string Command { get; init; }

    /// <summary>The remaining words after the command, split by whitespace.</summary>
    public required IReadOnlyList<string> Args { get; init; }

    /// <summary>The remaining text after the command, unparsed.</summary>
    public required string ArgString { get; init; }
}
```

### 3. Update `RegexMatchContext`

```csharp
public sealed class RegexMatchContext : HandlerContext
{
    /// <summary>The regex match result.</summary>
    public required Match Match { get; init; }
}
```

### 4. Update filter evaluators and examples

Filter evaluators can now use a single pattern match:

```csharp
// Before (two casts)
var sender = invocation.Context switch
{
    CommandContext cmd => cmd.Sender,
    RegexMatchContext regex => regex.Sender,
    _ => null
};

// After (one cast)
var sender = (invocation.Context as HandlerContext)?.Sender;
```

Update the Moderation example plugin's `RequireLevelEvaluator` and any
filter examples to use the simplified pattern.

### 5. Update `Marv.Testing` builders

Extract a shared base builder or add a helper that sets the common
properties. Both `CommandContextBuilder` and `RegexMatchContextBuilder`
should continue to work as-is since the properties they set still exist
(inherited from `HandlerContext`). No API changes to the builders are
required, but shared implementation can be extracted if desired.

### 6. Update `docs/PLUGIN_API.md`

Document the `HandlerContext` base class and the simplified filter
pattern-matching.

## Design decisions

**Why a base class instead of an interface?** The shared `ReplyAsync`
method contains logic (choosing between channel and DM targets). An
interface would require each implementer to duplicate this logic or use a
default interface method, which is less discoverable in C#. A base class
also enables `is HandlerContext` pattern matching.

**Why not change `HandlerInvocation.Context` to `HandlerContext?`?**
Interval handlers have no sender/channel context, and raw message handlers
receive an `IrcMessage` directly. Changing the type would require either
wrapping these in a `HandlerContext` subclass (adding unnecessary
ceremony) or making the property nullable with a different semantic. The
current `object?` type is accurate — filter evaluators pattern-match on
what they need.

**Why not include `ReplyAsync` only on concrete types?** Every context
that has a sender and channel should support replying. Putting `ReplyAsync`
on the base class means any future context type (webhook events, scheduled
messages) gets reply support automatically. The reply logic is
deterministic from the channel/sender state, so there's no reason for
subtypes to override it.

## Impact

- **Plugin API:** Adds `HandlerContext` abstract class. `CommandContext`
  and `RegexMatchContext` now extend it. This is **source-compatible** —
  existing plugin code that accesses properties on the concrete types
  compiles unchanged. It is **binary-breaking** — plugins compiled against
  the old types need recompilation. Since Marv is pre-1.0 and plugins are
  compiled from source, this is acceptable.
- **Filter evaluators:** Can simplify sender/channel extraction to a
  single `HandlerContext` cast instead of switching on each type.
- **Tests:** Existing tests compile unchanged. Add tests that verify
  `HandlerContext` properties are accessible from both context types.
