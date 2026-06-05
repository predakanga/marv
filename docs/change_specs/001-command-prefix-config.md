# CS-001: Command Prefix Configuration — COMPLETED

**Source:** `downstream_suggestions/improvements.md` §4
**Scope:** Marv.Core
**Complexity:** Small
**Breaking changes:** None
**Status:** Completed

---

## Problem

`MarvConfiguration.CommandPrefix` already exists (defaults to `"!"`) but is
unused. Command parsing in `MarvPlugin.DispatchCommandHandlers` (line 167) is
hardcoded to `'!'` with a TODO comment. Downstream projects that want a
different prefix (e.g. `.`) must use `[OnRegex]` workarounds.

## Decisions

- Expose `CommandPrefix` as a property on `IBot`.
- Support multi-character prefixes (e.g. `"!!"`, `"marv:"`).
- Prefix matching is case-sensitive (ordinal comparison).
- `[OnCommand]` gains a `Prefix` property to override the bot-wide default
  on a per-handler basis.

## Changes

### 1. Add `CommandPrefix` to `IBot`

```csharp
/// <summary>The configured command prefix (e.g. "!").</summary>
string CommandPrefix { get; }
```

The `MarvBot` implementation returns the value from
`MarvConfiguration.CommandPrefix`.

### 2. Add `Prefix` property to `OnCommandAttribute`

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class OnCommandAttribute(string command) : Attribute
{
    /// <summary>The command name to match (without the prefix).</summary>
    public string Command { get; } = command;

    /// <summary>
    /// Overrides the bot-wide command prefix for this handler.
    /// When null, the bot's configured <see cref="IBot.CommandPrefix"/> is used.
    /// </summary>
    public string? Prefix { get; init; }
}
```

Usage:

```csharp
// Uses the bot-wide prefix (default "!")
[OnCommand("ban")]
public async Task HandleBan(CommandContext ctx, CancellationToken ct) { ... }

// Uses "." regardless of bot-wide prefix
[OnCommand("invite", Prefix = ".")]
public async Task HandleInvite(CommandContext ctx, CancellationToken ct) { ... }
```

### 3. Store resolved prefix in `CommandRegistration`

Copy the effective prefix into the registration record at discovery time.
The per-handler `Prefix` property takes precedence; fall back to
`Bot.CommandPrefix` if null.

Since handler discovery happens in the `MarvPlugin` constructor (before
connection), the bot-wide prefix is available from config at construction
time.

```csharp
private sealed record CommandRegistration(
    object Target, MethodInfo Method, string Command, string Prefix);
```

During discovery:

```csharp
foreach (var cmdAttr in method.GetCustomAttributes<OnCommandAttribute>())
{
    _commandHandlers.Add(new CommandRegistration(
        target,
        method,
        cmdAttr.Command.ToLowerInvariant(),
        cmdAttr.Prefix ?? Bot.CommandPrefix));
}
```

### 4. Update `DispatchCommandHandlers`

Replace the current hardcoded prefix check with per-handler prefix matching.
Since different handlers may have different prefixes, the prefix check moves
inside the handler loop:

```csharp
private async Task DispatchCommandHandlers(MessageEvent msgEvt, CancellationToken ct)
{
    if (_commandHandlers.Count == 0)
        return;

    var text = IrcFormat.Strip(msgEvt.Text);

    foreach (var handler in _commandHandlers)
    {
        var prefix = handler.Prefix;

        if (text.Length < prefix.Length + 1
            || !text.StartsWith(prefix, StringComparison.Ordinal))
            continue;

        var afterPrefix = text.AsSpan(prefix.Length);
        var spaceIndex = afterPrefix.IndexOf(' ');
        var command = spaceIndex < 0
            ? afterPrefix.ToString().ToLowerInvariant()
            : afterPrefix[..spaceIndex].ToString().ToLowerInvariant();

        if (command != handler.Command)
            continue;

        var argString = spaceIndex < 0
            ? ""
            : afterPrefix[(spaceIndex + 1)..].ToString().TrimStart();
        var args = string.IsNullOrEmpty(argString)
            ? Array.Empty<string>()
            : argString.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var ctx = new CommandContext
        {
            Command = command,
            Args = args,
            ArgString = argString,
            Channel = msgEvt.Channel,
            Sender = msgEvt.Sender,
            RawMessage = msgEvt.RawMessage,
            Bot = Bot
        };

        await InvokeHandlerSafe(handler.Target, handler.Method, ctx, ct);
    }
}
```

**Performance note:** The current implementation does prefix matching and
command extraction once, then iterates handlers matching only on command name.
The new version does prefix matching per handler. This is fine for IRC
volumes, but if the handler count grows large, an optimization would be to
group handlers by prefix and only parse once per distinct prefix. This is
not worth doing upfront.

### 5. Remove the TODO comment

Delete the `// TODO: Make configurable per-bot` comment at line 166.

## Impact

- **Plugin API:** Adds `CommandPrefix` to `IBot` (additive, non-breaking).
  Adds `Prefix` property to `OnCommandAttribute` (additive, non-breaking —
  default `null` preserves current behavior).
- **Configuration:** Existing `CommandPrefix` property becomes functional.
  Default `"!"` preserves backward compatibility.
- **Tests:** Add tests for: default prefix dispatch, custom bot-wide prefix,
  per-handler prefix override, multi-character prefix, prefix case
  sensitivity.
