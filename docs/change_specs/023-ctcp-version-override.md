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

**Recommendation: Configuration property (A) only.**

A simple `CtcpVersionResponse` config property covers the common case
(operators who want a static override or want to suppress the response).
For plugins that need fully dynamic VERSION responses, the existing
`[OnEvent]` handler with `CtcpEvent` already provides the mechanism —
the plugin sets an empty config value to suppress the default and handles
the CTCP VERSION event itself.

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

### 2. Update `HandleCtcp` in `IrcBot`

```csharp
case "VERSION":
    var versionResponse = _config.CtcpVersionResponse
        ?? $"Marv IRC Bot {MarvVersion.Current}";

    if (!string.IsNullOrEmpty(versionResponse))
    {
        await SendRawAsync(new IrcMessage("NOTICE", [sender.Nick,
            $"\x01VERSION {versionResponse}\x01"]), ct);
    }
    break;
```

### 3. Update PLUGIN_API.md

Document the `CtcpVersionResponse` configuration property. Add a note
explaining that plugins needing dynamic VERSION responses can set
`CtcpVersionResponse` to an empty string to suppress the built-in
response, then handle `CtcpEvent` via `[OnEvent]` to send their own.

## Design decisions

**Why config-only, no delegate?** A delegate property on `IBot` would be
the only such pattern in the entire API — everything else uses either
configuration or handler attributes. The existing `[OnEvent]` +
`CtcpEvent` handler already provides the dynamic escape hatch without
introducing a new API pattern.

**Why allow suppressing the response?** Some operators don't want to
reveal bot software information. Setting an empty string in config
suppresses the NOTICE entirely. This also enables the plugin takeover
pattern: suppress the default, handle the event yourself.

## Testing

- **Unit test:** Default behavior — verify VERSION response contains
  `MarvVersion.Current`.
- **Unit test:** Set config value — verify it's used instead of the default.
- **Unit test:** Config set to empty string — verify no NOTICE is sent.

## Impact

- **Configuration:** One new optional property in `MarvConfiguration`.
- **API surface:** No changes to `IBot`.
- **Plugin DX:** Simple config override for static strings; documented
  escape hatch via `[OnEvent]` + `CtcpEvent` for dynamic responses.
