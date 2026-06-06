# CS-011: Plugin Loading Robustness

**Source:** Developer experience feedback from downstream plugin authoring
**Scope:** Core (PluginDiscovery, PluginManager, MarvServiceExtensions)
**Complexity:** Medium-Large
**Breaking changes:** Config schema change (additive); `CoreServiceTypes` removal (internal)
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
   `PluginDirectories` — if a shared assembly lives alongside a plugin DLL
   but that DLL's parent directory wasn't listed as a plugin directory, the
   resolver misses it.

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

**Solution:** Remove the `CoreServiceTypes` set entirely. Instead, during
dependency analysis, classify a constructor parameter as a "core service"
if it meets any of these criteria:

- It is `CancellationToken`.
- It is a generic instantiation of `IOptions<>`, `ILogger<>`,
  `IOptionsSnapshot<>`, or `IOptionsMonitor<>`.
- It is `ILoggerFactory`.
- It is registered in the `IServiceCollection` at the time of discovery
  (i.e., it was registered by the host or core before plugin discovery
  runs).

The last criterion is the key change: `DiscoverAndRegister` already receives
the `IServiceCollection`, so it can check whether a type is already
registered. Any service the host has registered (e.g., `IHttpClientFactory`,
`IBot`, `IServerInfo`, `ICapabilityManager`) will be found automatically
without maintaining a separate list.

A constructor parameter that is *not* in the service collection and *not* a
well-known generic type is classified as a plugin-provided dependency, same
as today.

```csharp
// In PluginDiscovery, replace CoreServiceTypes with:
private static bool IsCoreService(Type paramType, IServiceCollection services)
{
    if (paramType == typeof(CancellationToken))
        return true;

    if (paramType.IsGenericType)
    {
        var def = paramType.GetGenericTypeDefinition();
        if (def == typeof(IOptions<>) || def == typeof(ILogger<>) ||
            def == typeof(IOptionsSnapshot<>) || def == typeof(IOptionsMonitor<>))
            return true;
    }

    if (paramType == typeof(ILoggerFactory))
        return true;

    // Check if the service is already registered in the DI container
    return services.Any(sd => sd.ServiceType == paramType);
}
```

### 2. Directory-based plugin discovery with deduplication

**Problem:** Plugins are loaded by scanning every `.dll` in plugin
directories, with no deduplication and no way to avoid loading non-plugin
DLLs eagerly.

**Solution:** Redesign `ResolvePluginPaths` with a two-phase approach:

**Phase 1 — Metadata scanning (no assembly loading):**

Use `System.Reflection.MetadataLoadContext` to inspect DLLs without loading
them into the runtime. For each `.dll` in the plugin directories:

1. Open the assembly with `MetadataLoadContext`.
2. Check if it contains a type implementing `IPlugin` (by checking for a
   type that has `MarvPlugin` as a base class, or that implements an
   interface named `Marv.Core.Plugin.IPlugin`).
3. If yes, extract the plugin name (from `[PluginName]` attribute or class
   name convention).
4. Record the path and plugin name.
5. Close the `MetadataLoadContext`.

This avoids loading non-plugin DLLs into the runtime. Only DLLs that
actually contain plugins are loaded in phase 2.

**Phase 2 — Selective loading:**

Load only the assemblies identified in phase 1 into
`AssemblyLoadContext.Default`. The runtime's assembly resolution handler
(already registered via `RegisterAssemblyResolvers`) handles transitive
dependency loading on demand.

**Deduplication:** Track loaded assemblies by their full path (resolved to
absolute, canonical form). If the same file would be loaded twice (due to
duplicate directory entries, symlinks, or overlapping recursive scans),
skip the duplicate and log a debug message.

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
apply heuristics to produce actionable error messages.

#### 3a. Missing service provider — suggest likely fixes

When a required service has no provider, search for near-matches:

```
ERROR: Plugin 'Moderation' requires service
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

#### 3b. Plugin name not found — suggest similar names

When a name in the `Plugins` config doesn't match any discovered plugin,
list the available plugins and suggest close matches (using simple string
distance or prefix matching):

```
WARNING: Plugin 'Common' was requested in config but no plugin with that
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
ERROR: Failed to load plugin assembly: plugins/Marv.Plugins.Foo.dll
  System.IO.FileNotFoundException: Could not load file or assembly
  'SomeDependency, Version=1.0.0.0, ...'

  This usually means the plugin has a dependency that is not present
  in the plugin directories. Ensure 'SomeDependency.dll' is placed
  alongside the plugin DLL.
```

#### 3d. Duplicate plugin — warn and skip

When a plugin would be loaded twice, warn clearly instead of throwing:

```
WARNING: Plugin 'Common' (from plugins/Marv.Plugins.Common.dll) was
  already loaded. Skipping duplicate. Check your PluginDirectories
  config for overlapping paths.
```

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
3. If still no match, try a substring/prefix match against all known plugin
   names and assembly names to produce a suggestion (see 3b above).

This means a user can write `Plugins: ["CannedResponses"]` instead of
needing to know whether the plugin class is named `CannedResponsesPlugin`
or has a `[PluginName("CannedResponses")]` attribute.

### 5. Validate all requested plugins are found

**Problem:** If a plugin name in the `Plugins` config doesn't match any
discovered plugin, it is silently ignored. The user gets no feedback that
their config is wrong.

**Solution:** After the metadata scan and name resolution, check that every
entry in the `Plugins` config was matched to a discovered plugin. For any
unmatched entries, log a warning with suggestions (per 3b above). If *all*
requested plugins are unmatched, log an error — the bot will start with no
plugins, which is almost certainly not intended.

### 6. Assembly resolution improvements

**Problem:** The assembly resolver only probes directories listed in
`PluginDirectories`. If a plugin's dependency lives in a subdirectory or
alongside the plugin DLL in a directory that wasn't explicitly listed,
resolution fails.

**Solution:** When loading a plugin assembly, also register its parent
directory as a probe path for the assembly resolver. This means if a
plugin DLL is at `plugins/MyPlugin/MyPlugin.dll`, its dependencies at
`plugins/MyPlugin/SomeDep.dll` will be found automatically.

The resolver should probe directories in this order:
1. The directory containing the plugin DLL being loaded.
2. All configured `PluginDirectories`.
3. The application's base directory (existing behavior from the default
   load context).

## Design decisions

**Why `MetadataLoadContext` instead of loading everything?**
`MetadataLoadContext` opens assemblies for inspection only — it doesn't
execute static constructors, resolve dependencies, or affect the runtime's
type system. This means we can safely inspect 50 DLLs in a plugin
directory to find the 3 that are actually plugins, without triggering
assembly resolution failures for the other 47. It also means we can read
plugin metadata (name, provided services) from DLLs that the user didn't
ask to load, enabling the "did you mean?" suggestions.

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

## Implementation order

1. **Deduplication** (change 2, dedup portion) — immediate bug fix for the
   duplicate-loading issues.
2. **CoreServiceTypes replacement** (change 1) — removes the maintenance
   burden and fixes the IHttpClientFactory-class of bugs.
3. **Metadata scanning** (change 2, MetadataLoadContext portion) — enables
   selective loading and powers the diagnostic messages.
4. **Plugin name validation and suggestions** (changes 3b, 4, 5) — uses
   metadata scan results.
5. **Error message improvements** (changes 3a, 3c, 3d) — can be done
   incrementally.
6. **Assembly resolution improvements** (change 6) — independent, can be
   done in parallel with 3-5.

## Testing

- **Unit tests for deduplication:** Verify that duplicate directory entries
  and duplicate assembly paths result in a single plugin load.
- **Unit tests for `IsCoreService`:** Verify that host-registered services
  are correctly classified without `CoreServiceTypes`.
- **Unit tests for name matching:** Verify plugin name resolution by exact
  name, assembly convention, and case-insensitive matching.
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
  Mismatched plugin names produce helpful suggestions instead of silent
  failures. Non-plugin DLLs in plugin directories are no longer
  eagerly loaded.
- **Core maintainers:** `CoreServiceTypes` no longer needs to be kept in
  sync with host service registrations. Adding a new core service "just
  works."
- **API surface:** No changes to the public plugin API.
  `MarvConfiguration` is unchanged (same `PluginDirectories` and `Plugins`
  properties). Internal classes (`PluginDiscovery`, `PluginManager`,
  `MarvServiceExtensions`) are refactored but remain internal.
