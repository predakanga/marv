# CS-022: Document Full IOptions API Availability

**Source:** Downstream feature request
**Scope:** Documentation (PLUGIN_API.md)
**Complexity:** Trivial
**Breaking changes:** None
**Status:** Pending

---

## Problem

The Plugin API documentation (§8 Configuration) only shows `IOptions<T>` in
the constructor injection example. Plugin authors don't realize they can also
inject `IOptionsMonitor<T>` or `IOptionsSnapshot<T>` from the standard
`Microsoft.Extensions.Options` package.

Since Marv registers plugin configuration using the standard
`services.Configure<T>(section)` pattern, the full `IOptions` API is
available automatically through the DI container:

- `IOptions<T>` — singleton, read once at startup
- `IOptionsSnapshot<T>` — scoped, re-reads per scope (less useful for
  long-lived plugins, but available)
- `IOptionsMonitor<T>` — singleton, fires `OnChange` callbacks when the
  underlying configuration source changes

`IOptionsMonitor<T>` is particularly valuable: if the bot's configuration
file is edited at runtime and the configuration provider supports reload
(which the JSON5/YAML providers do when `reloadOnChange: true` is set),
plugins can react to configuration changes without a restart.

## Changes

### 1. Update §8 (Configuration) in PLUGIN_API.md

After the existing `IOptions<T>` example, add a note and example:

```markdown
### Options API variants

The full `Microsoft.Extensions.Options` API is available for plugin
configuration. In addition to `IOptions<T>`, you can inject:

| Type | Lifetime | Use case |
|---|---|---|
| `IOptions<T>` | Singleton | Read config once at construction |
| `IOptionsMonitor<T>` | Singleton | React to config changes at runtime via `OnChange` |
| `IOptionsSnapshot<T>` | Scoped | Re-read config per scope (rarely needed for plugins) |

Example using `IOptionsMonitor<T>` to react to config file edits:

```csharp
public class MyPlugin(IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory,
    IOptionsMonitor<MyConfig> configMonitor)
    : MarvPlugin(bot, activator, loggerFactory)
{
    private MyConfig _config = configMonitor.CurrentValue;

    public override Task OnConnectedAsync(CancellationToken ct)
    {
        configMonitor.OnChange(updated => _config = updated);
        return Task.CompletedTask;
    }
}
```
```

### 2. Update §13 (Available Services) table

Add `IOptionsMonitor<T>` and `IOptionsSnapshot<T>` alongside the existing
`IOptions<T>` row:

```markdown
| `IOptions<T>` | Singleton config for `[PluginConfig]` types |
| `IOptionsMonitor<T>` | Config with change notification support |
| `IOptionsSnapshot<T>` | Scoped config (re-reads per scope) |
```

## Design decisions

**Why not also update reloadOnChange in the host?** The current
`AddJson5File` call uses `reloadOnChange: false`. Enabling it is a
separate concern — this spec documents what's already possible through
the DI container. A follow-up spec could enable `reloadOnChange` and
document the full live-reload story.

**Why mention IOptionsSnapshot at all?** For completeness. Plugin authors
coming from ASP.NET Core expect to see all three variants documented.
The note that it's "rarely needed for plugins" sets expectations.

## Testing

- **Review only:** Verify the documentation is accurate by confirming that
  `services.Configure<T>()` is used in plugin config registration (which
  makes all three `IOptions` variants available).

## Impact

- **Plugin DX:** Authors discover `IOptionsMonitor<T>` without reading
  framework source code.
- **API surface:** No code changes.
