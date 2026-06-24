# CS-035: Wildcard Plugin Loading — COMPLETED

**Source:** GitHub issue #11
**Scope:** Core / Host
**Complexity:** Medium
**Breaking changes:** None — existing exact-name behaviour is preserved; wildcards are additive
**Status:** Completed

---

## Problem

The `Plugins` configuration list requires every plugin to be named
individually. Downstream projects with many plugins find this tedious and
error-prone — adding a new plugin assembly to the plugin directory still
requires a config change. There is no way to say "load everything" or
"load everything except X".

## Changes

### 1. Add pattern expansion step in `PluginManager`

Add a new static method to `PluginManager` (where `ResolveRequestedPlugins`
and `DeduplicateDirectories` already live). This method expands
wildcard/glob entries against the metadata-scanned plugin names:

```csharp
internal static IReadOnlyList<string> ExpandPluginPatterns(
    IReadOnlyList<string> patterns,
    IReadOnlyList<PluginMetadata> allMetadata,
    ILogger? logger = null)
```

**Pattern rules (evaluated left-to-right):**

- A plain name (no `*` or `?` characters) is passed through as-is to
  `ResolveRequestedPlugins` for exact matching (preserving current
  behaviour including the "did you mean?" error).
- A glob pattern (contains `*` or `?`) is matched case-insensitively
  against all discovered plugin names using
  `FileSystemName.MatchesSimpleExpression`. Every match is added to
  the result set. An unmatched glob silently matches nothing (logged at
  Debug level).
- A negation pattern (prefixed with `!`) removes previously matched
  names. The remainder after `!` may be a plain name or a glob. For
  example, `["*", "!Slap"]` loads all plugins except `Slap`.
- Duplicate names are suppressed — each plugin appears at most once in
  the expanded result.

The expanded list of concrete plugin names is then passed to
`ResolveRequestedPlugins` as today.

### 2. Update `AddMarv` call site

In `MarvServiceExtensions.AddMarv`, after the metadata scan and before
`PluginManager.ResolveRequestedPlugins`, call `ExpandPluginPatterns`:

```csharp
var expandedPlugins = PluginManager.ExpandPluginPatterns(
    config.Plugins, allPluginMetadata, bootstrapLogger);

var resolvedPaths = PluginManager.ResolveRequestedPlugins(
    expandedPlugins, allPluginMetadata, bootstrapLogger);
```

### 3. Logging

- Log at Information level when a glob pattern matches one or more
  plugins, listing the matched names.
- Log at Information level when a negation pattern excludes plugins.
- Log at Debug level when a glob matches nothing.

### 4. Default behaviour unchanged

An empty `Plugins` list (`[]`) still loads nothing. Users who want all
plugins must explicitly set `["*"]`. This avoids accidentally loading
untested or unwanted plugins.

### 5. Unit tests

- Test that plain names pass through unchanged (backward compatibility).
- Test that `["*"]` expands to all discovered plugin names.
- Test that `["IdleRPG.*"]` matches only plugins with that prefix.
- Test that `["*", "!Slap"]` loads everything except `Slap`.
- Test that `["*", "!IdleRPG.*"]` excludes all `IdleRPG.*` plugins.
- Test that negation of an unmatched name is a no-op (no error).
- Test that an unmatched glob silently matches nothing.
- Test that duplicate names are suppressed.
- Test that evaluation order matters: `["!Slap", "*"]` loads everything
  including `Slap` (negation had nothing to remove when it ran).

## Design decisions

- **`!` prefix for negation, not `-`:** The `!` prefix avoids ambiguity
  with YAML list syntax where `-` is a list item marker. This was
  confirmed by the issue author.
- **`FileSystemName.MatchesSimpleExpression`:** Available in
  `System.IO.Enumeration` without external dependencies. Supports `*`
  and `?` wildcards, which covers the requested use cases without the
  complexity of full regex.
- **Unmatched globs are not fatal:** Unlike plain names (which are fatal
  when unmatched, to catch typos), globs matching nothing is expected
  and normal — e.g. `["IdleRPG.*"]` when no IdleRPG plugins are
  installed. Plain names retain the existing fatal-error-with-suggestions
  behaviour.
- **Left-to-right evaluation:** Simple and predictable. Matches how
  `.gitignore` and similar tools process patterns.

## Testing

- Unit tests for `ExpandPluginPatterns` covering all pattern types.
- Unit tests for interaction with `ResolveRequestedPlugins` (plain names
  still get "did you mean?" errors).
- Integration test: configure `["*"]`, place multiple plugin DLLs in the
  plugin directory, verify all are loaded.
- Integration test: configure `["*", "!Greet"]`, verify Greet is
  excluded.
- Manual test: set `"Plugins": ["*"]` in config, verify all plugins in
  the directory are loaded.

## Impact

- **Plugin API:** No changes.
- **DX:** Users can now use `["*"]` to load all plugins, reducing
  boilerplate config. Negation patterns provide fine-grained exclusion.
- **Risk:** Low — plain name behaviour is unchanged; the new code path
  only activates when patterns contain `*`, `?`, or `!` characters.
