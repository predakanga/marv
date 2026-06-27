# CS-027: Idiomatic Configuration Loading — COMPLETED

**Source:** GitHub issue #4
**Scope:** Host
**Complexity:** Small-Medium
**Breaking changes:** None — existing `marv.json` and `MARV_` env var behaviour preserved
**Status:** Completed

---

## Problem

The host application (`Program.cs`) calls `builder.Configuration.Sources.Clear()`
and rebuilds the configuration stack from scratch, discarding the default .NET
providers (`appsettings.json`, `appsettings.{Environment}.json`, etc.). This is
surprising for users familiar with .NET conventions. Additionally,
`ConfigurationOptions` serves two purposes — generating CLI option definitions
and extracting overrides via `GetOverrides` — when it should only do the former.

## Changes

### 1. Preserve the default configuration stack

Stop calling `builder.Configuration.Sources.Clear()`. The default .NET JSON
configuration provider already supports comments and trailing commas, so no
source replacement is needed.

> **Note:** This step originally replaced `JsonConfigurationSource` instances
> with JSON5 equivalents. That was reverted when we discovered the built-in
> `Microsoft.Extensions.Configuration.Json` provider supports comments and
> trailing commas natively (via `System.Text.Json` with
> `JsonCommentHandling.Skip` and `AllowTrailingCommas = true`).

### 2. Add `marv.json` as a higher-priority source

After the default stack is preserved, add `marv.json` (and any `--config`
override) as a JSON5 source layered on top, so Marv-specific settings take
precedence over `appsettings.json` defaults.

### 3. Use the host builder's environment variable prefix

Remove the manual `builder.Configuration.AddEnvironmentVariables("MARV_")` call.
Instead, configure the host builder's default environment variable provider with
the `MARV_` prefix via `builder.Configuration.AddEnvironmentVariables("MARV_")`
— or, if the default host builder already provides a way to set the prefix,
use that.

> **Note:** The default `Host.CreateApplicationBuilder()` adds an unprefixed
> env var provider. If there is no built-in way to configure its prefix, keep
> the explicit `AddEnvironmentVariables("MARV_")` call but do not clear the
> default provider — let both coexist so that standard .NET env vars (e.g.
> `ASPNETCORE_ENVIRONMENT`) still work.

### 4. Create a `CommandLineConfigurationProvider`

Replace `ConfigurationOptions.GetOverrides()` and the `AddInMemoryCollection`
approach with a custom `IConfigurationProvider` that wraps the
`System.CommandLine.ParseResult`. This provider will:

- Iterate the `Entry` list and extract explicitly-set CLI values.
- Present them as configuration keys, maintaining the same precedence (CLI
  overrides everything).

This simplifies `ConfigurationOptions` to a single responsibility: generating
`Option<T>` definitions from `MarvConfiguration` properties.

### 5. Remove `GetOverrides` and entry `Apply` methods

Once the `CommandLineConfigurationProvider` is in place, remove:

- `ConfigurationOptions.GetOverrides(ParseResult)`
- The `Apply` method on `Entry` and its subclasses (`ScalarEntry`, `BoolEntry`,
  `CollectionEntry`)
- The `AddInMemoryCollection` call in `Program.cs`

`ConfigurationOptions` retains `Build()`, `All`, and the entry records (for
option metadata) but the records no longer need `Apply`.

## Design decisions

- **Preserve `marv.json` as the default config filename** rather than switching
  to `appsettings.json`. The owner confirmed this in issue discussion.
- **Layer order** (lowest to highest priority):
  1. Default .NET stack (`appsettings.json`, `appsettings.{Environment}.json`)
  2. `marv.json` (Marv-specific settings override appsettings defaults)
  3. Environment variables (`MARV_` prefix)
  4. CLI arguments (via `CommandLineConfigurationProvider`)
- **Custom provider over in-memory collection**: the `GetOverrides` +
  `AddInMemoryCollection` approach works but gives `ConfigurationOptions` two
  responsibilities. A dedicated provider is more idiomatic and single-purpose.

## Testing

- Unit test: `CommandLineConfigurationProvider` returns correct keys for various
  `ParseResult` inputs (scalar, bool, collection).
- Unit test: verify config layering precedence — CLI > env vars > marv.json >
  appsettings.json.
- Unit test: verify that `appsettings.json` with JSON5 comments parses correctly
  after the JSON source replacement.
- Manual test: run the bot with a combination of `appsettings.json`,
  `marv.json`, `MARV_` env vars, and CLI args to confirm precedence.

## Impact

- **Plugin API:** No changes.
- **User-facing:** Users gain `appsettings.json` support. Existing `marv.json`
  and `MARV_` env var workflows continue to work.
- **Risk:** Low. The configuration stack order is well-defined and testable.
  Existing users who only use `marv.json` + CLI args see no difference.
