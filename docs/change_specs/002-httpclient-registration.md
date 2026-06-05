# CS-002: IHttpClientFactory Registration — COMPLETED

**Source:** `downstream_suggestions/improvements.md` §3
**Scope:** Marv.Core (via AddMarv service registration)
**Complexity:** Trivial
**Breaking changes:** None
**Status:** Completed

---

## Problem

Plugins that make HTTP requests need `IHttpClientFactory`. Currently each
plugin project must add `Microsoft.Extensions.Http` as a NuGet dependency
and register the factory in `ConfigureServices`. The downstream project had
four plugins doing this independently.

HTTP access is a common enough need that the host should provide it by
default.

## Changes

### 1. Add `services.AddHttpClient()` in the host's service registration

In the Marv host application (`Program.cs` or wherever the
`IServiceCollection` is configured), add:

```csharp
services.AddHttpClient();
```

This registers `IHttpClientFactory` and `HttpClient` in the DI container.

### 2. Add `Microsoft.Extensions.Http` dependency to Marv (host)

The host application already depends on `Microsoft.Extensions.DependencyInjection`
and `Microsoft.Extensions.Hosting`. Adding `Microsoft.Extensions.Http` is a
lightweight addition (~50KB, no transitive dependencies beyond what's already
present).

## Impact

- **Plugin authors:** Can inject `IHttpClientFactory` directly without adding
  the NuGet package or calling `AddHttpClient()` themselves.
- **Existing plugins:** No change needed. If a plugin already calls
  `services.AddHttpClient()` in its `ConfigureServices`, the second call is
  a no-op (idempotent registration).
- **Host binary size:** Negligible increase (~50KB).

## Non-changes

- This does **not** register any named or typed HTTP clients. Plugins that
  need custom `HttpClient` configuration (base address, default headers,
  retry policies) still configure them via `services.AddHttpClient<T>()` or
  `services.AddHttpClient("name")` in their own `ConfigureServices`.
