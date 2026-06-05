# CS-005: Bulk Channel Join

**Source:** `downstream_suggestions/improvements.md` §5
**Scope:** Marv.Core
**Complexity:** Small-Medium
**Breaking changes:** None (additive API)

---

## Problem

When the bot needs to join multiple channels (post-authentication, or when a
plugin requests joins), it currently sends individual `JOIN` commands in a
loop. This is slow due to outbound rate limiting and generates unnecessary
traffic. IRC allows comma-separated channel lists in a single `JOIN` command.

## Changes

### 1. Add `JoinMultipleAsync` to `IBot`

```csharp
/// <summary>
/// Joins multiple channels in a single IRC JOIN command.
/// Channel names are comma-separated per RFC 2812.
/// </summary>
Task JoinMultipleAsync(
    IReadOnlyList<string> channels,
    CancellationToken ct);
```

### 2. Implementation in MarvBot

Construct a single `JOIN` command with comma-separated channel names:

```csharp
public async Task JoinMultipleAsync(
    IReadOnlyList<string> channels, CancellationToken ct)
{
    if (channels.Count == 0) return;

    // IRC line length limit is 512 bytes. Batch channels to fit.
    // "JOIN " = 5 bytes, "\r\n" = 2 bytes, leaves 505 for channel list.
    const int maxLineLength = 505;
    var batch = new List<string>();
    var currentLength = 0;

    foreach (var channel in channels)
    {
        var addedLength = batch.Count == 0
            ? channel.Length
            : channel.Length + 1; // +1 for comma

        if (currentLength + addedLength > maxLineLength && batch.Count > 0)
        {
            await SendJoinBatch(batch, ct);
            batch.Clear();
            currentLength = 0;
            addedLength = channel.Length;
        }

        batch.Add(channel);
        currentLength += addedLength;
    }

    if (batch.Count > 0)
        await SendJoinBatch(batch, ct);
}

private Task SendJoinBatch(List<string> channels, CancellationToken ct)
{
    var joined = string.Join(',', channels);
    return SendRawAsync(new IrcMessage("JOIN", [joined]), ct);
}
```

### 3. Use in core channel join logic

The bot's post-authentication channel join (which iterates
`MarvConfiguration.Channels`) should use `JoinMultipleAsync` instead of
looping over `JoinAsync`.

### 4. Channel keys

The simple version above doesn't handle per-channel keys. Two options:

- **Option A:** Overload that accepts `(channel, key?)` tuples. Channels
  with keys must be first in the JOIN command per RFC 2812.
- **Option B:** Only batch keyless channels. Channels with keys use
  individual `JoinAsync` calls.

**Recommendation:** Option B for now. Key-protected channels are uncommon
in bot deployments. The primary win is batching the bulk of keyless channels.

## Slipstream / coalescing (deferred)

The downstream suggestion mentions a slipstream approach: maintaining a
pending join list and coalescing joins into a single message when flushed.
This is a good idea for generalization (could apply to WHO, MODE, etc.) but
raises layering concerns — the connection layer would need awareness of
pending commands.

**Recommendation:** Defer coalescing. `JoinMultipleAsync` gives the caller
explicit control over batching without complicating the connection layer. If
coalescing proves necessary, it can be added as a connection-level feature
later without changing the `IBot` API.

## Impact

- **Plugin API:** Adds one method to `IBot`. Additive, non-breaking.
- **Core behavior:** Initial channel join becomes a single IRC command
  instead of N individual commands. Faster startup, less rate-limit
  pressure.
- **Tests:** Add unit test for batching logic (line-length splitting).
  Integration test for multi-channel join.
