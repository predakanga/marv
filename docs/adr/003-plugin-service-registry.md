# ADR-003: Plugin Service Registry

**Status**: Proposed  
**Date**: 2026-05-30

## Context

Marv's plugin system needs to support inter-plugin services: one
plugin provides an implementation (e.g., `IAuthorizationService`),
and other plugins consume it. We need to decide how services are
registered, discovered, and resolved.

Three approaches were evaluated (see `docs/research.md` section 4):

1. **.NET `IServiceProvider`** (Microsoft DI) — plugins register into
   `IServiceCollection`, the container is built once, dependencies are
   constructor-injected.

2. **Explicit service registry** — a custom `IServiceRegistry` where
   plugins call `Register<T>()` and `Get<T>()` at runtime.

3. **Hybrid** — Microsoft DI as the underlying mechanism, wrapped in
   a plugin-aware metadata layer for dependency sorting and
   diagnostics.

## Decision

**Option 3: Hybrid approach with a single DI container.**

There is one `IServiceCollection` and one `IServiceProvider` for the
entire application. Core services and plugin-contributed services all
live in the same container. There is no separate container for
plugins.

Plugins declare their service relationships via attributes:

- `[ProvidesService(typeof(IFoo))]` on the plugin class
- `[ConsumesService(typeof(IFoo), Required = true/false)]` on the
  plugin class
- `[DependsOn(typeof(OtherPlugin))]` for direct plugin dependencies

These attributes feed a metadata layer that:

1. Builds a dependency graph before the container is constructed
2. Topologically sorts plugins to determine load order
3. Detects cycles and missing required dependencies at startup
4. Provides diagnostic information (which plugin provides which
   service)

The actual service registration and resolution uses standard
`IServiceCollection` / `IServiceProvider` patterns. Plugins register
services in a static `ConfigureServices(IServiceCollection)` method
called in dependency order. Plugins receive dependencies via
constructor injection.

## Rationale

**Why not pure Microsoft DI (option 1)**: Microsoft DI alone does not
understand plugin identity or dependency ordering. It cannot answer
"which plugin provides this service?" or "in what order should plugins
load?" We need the metadata layer for:

- Correct load ordering (provider must register before consumer)
- Clear startup error messages ("ModerationPlugin requires
  IAuthorizationService, but no loaded plugin provides it")
- Diagnostics and introspection

**Why not a custom registry (option 2)**: A custom service locator
would duplicate functionality that Microsoft DI already provides
(lifetime management, constructor injection, scoping) and would be
unfamiliar to .NET developers. It also hides dependencies — consumers
call `registry.Get<T>()` at arbitrary points rather than declaring
them in the constructor.

**Why hybrid works**: The attributes provide the metadata needed for
ordering and diagnostics. The actual DI uses standard .NET patterns
that C# developers already know. Constructor injection keeps
dependencies explicit and testable.

**Why a single container**: One `IServiceProvider` for core and
plugins avoids type identity issues and complex cross-container
resolution. A plugin's `IAuthorizationService` is the same type
whether resolved by core code or another plugin.

### Optional Dependencies

For optional service dependencies, the consuming plugin declares the
constructor parameter as nullable with a default of `null`:

```csharp
public GreetPlugin(IAuthorizationService? auth = null) { ... }
```

Combined with `[ConsumesService(typeof(IAuthorizationService),
Required = false)]`, the dependency sorter treats this as an ordering
hint (if the provider is present, load it first) without requiring it
to be present.

Microsoft DI resolves unregistered services as `null` when the
parameter has a default value, which makes this pattern work naturally.

## Consequences

- Plugin authors use familiar .NET DI patterns for most things.
- The attribute-based metadata layer is Marv-specific and must be
  documented clearly.
- The `ConfigureServices` method is static and runs before plugin
  instances exist, which means service registration cannot depend on
  runtime state. This is intentional — services should be deterministic
  based on configuration.
- Adding new dependency relationship types (e.g., "load after but
  don't consume") requires adding new attributes, not changing the DI
  mechanism.
- The single-container approach means plugin assemblies must not have
  conflicting type definitions. This is enforced by using a shared
  `AssemblyLoadContext`.
