# CS-023: CTCP VERSION Response Override

**Source:** Downstream feature request
**Scope:** Core
**Complexity:** Small
**Breaking changes:** None
**Status:** Pending

---

## Problem

The bot responds to CTCP VERSION queries with a hardcoded string:

```csharp
$"\x01VERSION Marv IRC Bot {MarvVersion.Current}\x01"
```

(`IrcBot.cs`, line ~904)

Some operators want to customize this — to include plugin names, the host
OS, or to obscure the bot's identity for security. There is currently no
way to change this without modifying the bot's source code.

## Approach options considered

| Approach | Pros | Cons |
|---|---|---|
| **A. Configuration property** | Simple, discoverable, no code changes at runtime | Static — can't include dynamic info (plugin list, uptime) |
| **B. Writable property on IBot** | Plugins can set it dynamically | Unclear ownership if multiple plugins set it; still just a string |
| **C. Subclass IrcBot** | Maximum flexibility | Defeats the plugin model; IrcBot is internal |
| **D. Configuration + format tokens** | Config-driven but with dynamic placeholders | Custom token system adds complexity |
| **E. Event/delegate on IBot** | Plugin provides a callback; full dynamic control | Clean ownership, composable, maximum flexibility |

**Recommendation: Configuration property (A) with a delegate fallback (E).**

A simple `CtcpVersionResponse` config property covers the 90% case (operators
who want a static string). For plugins that need dynamic responses, a
delegate property on `IBot` allows full control.

## Changes

### 1. Add `CtcpVersionResponse` to `MarvConfiguration`

```csharp
/// <summary>
/// Custom response to CTCP VERSION queries. If null, uses the default
/// "Marv IRC Bot {version}" string. Set to empty string to suppress
/// VERSION responses entirely.
/// </summary>
public string? CtcpVersionResponse { get; set; }
```

### 2. Add a delegate property to `IBot`

```csharp
/// <summary>
/// Optional callback that generates the CTCP VERSION response string.
/// When set, takes precedence over the <c>CtcpVersionResponse</c>
/// configuration value. Return null to suppress the response.
/// </summary>
Func<string?>? CtcpVersionProvider { get; set; }
```

### 3. Update `HandleCtcp` in `IrcBot`

```csharp
case "VERSION":
    var versionResponse = CtcpVersionProvider?.Invoke()
        ?? _config.CtcpVersionResponse
        ?? $"Marv IRC Bot {MarvVersion.Current}";

    if (!string.IsNullOrEmpty(versionResponse))
    {
        await SendRawAsync(new IrcMessage("NOTICE", [sender.Nick,
            $"\x01VERSION {versionResponse}\x01"]), ct);
    }
    break;
```

### 4. Update PLUGIN_API.md

Add `CtcpVersionProvider` to the IBot table in §5. Document the precedence
order: delegate > config > default.

## Design decisions

**Why not just config?** A static string can't include runtime information
like loaded plugins, uptime, or platform details. The delegate lets
plugins like a system-info plugin provide rich version strings.

**Why not just a delegate?** Operators who just want to set
`"CtcpVersionResponse": "My Custom Bot"` in their config file shouldn't
need to write a plugin for it.

**Why `Func<string?>` instead of an event?** Only one VERSION response
can be sent. An event implies multiple subscribers, which doesn't make
sense here. A single delegate with last-writer-wins semantics is clear.

**Why allow suppressing the response?** Some operators don't want to
reveal bot software information. Returning null or setting an empty
string in config suppresses the NOTICE entirely.

## Testing

- **Unit test:** Default behavior — verify VERSION response contains
  `MarvVersion.Current`.
- **Unit test:** Set config value — verify it's used instead of the default.
- **Unit test:** Set delegate — verify it takes precedence over config.
- **Unit test:** Delegate returns null — verify no NOTICE is sent.
- **Unit test:** Config set to empty string — verify no NOTICE is sent.

## Impact

- **Configuration:** One new optional property in `MarvConfiguration`.
- **API surface:** One new property on `IBot`.
- **Plugin DX:** Plugins can dynamically control VERSION responses.
