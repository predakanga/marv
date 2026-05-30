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

**Option 3: Hybrid approach with a single DI container and automatic
service discovery.**

There is one `IServiceCollection` and one `IServiceProvider` for the
entire application. Core services and plugin-contributed services all
live in the same container. There is no separate container for
plugins.

### Automatic Service Discovery

Service relationships are inferred from the code — no explicit
`[ProvidesService]` or `[ConsumesService]` attributes are required:

**Provided services** are discovered by scanning each plugin's static
`ConfigureServices(IServiceCollection)` method. The plugin loader
inspects the registrations to determine which service types a plugin
contributes to the container. Plugins that don't provide services
don't need this method.

**Consumed services** are discovered by inspecting the plugin's
constructor parameters. Any parameter whose type is not a core
service (not `IBot`, `IOptions<T>`, `ILogger<T>`, etc.) is assumed to
be a plugin-provided service dependency:

- **Required**: Non-nullable parameter → the service must be
  registered by another plugin, or startup fails.
- **Optional**: Parameter marked with `[OptionalService]`, nullable
  type, and a default of `null` → the service is used if available
  but does not block startup.

### Explicit Ordering

`[DependsOn(typeof(OtherPlugin))]` forces one plugin to load after
another without implying any service relationship. This is a
secondary mechanism for cases where the service graph alone doesn't
capture the needed ordering.

### Automatic Configuration

Plugins extending `MarvPlugin<TConfig>` have their configuration type
automatically registered as `IOptions<TConfig>` bound to
`Plugins:{PluginName}`. No `ConfigureServices` boilerplate is needed
for the common case of a plugin that only has configuration and event
handlers.

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

**Why automatic discovery over explicit attributes**: Requiring
`[ProvidesService]` and `[ConsumesService]` attributes creates
redundancy — the information already exists in `ConfigureServices`
registrations and constructor parameters. Inferring it automatically:

- Reduces boilerplate for plugin authors
- Eliminates the risk of attributes getting out of sync with the
  actual code
- Makes the common case (no services, or simple service consumption)
  require zero ceremony

The `[OptionalService]` attribute remains because optionality cannot
be reliably inferred from nullability alone — a plugin author might
use a nullable parameter for other reasons.

**Why a single container**: One `IServiceProvider` for core and
plugins avoids type identity issues and complex cross-container
resolution. A plugin's `IAuthorizationService` is the same type
whether resolved by core code or another plugin.

## Consequences

- Plugin authors use familiar .NET DI patterns for most things.
- Most plugins need zero attributes — just constructor parameters and
  optionally a `ConfigureServices` method.
- The `[OptionalService]` attribute is the only Marv-specific
  attribute needed for service consumption.
- The `ConfigureServices` method is static and runs before plugin
  instances exist, which means service registration cannot depend on
  runtime state. This is intentional — services should be
  deterministic based on configuration.
- The single-container approach means plugin assemblies must not have
  conflicting type definitions. This is enforced by using a shared
  `AssemblyLoadContext`.
- The automatic discovery of provided services requires inspecting
  `ConfigureServices` registrations, which may involve reflection or
  a convention-based approach. The exact mechanism is an
  implementation detail.
