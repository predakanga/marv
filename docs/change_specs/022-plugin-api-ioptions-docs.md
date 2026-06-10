# CS-022: Live Config Reload & IOptions API Documentation — COMPLETED

**Source:** Downstream feature request
**Scope:** Host + Documentation (Program.cs, PLUGIN_API.md)
**Complexity:** Small
**Breaking changes:** None
**Status:** Completed

---

## Problem

Two related gaps prevent plugins from reacting to configuration changes at
runtime:

1. **No live reload:** All file-based configuration providers in `Program.cs`
   are registered with `reloadOnChange: false`. Even though the underlying
   providers (JSON5, YAML, XML) support file-watching, the bot never picks up
   edits to the configuration file without a restart.

2. **Undocumented IOptions variants:** The Plugin API documentation
   (§8 Configuration) only shows `IOptions<T>` in the constructor injection
   example. Plugin authors don't realize they can also inject
   `IOptionsMonitor<T>` or `IOptionsSnapshot<T>` from the standard
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
plugins can react to configuration changes without a restart. But this
requires `reloadOnChange: true` to be set on the file providers — which
it currently is not.

## Changes

### 1. Enable `reloadOnChange` on all file-based config providers

In `src/Marv/Program.cs`, change the `AddConfigFile` helper to pass
`reloadOnChange: true` for all file formats (JSON5, YAML, XML):

```csharp
static void AddConfigFile(IConfigurationBuilder config, string path, bool required)
{
    var extension = Path.GetExtension(path).ToLowerInvariant();

    switch (extension)
    {
        case ".json" or ".json5":
            config.AddJson5File(path, optional: !required, reloadOnChange: true);
            break;
        case ".yaml" or ".yml":
            config.AddYamlFile(path, optional: !required, reloadOnChange: true);
            break;
        case ".xml":
            config.AddXmlFile(path, optional: !required, reloadOnChange: true);
            break;
        default:
            throw new InvalidOperationException(
                $"Unsupported configuration file format: '{extension}'. " +
                "Supported formats: .json, .json5, .yaml, .yml, .xml");
    }
}
```

This is the infrastructure change that makes `IOptionsMonitor<T>.OnChange`
actually fire when a user edits the config file at runtime.

### 2. Update §8 (Configuration) in PLUGIN_API.md

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

### 3. Update §13 (Available Services) table

Add `IOptionsMonitor<T>` and `IOptionsSnapshot<T>` alongside the existing
`IOptions<T>` row:

```markdown
| `IOptions<T>` | Singleton config for `[PluginConfig]` types |
| `IOptionsMonitor<T>` | Config with change notification support |
| `IOptionsSnapshot<T>` | Scoped config (re-reads per scope) |
```

## Design decisions

**Why enable reloadOnChange on all providers?** Without it,
`IOptionsMonitor<T>.OnChange` never fires — the documentation would be
describing a capability that doesn't work in practice. Enabling reload
completes the live-config story end-to-end: edit the file, the bot picks
up the change, plugins react via `OnChange`.

**Why not a separate spec for reloadOnChange?** The code change is a
single-line-per-provider flip from `false` to `true`. It's the
infrastructure that makes the documented `IOptionsMonitor<T>` pattern
actually useful, so bundling them keeps the story coherent.

**Why mention IOptionsSnapshot at all?** For completeness. Plugin authors
coming from ASP.NET Core expect to see all three variants documented.
The note that it's "rarely needed for plugins" sets expectations.

## Testing

- **Review only:** Verify the documentation is accurate by confirming that
  `services.Configure<T>()` is used in plugin config registration (which
  makes all three `IOptions` variants available).
- **Manual verification:** After the code change, confirm that modifying
  the config file at runtime causes `IOptionsMonitor<T>.OnChange` to fire
  (observable via debug logging in a test plugin).
- **Build verification:** `dotnet build` must pass — the change is a
  parameter value flip with no new dependencies.

## Impact

- **Plugin DX:** Authors discover `IOptionsMonitor<T>` without reading
  framework source code, and it actually works out of the box.
- **Runtime behaviour:** Config file edits are now picked up without a
  restart. This is a new capability but non-breaking — plugins using
  `IOptions<T>` are unaffected (it reads once at startup regardless).
- **API surface:** No plugin API changes.
