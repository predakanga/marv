# CS-016: Extract BatchChannels to Utility Class

**Source:** `TODO.md` item 6
**Scope:** Marv.Core
**Complexity:** Trivial
**Breaking changes:** None (method is `internal`)

---

## Problem

`IrcBot.BatchChannels` is an `internal static` method that splits a list
of channel names into batches fitting the 512-byte IRC line length limit.
This logic is useful to any plugin that needs to send batched IRC commands
(e.g., joining many channels, sending MODE for multiple targets). Since
it's `internal` to `IrcBot`, plugins cannot use it.

## Decisions

- Move the method to a new `public static` utility class
  `IrcUtils` in `Marv.Core.Utils`.
- Keep the method named `BatchChannels` — it operates on channel name
  lists specifically, and generalizing to arbitrary comma-separated
  parameters risks missing protocol-specific quirks (e.g., channels with
  keys use a different format than plain comma-separated lists).
- The `maxPayloadLength` parameter is required (no default). The correct
  value depends on the IRC command being constructed, and encoding a
  default would bake in assumptions about which command the caller is
  building. `IrcBot.JoinMultipleAsync` passes the value it computes for
  JOIN; other callers compute theirs for their command.
- Remove `IrcBot.BatchChannels` entirely and update `JoinMultipleAsync`
  to call `IrcUtils.BatchChannels` directly.

## Changes

### 1. Create `IrcUtils` class

```csharp
namespace Marv.Core.Utils;

/// <summary>
/// Utility methods for working with IRC protocol constraints.
/// </summary>
public static class IrcUtils
{
    /// <summary>
    /// Splits a list of channel names into batches where each batch's
    /// comma-separated representation fits within
    /// <paramref name="maxPayloadLength"/> bytes. Useful for batching
    /// JOIN, MODE, and other channel-list commands within the 512-byte
    /// IRC line limit.
    /// </summary>
    /// <param name="channels">The channel names to batch.</param>
    /// <param name="maxPayloadLength">
    /// Maximum byte length for the comma-separated channel list.
    /// The caller must account for the command prefix and CRLF when
    /// computing this value (e.g., 512 - len("JOIN ") - len("\r\n")).
    /// </param>
    public static IEnumerable<List<string>> BatchChannels(
        IReadOnlyList<string> channels, int maxPayloadLength)
    {
        // Implementation moved from IrcBot.BatchChannels
    }
}
```

### 2. Update `IrcBot`

Remove the `BatchChannels` method from `IrcBot`. Update
`JoinMultipleAsync` to call `IrcUtils.BatchChannels` directly, passing
the computed max payload length (currently 505):

```csharp
foreach (var batch in IrcUtils.BatchChannels(channelNames, maxPayloadLength: 505))
{
    var joinList = string.Join(",", batch);
    await SendRawAsync(new IrcMessage("JOIN", [joinList]), ct);
}
```

### 3. Move existing tests

Update existing tests for `IrcBot.BatchChannels` to test
`IrcUtils.BatchChannels` instead. Since the parameter no longer has a
default, all test call sites must pass `maxPayloadLength` explicitly.

## Impact

- **Plugin API:** Adds `IrcUtils.BatchChannels` as a new public utility
  in `Marv.Core.Utils`. Non-breaking.
- **Existing code:** `IrcBot.JoinMultipleAsync` behavior is unchanged.
  `IrcBot.BatchChannels` is removed (was `internal`, no external impact).
- **Tests:** Existing batch tests move to the new class. No behavior
  change.
