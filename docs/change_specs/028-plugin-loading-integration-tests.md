# CS-028: Plugin Loading Integration Tests

**Source:** GitHub issue #3
**Scope:** Tests
**Complexity:** Medium
**Breaking changes:** None
**Status:** Pending

---

## Problem

Plugin loading has proven fragile, particularly around interactions with
`PublishSingleFile`, `SelfContained`, and the `MetadataLoadContext`-based
inspection code. Existing unit tests cover individual components
(`PluginDiscovery`, `PluginDependencySorter`, `PluginManager` instantiation)
but no tests exercise the full pipeline against published artifacts. Regressions
in the interaction between phases go undetected until a release is built.

## Changes

### 1. Add a published-output test fixture

Create a test fixture class (e.g. `PublishedOutputFixture`) that:

- Runs `dotnet publish` on the Marv host project and plugin projects to a
  temporary directory, producing the same artifact layout as `make publish`.
- Exposes the output directory paths (host output dir, plugin dir) to tests.
- Caches the build output for the test run (build once, run many tests).

The fixture should use an idiomatic xUnit approach (`IAsyncLifetime` or
`ICollectionFixture<T>`) rather than shelling out to `make`, so it works
correctly in CI without requiring Make.

### 2. Metadata scanning tests

Tests that exercise `PluginMetadataScanner.ScanDirectories()` against the
published plugin DLLs:

- Verify that all expected plugins are discovered (Greet, CannedResponses,
  Auth, AuthConsumer, Moderation).
- Verify plugin names are correctly extracted (from `[PluginName]` attributes
  and class name conventions).
- Verify that non-plugin DLLs in the plugin directory are ignored.

### 3. Full pipeline tests

Tests that exercise the complete loading pipeline:

- `PluginMetadataScanner.ScanDirectories()` → `ResolveRequestedPlugins()` →
  `PluginManager.DiscoverAndRegister()` → `InstantiatePlugins()`.
- Verify assembly resolution works correctly in a published context (where
  assembly probing differs from development builds).
- Verify dependency sorting: Auth must load before AuthConsumer.
- Verify service registration: plugins' `ConfigureServices` methods are called.
- Verify `[PluginConfig]` configuration binding works end-to-end with an
  in-memory `IConfiguration`.

### 4. Error case tests

- Missing plugin assembly: verify clear error message.
- Plugin with unsatisfied dependency: verify error message names the missing
  dependency.
- Assembly that is not a valid plugin: verify it is skipped gracefully.

### 5. Test organisation

- Tests live in `tests/Marv.Core.Tests/Integration/PluginLoading/`.
- Tagged with `[Trait("Category", "Integration")]` so they are included in
  `make test-integration` and excluded from `make test`.
- Shared fixture via `[CollectionDefinition]` / `[Collection]` to avoid
  rebuilding published output per test class.

## Design decisions

- **Published artifacts, not development builds**: The owner confirmed that
  several past failures only manifested in published output due to
  `PublishSingleFile` / `SelfContained` interactions with `MetadataLoadContext`.
  Testing against development build output would miss these regressions.
- **xUnit fixture over Makefile**: Using `dotnet publish` directly from the
  fixture is more portable and CI-friendly than depending on Make.
- **No IRC server needed**: These tests only exercise the plugin loading
  pipeline, not IRC connectivity. They need built assemblies and a
  `ServiceCollection`, not a running IRC server.

## Testing

- All new tests are themselves the deliverable.
- Verify tests pass with `make test-integration` (or `dotnet test --filter
  "Category=Integration"`).
- Verify tests fail correctly when a plugin DLL is removed from the published
  output.

## Impact

- **Plugin API:** No changes.
- **DX:** Developers gain confidence that plugin loading works end-to-end in
  published artifacts. CI catches regressions that unit tests miss.
- **Risk:** Very low. Test-only change; no production code modified.
