# CS-032: Disable Auto-Reconnect Option — COMPLETED

**Source:** GitHub issue #8
**Scope:** Core / Host
**Complexity:** Small
**Breaking changes:** None — new opt-in configuration property, default preserves current behaviour
**Status:** Completed

---

## Problem

The bot unconditionally reconnects after any connection failure, with
exponential backoff hardcoded in `MarvBotService.ExecuteAsync`. Downstream
projects that want exceptions to terminate the process (e.g. to let a
supervisor restart the bot with a clean state) have no way to opt out.

## Changes

### 1. Add `AutoReconnect` property to `MarvConfiguration`

Add a new boolean property defaulting to `true`:

```csharp
/// <summary>
/// Whether the bot should automatically reconnect after a connection
/// failure. When false, the bot logs the error and exits with a
/// non-zero exit code.
/// </summary>
[Description("Auto-reconnect on connection failure (default: true).")]
public bool AutoReconnect { get; init; } = true;
```

This preserves current behaviour for existing users. The property will
automatically appear as a CLI argument via the reflection-based
`ConfigurationOptions` generator, and can be set in JSON configuration
or environment variables.

### 2. Modify `MarvBotService.ExecuteAsync` to respect the setting

When `AutoReconnect` is `false`:

- After an unexpected disconnection (the `catch (Exception ex)` block at
  line 87–90), log the error as today.
- Still run the disconnect cleanup (`NotifyDisconnectedAsync`,
  `UnloadPluginsAsync`) so plugins can clean up.
- After cleanup, set the process exit code to a non-zero value
  (e.g. `Environment.ExitCode = 1`) and return from `ExecuteAsync`
  instead of looping. The `BackgroundService` completing will trigger
  the generic host to shut down.

The `OperationCanceledException` path (graceful shutdown) remains
unchanged regardless of the setting.

### 3. Unit tests

- Test that when `AutoReconnect` is `true` (default), the service
  attempts to reconnect after a simulated connection failure.
- Test that when `AutoReconnect` is `false`, the service exits after a
  single connection failure and sets a non-zero exit code.
- Test that disconnect cleanup (plugin notification/unload) runs
  regardless of the `AutoReconnect` setting.

## Design decisions

- **Non-zero exit code, not re-thrown exception:** The downstream use
  case is to let a process supervisor detect the failure and restart.
  A clean exit with a non-zero code is more predictable than letting an
  exception propagate through the generic host's unhandled exception
  path. The error is already logged before the exit.
- **Backoff timing left as hardcoded constants:** Per the issue
  discussion, making backoff configurable is a separate concern and is
  not included here.

## Testing

- Unit test: `AutoReconnect = true` → service loops after simulated
  disconnect.
- Unit test: `AutoReconnect = false` → service exits after simulated
  disconnect with non-zero exit code.
- Unit test: disconnect cleanup runs in both modes.
- Manual test: set `"AutoReconnect": false` in config, connect to a
  server, kill the server, verify the bot exits.

## Impact

- **Plugin API:** No changes.
- **DX:** New configuration option, documented via `[Description]`
  attribute and visible in `--help` output.
- **Risk:** Low — default behaviour is unchanged; the new code path is
  a strict subset (skip the reconnect loop).
