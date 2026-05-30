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

### Service Discovery

**Provided services** are declared explicitly with a
`[ProvidesService(typeof(IFoo))]` attribute on the plugin class. The
plugin overrides the static `ConfigureServices(IServiceCollection)`
method defined on `IPlugin` (which has an empty default
implementation) to perform the actual DI registration. The attribute
tells the dependency sorter which plugin provides which service; the
method does the wiring.

**Consumed services** are discovered automatically by inspecting the
plugin's constructor parameters. Any parameter whose type is not a
core service (not `IBot`, `IOptions<T>`, `ILogger<T>`, etc.) is
assumed to be a plugin-provided service dependency:

- **Required**: Non-nullable parameter → the service must be
  registered by another plugin, or startup fails.
- **Optional**: Nullable parameter with a default of `null` → the
  service is used if available but does not block startup.

Only the providing side needs an attribute. The consuming side is
inferred entirely from constructor signatures — nullability and
default values express optionality naturally. This keeps the common
case (consuming a service) completely boilerplate-free, while keeping
the uncommon case (providing a service) explicit and unambiguous.

### Explicit Ordering

`[DependsOn(typeof(OtherPlugin))]` forces one plugin to load after
another without implying any service relationship. This is a
secondary mechanism for cases where the service graph alone doesn't
capture the needed ordering.

### Automatic Configuration

Configuration classes tagged with `[PluginConfig(Section = "Name")]`
are discovered during assembly scanning and automatically registered
as `IOptions<TConfig>` bound to `Plugins:{Section}`. No
`ConfigureServices` boilerplate is needed for the common case of a
plugin that only has configuration and event handlers. Plugins access
their configuration via constructor injection of `IOptions<TConfig>`.

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

**Why `[ProvidesService]` on the provider only**: The providing side
is the uncommon case — most plugins only consume services or handle
events. An explicit attribute here is low-cost and avoids the
alternatives (scanning `ConfigureServices` registrations via a
throwaway `ServiceCollection`, or scanning assemblies for interface
implementations — both fragile). The consuming side needs no attribute
because the dependency information already lives in the constructor
signature.

Optionality is inferred from nullability and default values: a
nullable parameter with `= null` is optional, everything else is
required. No Marv-specific attribute is needed on the consuming side.
If a plugin ever needs "nullable but required," `[DependsOn]` covers
that edge case.

**Why a single container**: One `IServiceProvider` for core and
plugins avoids type identity issues and complex cross-container
resolution. A plugin's `IAuthorizationService` is the same type
whether resolved by core code or another plugin.

## Consequences

- Plugin authors use familiar .NET DI patterns for most things.
- Most plugins (consumers) need zero Marv-specific attributes — just
  constructor parameters with standard C# nullability.
- Plugins providing services need `[ProvidesService]` and a
  `ConfigureServices` method — explicit but only for the uncommon
  case.
- The `ConfigureServices` method is static and runs before plugin
  instances exist, which means service registration cannot depend on
  runtime state. This is intentional — services should be
  deterministic based on configuration.
- The single-container approach means plugin assemblies must not have
  conflicting type definitions. This is enforced by using a shared
  `AssemblyLoadContext`.
