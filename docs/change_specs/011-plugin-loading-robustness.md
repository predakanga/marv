# CS-011: Plugin Loading Robustness

**Source:** Developer experience feedback from downstream plugin authoring
**Scope:** Core (PluginDiscovery, PluginManager, MarvServiceExtensions)
**Complexity:** Medium-Large
**Breaking changes:** `CoreServiceTypes` removal (internal)
**Status:** Draft

---

## Problem

The plugin loading system is fragile in several ways that produce confusing
errors and surprising behavior. These have been observed during real plugin
development:

1. **Missing `CoreServiceTypes` entries cause false dependency errors.**
   Services like `IHttpClientFactory` are available in DI but not listed in
   `CoreServiceTypes`, so `PluginDiscovery` treats them as plugin-provided
   service dependencies. When no plugin declares `[ProvidesService]` for them,
   the dependency sorter can't find a provider and instantiation fails with a
   misleading "no plugin provides this service" error. Every new core/host
   service registration requires a parallel update to `CoreServiceTypes`.

2. **Assembly load order matters.** `ResolvePluginPaths` scans directories
   with `Directory.GetFiles` and feeds every `.dll` into
   `PluginManager.DiscoverAndRegister`. Assemblies are loaded into
   `AssemblyLoadContext.Default` in filesystem-enumeration order. If plugin B
   references a shared library in plugin A's directory, B may fail to load if
   the assembly resolver hasn't seen A's directory yet. The
   `RegisterAssemblyResolvers` handler should cover this, but it only probes
   `PluginDirectories` — if a shared assembly is not in a configured plugin
   directory, the resolver misses it.

3. **Duplicate plugin loading.** If the same plugin directory appears twice
   in `PluginDirectories` (e.g., via config layering where a JSON file and
   an environment variable both specify the same path), every DLL in that
   directory is loaded twice. This causes:
   - `[ProvidesService]` conflicts: "Service IFoo is provided by both
     'MyPlugin' and 'MyPlugin'."
   - Duplicate handler registrations: triggers fire twice.
   - Double `ConfigureServices` calls.

4. **All DLLs are loaded eagerly.** `ResolvePluginPaths` loads every `.dll`
   in every plugin directory (recursively), including dependency DLLs that
   are not plugins. This triggers assembly resolution for all transitive
   dependencies, which may fail if those dependencies aren't available. It
   also wastes time and memory.

5. **Opaque error messages.** When a required service is missing, the error
   says "Plugin 'Misc' requires service Example.Plugins.Common.IDbService,
   but no loaded plugin provides it." This is correct but unhelpful — common
   root causes include:
   - The providing plugin isn't listed in `Plugins` config.
   - The providing plugin has a `[PluginName]` that doesn't match the config.
   - The providing plugin failed to load (assembly error).
   - The service interface type doesn't match (version skew, different
     assembly).

6. **Plugin name matching is fragile.** The `Plugins` config list contains
   human-readable names (e.g., "Common"). These must exactly match either the
   `[PluginName]` attribute value or the class name minus "Plugin" suffix.
   A typo or naming mismatch silently results in the plugin not loading,
   with no warning.

## Changes

### 1. Replace `CoreServiceTypes` with DI container probing

**Problem:** `CoreServiceTypes` is a manually-maintained allowlist that must
be updated every time a new core service is registered. Forgetting an entry
causes false dependency errors.

**Solution:** Remove the `CoreServiceTypes` set and the `IsCoreService`
method entirely. Instead, during dependency analysis, classify a constructor
parameter as a "core/host service" (i.e., not a plugin dependency) if it is
registered in the `IServiceCollection` at the time of discovery.

`DiscoverAndRegister` already receives the `IServiceCollection`, so it can
check whether a type is already registered. All services previously
special-cased — `IBot`, `ICapabilityManager`, `IServerInfo`,
`IPluginActivator`, `ILoggerFactory`, `ILogger<T>`, `IOptions<T>`, and
`CancellationToken` — are registered in the service collection by either
the generic host or `AddMarv` before plugin discovery runs. No separate
allowlist is needed.

A constructor parameter that is *not* in the service collection is
classified as a plugin-provided dependency, same as today.

```csharp
// In PluginDiscovery, replace CoreServiceTypes + IsCoreService with:
private static bool IsCoreService(Type paramType, IServiceCollection services)
{
    // CancellationToken is not a DI service — it's passed at invocation time
    if (paramType == typeof(CancellationToken))
        return true;

    // Check if the service is already registered in the DI container
    return services.Any(sd => sd.ServiceType == paramType);
}
```

Note: `CancellationToken` is the sole special case because it is not a DI
service — it is passed at invocation time by the plugin manager. Everything
else is handled by the DI container probe.

### 2. Directory-based plugin discovery with deduplication

**Problem:** Plugins are loaded by scanning every `.dll` in plugin
directories, with no deduplication and no way to avoid loading non-plugin
DLLs eagerly.

**Solution:** Redesign `ResolvePluginPaths` with a two-phase approach:

**Phase 1 — Metadata scanning (no assembly loading):**

Use `System.Reflection.MetadataLoadContext` to inspect DLLs without loading
them into the runtime. For each `.dll` in the plugin directories (non-recursive,
since plugin directories are flat):

1. Open the assembly with `MetadataLoadContext`.
2. Check if it contains a type that has `MarvPlugin` as a base class, or
   that implements an interface with the full name
   `Marv.Core.Plugin.IPlugin`. (Type matching is by name since
   `MetadataLoadContext` types are not the same as runtime types.)
3. If yes, extract the plugin name. Read `CustomAttributeData` on the plugin
   type to find a `PluginNameAttribute` — the constructor argument's value
   is available via `ConstructorArguments[0].Value`. If no attribute is
   present, derive the name from the class name by stripping the "Plugin"
   suffix, same as the runtime logic.
4. Record the DLL path and plugin name.
5. Close the `MetadataLoadContext`.

This avoids loading non-plugin DLLs into the runtime. Only DLLs that
actually contain plugins are loaded in phase 2.

**Phase 2 — Selective loading:**

Load only the assemblies identified in phase 1 into
`AssemblyLoadContext.Default`. The runtime's assembly resolution handler
(already registered via `RegisterAssemblyResolvers`) handles transitive
dependency loading on demand.

**Deduplication:** Track scanned DLLs by their full path (resolved to
absolute, canonical form). If the same file would be scanned twice (due to
duplicate directory entries or symlinks), skip the duplicate and log a
debug message.

Additionally, deduplicate plugin directories themselves at the start:
normalize all paths to absolute form and remove duplicates.

```csharp
// Deduplicate plugin directories early
var uniqueDirs = pluginDirectories
    .Select(d => Path.GetFullPath(d))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList();
```

### 3. Improved error messages with diagnostic heuristics

**Problem:** Plugin loading errors are technically correct but don't help
the user figure out what went wrong.

**Solution:** When a plugin fails to load or a dependency is missing,
apply heuristics to produce actionable error messages. All plugin loading
failures are fatal — the bot must not start if it cannot provide the
plugins the user requested.

#### 3a. Missing service provider — suggest likely fixes

When a required service has no provider, search for near-matches:

```
FATAL: Plugin 'Moderation' requires service
  Example.Plugins.Common.IDbService
  but no loaded plugin provides it.

  Possible causes:
  - No loaded plugin declares [ProvidesService(typeof(IDbService))].
    Plugins loaded: Auth, Greet, CannedResponses.
  - Did you mean to load plugin 'ExampleCommon'? It is available in
    the plugin directories but not listed in the Plugins config.
    (Found: plugins/Marv.Plugins.Common.dll → plugin 'ExampleCommon')
```

To produce the "did you mean" suggestion, the metadata scan from change 2
provides a list of *all* discovered plugins (not just the ones in the
`Plugins` config list). When a required service is missing, check the
available-but-not-loaded plugins for one that provides the missing service
type.

#### 3b. Plugin name not found — fatal error with suggestions

When a name in the `Plugins` config doesn't match any discovered plugin,
this is a fatal error. List the available plugins and suggest close matches
(using simple string distance or prefix matching):

```
FATAL: Plugin 'Common' was requested in config but no plugin with that
  name was found.

  Available plugins in configured directories:
  - 'ExampleCommon' (from plugins/Marv.Plugins.Common.dll)
  - 'Auth' (from plugins/Marv.Plugins.Auth.dll)
  - 'Greet' (from plugins/Marv.Plugins.Greet.dll)

  Did you mean 'ExampleCommon'?
```

#### 3c. Assembly load failures — include context

When an assembly fails to load, include the path, the specific exception,
and suggestions:

```
FATAL: Failed to load plugin assembly: plugins/Marv.Plugins.Foo.dll
  System.IO.FileNotFoundException: Could not load file or assembly
  'SomeDependency, Version=1.0.0.0, ...'

  This usually means the plugin has a dependency that is not present
  in the plugin directories. Ensure 'SomeDependency.dll' is placed
  in one of the configured plugin directories.
```

#### 3d. Duplicate plugin — warn and skip

When the same plugin DLL would be loaded twice (due to deduplicated
directory overlap), warn clearly and skip the duplicate:

```
WARNING: Plugin 'Common' (from plugins/Marv.Plugins.Common.dll) was
  already loaded. Skipping duplicate. Check your PluginDirectories
  config for overlapping paths.
```

This is the one case that is a warning rather than a fatal error, since
deduplication handles it gracefully.

### 4. Plugin name resolution by convention

**Problem:** Users must know the exact plugin name (from `[PluginName]` or
the class-name-minus-suffix convention) to list it in config. There's no
way to discover what a plugin's name is without reading its source.

**Solution:** In addition to matching by plugin name, support matching by
assembly name convention. The metadata scan from change 2 produces a
mapping of `(assembly path, plugin name)`. When resolving which plugins to
load:

1. First, match by plugin name (exact, case-insensitive) — same as today.
2. If no match, try matching by assembly filename convention: strip the
   namespace prefix and `.dll` suffix. For example,
   `Marv.Plugins.CannedResponses.dll` → try matching "CannedResponses".
   If this matches, **log a warning** suggesting the user update their
   config to use the canonical plugin name, so config stays correct as
   assemblies are renamed.
3. If still no match, attempt a substring/prefix match against all known
   plugin names and assembly names. If a single close match is found,
   **do not load it** — this is a fatal error. Include the suggestion
   in the error message so the user can correct their config (see 3b
   above).

This means a user can write `Plugins: ["CannedResponses"]` instead of
needing to know whether the plugin class is named `CannedResponsesPlugin`
or has a `[PluginName("CannedResponses")]` attribute. But fuzzy/substring
matches never silently proceed.

### 5. Validate all requested plugins are found — fatal on failure

**Problem:** If a plugin name in the `Plugins` config doesn't match any
discovered plugin, it is silently ignored. The user gets no feedback that
their config is wrong.

**Solution:** After the metadata scan and name resolution, check that every
entry in the `Plugins` config was matched to a discovered plugin. Any
unmatched entry is a fatal error — the bot must not start. The error
message includes the list of available plugins and any close-match
suggestions (per 3b above).

This also means the existing logic in `PluginManager` for skipping failed
plugins and cascading to their dependents (`ShouldSkipDueToFailedDependency`,
`MarkPluginFailed`, `_failedPlugins`) should be removed. If a plugin that
the user requested fails to load or instantiate, the bot should not start.
Partial plugin loading creates confusing runtime behavior where some
commands work and others silently don't.

### 6. Assembly resolution improvements

**Problem:** The assembly resolver only probes directories listed in
`PluginDirectories`. If the bot is published as a single-file executable
(`PublishSingleFile`), assemblies bundled alongside the executable are not
in the plugin directories and may not be found by the default resolver.

**Solution:** The assembly resolver should probe directories in this order:

1. All configured `PluginDirectories` (existing behavior).
2. `AppContext.BaseDirectory` — the directory containing the Marv
   executable. This is important for `PublishSingleFile` deployments where
   the bot is a single executable but plugins and their dependencies may
   reference assemblies that ship alongside the executable.

Plugin directories are flat — there are no subdirectories to probe. The
resolver does not need to scan subdirectories within plugin directories.

```csharp
// Probe order for assembly resolution
var probeDirs = uniquePluginDirs
    .Append(AppContext.BaseDirectory)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList();
```

## Design decisions

**Why `MetadataLoadContext` instead of loading everything?**
`MetadataLoadContext` opens assemblies for inspection only — it doesn't
execute static constructors, resolve dependencies, or affect the runtime's
type system. This means we can safely inspect 50 DLLs in a plugin
directory to find the 3 that are actually plugins, without triggering
assembly resolution failures for the other 47. It also means we can read
plugin metadata (name, provided services) from DLLs that the user didn't
ask to load, enabling the "did you mean?" suggestions.

Reading `[PluginName]` from `MetadataLoadContext` is possible via
`CustomAttributeData`: the attribute's constructor arguments are available
as `ConstructorArguments[0].Value` without needing the attribute type to be
loaded in the runtime. Type matching is done by full name
(`Marv.Core.Plugin.PluginNameAttribute`) rather than by runtime type
identity.

The tradeoff is an additional dependency (`System.Reflection.Metadata`
package, which `MetadataLoadContext` depends on) and slightly more complex
code. This is worth it for the robustness gains.

**Why not separate `AssemblyLoadContext` per plugin?** Isolated load
contexts are the standard approach for plugin systems that need unloading
or version isolation. However, Marv's design requires plugins to share
service interfaces — plugin A provides `IAuthorizationService` and plugin B
consumes it. If they're in separate load contexts, the `IAuthorizationService`
type in A is a *different type* from the one B references, and DI resolution
fails. Since Marv doesn't need plugin unloading (non-goal) and uses a
single DI container (ADR-003), a shared `AssemblyLoadContext.Default` is
the right choice.

**Why deduplicate by resolved path rather than plugin name?** Two
different DLLs could legitimately have the same plugin name (though this is
a conflict that should be reported). Deduplicating by resolved file path
catches the specific case of the same file being scanned twice due to
overlapping directory configuration, without incorrectly treating
name-colliding plugins as duplicates.

**Why keep the `Plugins` config list as the source of truth for what to
load?** An alternative is to load all discovered plugins automatically.
This was rejected because:
- It's a security concern — placing a DLL in the plugin directory shouldn't
  automatically give it access to the bot.
- It makes the bot's behavior dependent on filesystem state rather than
  explicit configuration.
- It contradicts the principle of least surprise.

The `Plugins` list remains required. The improvements here make it easier
to *use correctly* by improving name matching and error messages.

**Why not require plugins to declare dependencies on other plugins?**
The current design (ADR-003) infers dependencies from constructor
signatures, which keeps the common case (consuming a service)
boilerplate-free. Adding a requirement for explicit `[DependsOn]` for
service dependencies would be redundant with the constructor analysis and
would add friction for plugin authors. The existing system works — the
issues are in the loading mechanics, not the dependency model.

**Why are all plugin loading failures fatal?** The bot is configured to
load specific plugins because the operator needs them. If a plugin fails to
load, the bot runs in a degraded state where some commands silently don't
work — this is more confusing than failing to start. The operator should
fix the config or the plugin before starting the bot. The previous behavior
of skipping failed plugins and their dependents created a cascade of
silent failures that was hard to diagnose.

**Why warn on assembly-name fallback matching?** When a user writes
`Plugins: ["CannedResponses"]` and we match it to plugin name
`CannedResponses` via the assembly filename `Marv.Plugins.CannedResponses.dll`,
the match is unambiguous and we proceed. But we log a warning with the
canonical name so the user can update their config. This keeps config
explicit and resilient to assembly renames.

## Implementation order

1. **Deduplication** (change 2, dedup portion) — immediate bug fix for the
   duplicate-loading issues.
2. **CoreServiceTypes replacement** (change 1) — removes the maintenance
   burden and fixes the IHttpClientFactory-class of bugs.
3. **Fatal error on unmatched plugins** (change 5) — removes the
   skip-failed-plugin logic.
4. **Metadata scanning** (change 2, MetadataLoadContext portion) — enables
   selective loading and powers the diagnostic messages.
5. **Plugin name validation and suggestions** (changes 3b, 4) — uses
   metadata scan results.
6. **Error message improvements** (changes 3a, 3c, 3d) — can be done
   incrementally.
7. **Assembly resolution improvements** (change 6) — independent, can be
   done in parallel with 4-6.

## Testing

- **Unit tests for deduplication:** Verify that duplicate directory entries
  and duplicate assembly paths result in a single plugin load.
- **Unit tests for `IsCoreService`:** Verify that host-registered services
  are correctly classified without `CoreServiceTypes`.
- **Unit tests for name matching:** Verify plugin name resolution by exact
  name, assembly convention, and case-insensitive matching.
- **Unit tests for fatal errors:** Verify that unmatched plugin names,
  missing service providers, and assembly load failures all prevent startup.
- **Unit tests for error messages:** Verify that missing-service errors
  include "did you mean" suggestions when an unloaded plugin provides the
  service.
- **Integration test:** Load plugins from a directory containing both
  plugin and non-plugin DLLs; verify only plugin DLLs are loaded into the
  runtime.

## Impact

- **Plugin authors:** No changes required. Existing plugins work as-is.
  Error messages become more helpful when something goes wrong.
- **Bot operators:** Duplicate directory entries no longer cause crashes.
  Mismatched plugin names produce helpful suggestions and a clear fatal
  error instead of silent failures. Non-plugin DLLs in plugin directories
  are no longer eagerly loaded. Misconfigured plugins now fail fast at
  startup instead of silently degrading at runtime.
- **Core maintainers:** `CoreServiceTypes` no longer needs to be kept in
  sync with host service registrations. Adding a new core service "just
  works."
- **API surface:** No changes to the public plugin API.
  `MarvConfiguration` is unchanged (same `PluginDirectories` and `Plugins`
  properties). Internal classes (`PluginDiscovery`, `PluginManager`,
  `MarvServiceExtensions`) are refactored but remain internal.
- **Behavioral change:** Plugin loading failures that previously resulted
  in degraded operation now prevent the bot from starting. This is
  intentional — fail-fast is better than silent degradation.
