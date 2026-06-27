# CS-036: Bot Identity

**Source:** GitHub issue #12
**Scope:** Core / Host
**Complexity:** Medium
**Breaking changes:** Yes — removes `CtcpVersionResponse` config property and `MarvVersion` class
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
/// The bot's public identity — name, version, and optional source URL.
/// Used in CTCP VERSION responses, the !version command, Sentry reports,
/// and anywhere else the bot identifies itself.
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
/// The bot's public name, used in CTCP VERSION, the !version command,
/// Sentry reports, and anywhere else the bot identifies itself.
/// Defaults to "Marv IRC Bot".
/// </summary>
[Description("Bot public name for identification.")]
public string BotName { get; init; } = "Marv IRC Bot";

/// <summary>
/// The bot's public version. When null, auto-detected from the entry
/// assembly's informational version. Set this when running a downstream
/// distribution that packages Marv as a dependency.
/// </summary>
[Description("Bot public version (null = auto-detect from entry assembly).")]
public string? BotVersion { get; init; }
```

### 3. Remove `CtcpVersionResponse` from `MarvConfiguration`

Delete the `CtcpVersionResponse` property. With `BotIdentity` providing
a unified name + version, a separate CTCP-specific override is
redundant. Operators who need to completely suppress or customise the
CTCP VERSION response beyond what `BotName`/`BotVersion` provide can
use the existing `[OnEvent]` + `CtcpEvent` handler pattern — set the
handler to intercept the VERSION event and send (or not send) whatever
response they want.

This is a breaking change for anyone using `CtcpVersionResponse` in
their config file (the property will be silently ignored) or
referencing it in code (compile error). Acceptable pre-1.0.

### 4. Remove `MarvVersion` class

Delete `src/Marv.Core/MarvVersion.cs`. Its version-detection logic is
inlined into the `BotIdentity` construction (see below). With
`BotIdentity` as the canonical source of version information, a
separate `MarvVersion` class is unnecessary.

This is a breaking change for any code referencing
`MarvVersion.Current`. Acceptable pre-1.0.

### 5. Register `BotIdentity` in DI

In `MarvServiceExtensions.AddMarv()`, construct and register a
singleton `BotIdentity` from configuration:

```csharp
var identity = new BotIdentity(
    config.BotName,
    config.BotVersion ?? ResolveVersion());
services.AddSingleton(identity);
```

The version resolution order is:
1. Explicit `BotVersion` config value (highest priority).
2. Entry assembly's informational version (catches downstream hosts
   that set their own version in their `.csproj`).
3. `Marv.Core` assembly's informational version (fallback for test
   hosts or environments with no entry assembly).

```csharp
private static string ResolveVersion()
{
    // Try the entry assembly first — downstream hosts set their
    // own version in their .csproj
    var entry = Assembly.GetEntryAssembly();
    var info = (entry ?? typeof(MarvServiceExtensions).Assembly)
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion;

    if (info is not null)
    {
        // Strip build metadata suffix (e.g. "+abc123def")
        var plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }

    return entry?.GetName().Version?.ToString(3)
        ?? typeof(MarvServiceExtensions).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}
```

### 6. Update CTCP VERSION handling in `IrcBot`

Replace the current logic (which references `CtcpVersionResponse` and
`MarvVersion`) with `BotIdentity`:

```csharp
case "VERSION":
    await SendRawAsync(new IrcMessage("NOTICE", [sender.Nick,
        $"\x01VERSION {_identity.FullIdentity}\x01"]), ct);
    break;
```

Inject `BotIdentity` into `IrcBot`'s constructor.

### 7. Update `InfoHandlers` in CannedResponses

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

### 8. Wire `BotIdentity` into Sentry in `Program.cs`

Read `MarvConfiguration` from the config sources (already registered
at that point) and use the identity fields for the Sentry release:

```csharp
builder.Logging.AddSentry(o =>
{
    o.Dsn = sentryDsn;
    o.Release = $"{config.BotName}@{config.BotVersion ?? ResolveVersion()}";
    o.MinimumEventLevel = LogLevel.Error;
    o.MinimumBreadcrumbLevel = LogLevel.Warning;
    o.TracesSampleRate = 0;
});
```

The `ResolveVersion()` helper can be extracted to a shared location or
duplicated in `Program.cs` since it's a small static method. The
implementation can decide the cleanest approach.

### 9. Update `docs/PLUGIN_API.md`

Document:
- The `BotIdentity` record and how to inject it.
- The `BotName` and `BotVersion` configuration properties.
- Removal of `CtcpVersionResponse` and the `[OnEvent]` + `CtcpEvent`
  alternative for advanced CTCP VERSION customisation.
- How downstream distributions should set their identity (config or
  entry assembly version).

## Design decisions

**Why a dedicated `BotIdentity` type instead of just config properties?**
A typed record in DI gives plugins a clean injection point without
coupling to `MarvConfiguration`. It also provides `FullIdentity` as a
canonical formatted string, preventing each consumer from assembling
name + version differently.

**Why remove `CtcpVersionResponse`?** It was a stopgap from CS-023 that
addressed one touchpoint. With `BotIdentity` providing the unified
name + version, the config override is redundant — changing `BotName`
and `BotVersion` covers the common case. The suppress/fully-custom case
is already handled by the `[OnEvent]` + `CtcpEvent` handler pattern,
which is more powerful and doesn't require a special config property.

**Why remove `MarvVersion`?** Its sole purpose was reading the version
from the `Marv.Core` assembly. That logic is now inlined into the
`BotIdentity` construction, and `BotIdentity` is the canonical source
for all version display. Keeping `MarvVersion` around would be a
confusing alternative path to the same information.

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
  and the resolved assembly version when no config overrides are set.
- **Unit test:** Config override — setting `BotName` and `BotVersion`
  in config produces a `BotIdentity` with those values.
- **Unit test:** CTCP VERSION uses `BotIdentity.FullIdentity`.
- **Unit test:** `InfoHandlers.HandleVersion` uses injected
  `BotIdentity` values.
- **Unit test:** Version resolution falls back from entry assembly to
  `Marv.Core` assembly.

## Impact

- **Configuration:** Two new optional properties (`BotName`,
  `BotVersion`). One removed property (`CtcpVersionResponse`).
  Defaults match current behaviour for users who haven't set
  `CtcpVersionResponse`.
- **DI:** One new singleton registration (`BotIdentity`).
- **Plugin API:** `BotIdentity` is a new injectable type. `MarvVersion`
  is removed. `CtcpVersionResponse` config property is removed.
- **Breaking changes:** `MarvVersion.Current` and
  `CtcpVersionResponse` are removed. Acceptable pre-1.0.
- **Risk:** Low. The breaking changes affect a narrow surface area
  (one class, one config property) and the replacements are
  straightforward.
