# CS-036: Bot Identity

**Source:** GitHub issue #12
**Scope:** Core / Host
**Complexity:** Medium
**Breaking changes:** None
**Status:** Pending

---

## Problem

When Marv is used as the foundation for a downstream project (e.g. an
"IdleRPG" bot), the bot identifies itself as "Marv" everywhere:

- Sentry error reports use Marv's version number, not the downstream
  project's.
- The CTCP VERSION response defaults to "Marv IRC Bot {version}".
- The `!version` command in CannedResponses is hardcoded to
  "Marv IRC Bot v{version}".
- The IRC GECOS (real name) defaults to "Marv IRC Bot".
- `MarvVersion.Current` always reads from the `Marv.Core` assembly,
  so downstream projects that reference Marv.Core as a NuGet package
  get Marv's version, not their own.

CS-023 partially addressed this by adding a `CtcpVersionResponse`
configuration override, but that's a point fix for one touchpoint.
There's no unified identity concept that downstream distributions can
use to take ownership of the bot's branding.

## Changes

### 1. Add `BotIdentity` record to `Marv.Core`

A simple, immutable identity model:

```csharp
/// <summary>
/// Identifies the bot product for display, telemetry, and protocol responses.
/// </summary>
public record BotIdentity(string Name, string Version, string? SourceUrl = null)
{
    /// <summary>
    /// Combined name and version string (e.g. "Marv IRC Bot 0.8.0").
    /// </summary>
    public string FullIdentity => $"{Name} {Version}";
}
```

### 2. Add identity configuration properties to `MarvConfiguration`

```csharp
/// <summary>
/// The bot's product name, used in CTCP VERSION, the !version command,
/// Sentry reports, and anywhere else the bot identifies itself.
/// Defaults to "Marv IRC Bot".
/// </summary>
[Description("Bot product name for identification.")]
public string BotName { get; init; } = "Marv IRC Bot";

/// <summary>
/// The bot's product version. When null, auto-detected from the entry
/// assembly's informational version. Set this when running a downstream
/// distribution that packages Marv as a dependency.
/// </summary>
[Description("Bot product version (null = auto-detect from entry assembly).")]
public string? BotVersion { get; init; }
```

### 3. Register `BotIdentity` in DI

In `MarvServiceExtensions.AddMarv()`, construct and register a
singleton `BotIdentity` from configuration:

```csharp
var identity = new BotIdentity(
    config.BotName,
    config.BotVersion ?? EntryAssemblyVersion() ?? MarvVersion.Current,
    config.SourceUrl);
services.AddSingleton(identity);
```

The version resolution order is:
1. Explicit `BotVersion` config value (highest priority).
2. Entry assembly's informational version (catches downstream hosts
   that set their own version in their `.csproj`).
3. `MarvVersion.Current` (fallback — the Marv.Core assembly version).

Add a private helper to read the entry assembly version:

```csharp
private static string? EntryAssemblyVersion()
{
    var asm = Assembly.GetEntryAssembly();
    if (asm is null) return null;
    var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion;
    if (info is null) return null;
    var plus = info.IndexOf('+');
    return plus >= 0 ? info[..plus] : info;
}
```

### 4. Update CTCP VERSION handling in `IrcBot`

Replace the hardcoded fallback with `BotIdentity`:

```csharp
case "VERSION":
    var versionResponse = _config.CtcpVersionResponse
        ?? _identity.FullIdentity;
    // ...
```

`CtcpVersionResponse` remains as a point override for operators who
want a completely custom string or want to suppress the response.
Its existing behaviour is unchanged.

### 5. Update `InfoHandlers` in CannedResponses

Inject `BotIdentity` and use it instead of the hardcoded string:

```csharp
public InfoHandlers(IBot bot, BotIdentity identity)
{
    _bot = bot;
    _identity = identity;
}

[OnCommand("version")]
public async Task HandleVersion(CommandContext ctx, CancellationToken ct)
{
    await ctx.ReplyAsync($"{_identity.Name} v{_identity.Version}", ct);
}
```

### 6. Wire `BotIdentity` into Sentry in `Program.cs`

After building the host, resolve `BotIdentity` and set the Sentry
release. Since Sentry is configured during host building (before
services are available), use the same config-reading approach:

```csharp
builder.Logging.AddSentry(o =>
{
    o.Dsn = sentryDsn;
    o.Release = $"{config.BotName}@{config.BotVersion ?? MarvVersion.Current}";
    o.MinimumEventLevel = LogLevel.Error;
    o.MinimumBreadcrumbLevel = LogLevel.Warning;
    o.TracesSampleRate = 0;
});
```

This requires reading `MarvConfiguration` earlier in `Program.cs`,
which is straightforward since the configuration sources are already
registered at that point.

### 7. Update `MarvVersion` documentation

Add an XML doc note to `MarvVersion.Current` clarifying that it
returns the `Marv.Core` assembly version specifically, and that
`BotIdentity` should be preferred for display purposes.

### 8. Update `docs/PLUGIN_API.md`

Document:
- The `BotIdentity` record and how to inject it.
- The `BotName` and `BotVersion` configuration properties.
- That `CtcpVersionResponse` still works as a CTCP-specific override.
- How downstream distributions should set their identity (config or
  entry assembly version).

## Design decisions

**Why a dedicated `BotIdentity` type instead of just config properties?**
A typed record in DI gives plugins a clean injection point without
coupling to `MarvConfiguration`. It also provides `FullIdentity` as a
canonical formatted string, preventing each consumer from assembling
name + version differently.

**Why keep `CtcpVersionResponse`?** It serves a different purpose —
it's a protocol-level override that can suppress the response entirely
(empty string) or provide a string unrelated to the bot's actual
identity. `BotIdentity` is the product identity; `CtcpVersionResponse`
is what you want the IRC network to see, which may differ.

**Why auto-detect from the entry assembly?** Downstream projects that
create their own host application (referencing Marv.Core as a NuGet
package) naturally have their own assembly version in their `.csproj`.
Auto-detecting from the entry assembly means they get correct identity
without any configuration, while still allowing explicit override.

**Why not a subclassable host app?** The issue mentions this as a
possibility, but it would be a much larger change with significant API
surface implications. Configuration-based identity covers the concrete
use cases (Sentry version, CTCP, display strings) without requiring
downstream projects to subclass anything.

## Testing

- **Unit test:** Default identity — `BotIdentity` uses "Marv IRC Bot"
  and `MarvVersion.Current` when no config overrides are set.
- **Unit test:** Config override — setting `BotName` and `BotVersion`
  in config produces a `BotIdentity` with those values.
- **Unit test:** CTCP VERSION uses `BotIdentity.FullIdentity` as
  default, but `CtcpVersionResponse` still takes precedence when set.
- **Unit test:** `InfoHandlers.HandleVersion` uses injected
  `BotIdentity` values.
- **Unit test:** Entry assembly version detection fallback.

## Impact

- **Configuration:** Two new optional properties (`BotName`,
  `BotVersion`) in `MarvConfiguration`. No breaking changes — defaults
  match current behaviour.
- **DI:** One new singleton registration (`BotIdentity`).
- **Plugin API:** `BotIdentity` is a new injectable type. Existing
  plugins are unaffected. `CtcpVersionResponse` continues to work.
- **Risk:** Low. All changes are additive. Existing behaviour is
  preserved when no new config properties are set.
