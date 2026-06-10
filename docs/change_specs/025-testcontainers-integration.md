# CS-025: Replace Custom Docker Logic with Testcontainers

**Source:** Developer feedback
**Scope:** Tests / Build infrastructure
**Complexity:** Small-Medium
**Breaking changes:** None (test infrastructure only)
**Status:** Pending

---

## Problem

Integration tests rely on a manually managed ngircd Docker container
orchestrated through Makefile targets (`make ircd-start` / `make ircd-stop`)
and a hand-rolled TCP probe in `IrcServerFixture`. This has several drawbacks:

1. **Manual lifecycle management.** Developers must remember to start the
   container before running integration tests and stop it afterwards. The
   `make test-integration` target automates this, but running tests from an
   IDE or directly via `dotnet test` does not.

2. **Fragile port assumptions.** The fixture hardcodes `localhost:6667`. If
   another process occupies that port, or if tests run in parallel CI jobs,
   they collide silently.

3. **No container cleanup on failure.** If a test run crashes or is
   interrupted, the container is left running until someone manually runs
   `make ircd-stop` or `docker rm -f marv-ircd`.

4. **Skip-on-unavailable heuristic.** `IrcServerFixture` probes the port and
   sets `IsAvailable`; every test must call `SkipIfUnavailable()`. This means
   integration tests silently pass in CI if the container failed to start.

## Design

Replace the custom Docker orchestration with
[Testcontainers for .NET](https://dotnet.testcontainers.org/), a mature
library that manages container lifecycles programmatically within the test
process.

### Revised `IrcServerFixture`

```csharp
public class IrcServerFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("linuxserver/ngircd")
        .WithPortBinding(6667, true)  // random host port
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilPortIsAvailable(6667))
        .Build();

    public string Host => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(6667);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    // CreateConnectionAsync, CreateBot, CreateConfig remain the same
    // but use Host/Port properties instead of constants.
}
```

### Key changes

| Area | Before | After |
|---|---|---|
| Container start | `make ircd-start` / manual `docker run` | Automatic in `InitializeAsync` |
| Container stop | `make ircd-stop` / manual | Automatic in `DisposeAsync` (also on crash) |
| Port allocation | Hardcoded `localhost:6667` | Random mapped port, no collisions |
| Server readiness | TCP probe + `IsAvailable` flag | `WaitStrategy` blocks until port is listening |
| Test skip logic | `SkipIfUnavailable()` in every test | Not needed — fixture guarantees the server is up |
| IDE test runs | Require prior `make ircd-start` | Just run tests; container starts automatically |

### Test changes

- Remove `SkipIfUnavailable()` calls from all integration tests — the
  container is guaranteed to be running when tests execute.
- Remove `IrcServerCollection` if the fixture is used as a class fixture
  or retain it as a collection fixture — either way, the skip logic goes away.
- Update `CreateConnectionAsync`, `CreateBot`, and `CreateConfig` to use
  the dynamic `Host` and `Port` from the container instead of constants.

### Makefile changes

- `ircd-start` and `ircd-stop` targets can be removed or kept as convenience
  targets for manual debugging. Either way, `test-integration` no longer
  needs to wrap `ircd-start` / `ircd-stop` around the test run.
- `test-integration` simplifies to just `dotnet test` with the integration
  filter, since Testcontainers handles the container lifecycle.

### Package addition

Add the `Testcontainers` NuGet package to `Marv.Core.Tests.csproj`:

```xml
<PackageReference Include="Testcontainers" Version="4.*" />
```

No changes to production assemblies.

## Dependencies

- None on other change specs.
- Requires Docker to be available on the test host (same as today).

## Impact

- **Developer experience:** Integration tests become self-contained — run
  them from any IDE or CLI without manual container management.
- **CI reliability:** Random port mapping eliminates port conflicts in
  parallel jobs. Container cleanup is deterministic.
- **Test correctness:** No more silent skips when the server is unavailable;
  a missing Docker daemon produces a clear error instead.
- **Production code:** Zero changes — this is entirely test infrastructure.

## Migration steps

1. Add `Testcontainers` package reference to test project.
2. Rewrite `IrcServerFixture` to use `ContainerBuilder`.
3. Remove `SkipIfUnavailable()` from all integration tests.
4. Update hardcoded `Host`/`Port` references to use fixture properties.
5. Simplify `Makefile` targets.
6. Verify all integration tests pass with `dotnet test --filter "Category=Integration"`.

## Open questions

1. **Should we use a custom ngircd configuration?** Currently the container
   runs with the default ngircd config from the `linuxserver/ngircd` image.
   If custom config is needed (e.g. for auth testing), Testcontainers supports
   bind mounts and file copies into the container via `WithResourceMapping`.
2. **Shared container across test classes?** The current `IrcServerCollection`
   shares one fixture across all integration tests. Testcontainers supports
   both per-class and shared containers. Recommendation: keep a single shared
   container via collection fixture to avoid starting multiple ngircd
   instances.
