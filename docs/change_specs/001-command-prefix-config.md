# CS-001: Command Prefix Configuration

**Source:** `downstream_suggestions/improvements.md` §4
**Scope:** Marv.Core
**Complexity:** Small
**Breaking changes:** None

---

## Problem

`MarvConfiguration.CommandPrefix` already exists (defaults to `"!"`) but is
unused. Command parsing in `MarvPlugin.DispatchCommandHandlers` (line 167) is
hardcoded to `'!'` with a TODO comment. Downstream projects that want a
different prefix (e.g. `.`) must use `[OnRegex]` workarounds.

## Changes

### 1. Wire up the existing config property

In `MarvPlugin`, inject or access `MarvConfiguration.CommandPrefix` and use it
in `DispatchCommandHandlers` instead of the hardcoded `'!'`.

**`MarvPlugin.cs` — DispatchCommandHandlers:**

Replace:
```csharp
if (text.Length < 2 || text[0] != '!')
    return;
```

With:
```csharp
var prefix = Bot.Configuration.CommandPrefix;
if (text.Length < prefix.Length + 1 || !text.StartsWith(prefix, StringComparison.Ordinal))
    return;
```

And adjust the command/arg extraction slice indices accordingly (replace `1`
with `prefix.Length`).

### 2. Expose CommandPrefix on IBot or via DI

`MarvPlugin` currently accesses `Bot` (an `IBot`), which does not expose the
configuration. Options:

- **Option A:** Add `CommandPrefix` property to `IBot`. Minimal surface; this
  is the only config property plugins need at runtime.
- **Option B:** Add `IOptions<MarvConfiguration>` as a constructor parameter
  to `MarvPlugin`. Heavier; exposes all config to all plugins.
- **Option C:** Inject `IOptions<MarvConfiguration>` in `MarvPlugin`
  internally via `IPluginActivator` during construction. No API change for
  plugin authors but adds hidden coupling.

**Recommendation:** Option A — add `string CommandPrefix { get; }` to `IBot`.
It's the most discoverable and keeps the plugin API clean. Plugin authors who
want to parse commands differently (multi-prefix, etc.) can read this property.

### 3. Support multi-character prefixes

The current implementation uses a single `char` comparison. Switching to
`string.StartsWith` (as shown above) naturally supports multi-character
prefixes like `!!` or `marv:`. No additional design work needed.

### 4. Multiple prefixes (deferred)

The downstream suggestion mentions supporting multiple prefixes. This can be
deferred — it's a nice-to-have that adds complexity (array config, iteration
in dispatch). If needed later, `CommandPrefix` could become `CommandPrefixes`
(string array) with a minor API evolution.

## Impact

- **Plugin API:** Adds `CommandPrefix` to `IBot` (additive, non-breaking).
- **Configuration:** Existing `CommandPrefix` property becomes functional.
  Default `"!"` preserves backward compatibility.
- **Tests:** Add test for custom prefix dispatch. Update any tests that
  depend on the hardcoded `'!'`.

## Open questions

1. Should the prefix be case-sensitive? Recommendation: yes (ordinal
   comparison), since IRC commands are conventionally case-sensitive after
   the prefix.
