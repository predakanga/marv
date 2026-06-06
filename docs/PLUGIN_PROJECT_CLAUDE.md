# CLAUDE.md (template for Marv plugin projects)

Copy this file into your plugin project's root as `CLAUDE.md` and adjust
the path to the Marv source tree.

---

## Project context

This project builds plugins for the Marv IRC bot. The Marv source tree
is read-only — do not modify it.

## Key reference

- Plugin API: `<marv-root>/docs/PLUGIN_API.md` — the single authoritative
  reference for all plugin types, attributes, contexts, events, and patterns.

## Plugin constructor pattern

```csharp
using Marv.Core.Platform;
using Marv.Core.Plugin;
using Microsoft.Extensions.Logging;

public class MyPlugin : MarvPlugin
{
    public MyPlugin(IBot bot, IPluginActivator activator, ILoggerFactory loggerFactory)
        : base(bot, activator, loggerFactory) { }
}
```

Add `IOptions<TConfig>` or service interfaces after the three required
parameters as needed.

## Essential using statements

```csharp
using Marv.Core.Events;       // Event types
using Marv.Core.Formatting;   // IrcColor, IrcFormat
using Marv.Core.Platform;     // IBot, IUser, IChannel
using Marv.Core.Plugin;       // MarvPlugin, attributes, contexts
using Marv.Core.Protocol;     // IrcMessage
```

## Creating a new plugin assembly

1. Create a class library targeting `net10.0`
2. Add a project reference to `Marv.Core`
3. Create a class extending `MarvPlugin`
4. Build the DLL into a directory listed in the bot's `PluginDirectories` config
5. Add the plugin name to the bot's `Plugins` config list
