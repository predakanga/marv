# CS-024: SendAndAwaitAsync ENDOF* Fallback

**Source:** Downstream feature request
**Scope:** Core
**Complexity:** Medium
**Breaking changes:** None (behavioral improvement to existing method)
**Status:** Pending

---

## Problem

`SendAndAwaitAsync` currently relies on the `labeled-response` IRCv3
capability to correlate requests with responses. When `labeled-response`
is not available, the method falls back to sending the message and
immediately returning an empty list — making it effectively useless:

```csharp
else
{
    // Fallback: just send and return empty (no correlation available)
    await SendRawAsync(message, ct);
    return [];
}
```

Many IRC commands have well-defined response sequences terminated by an
`ENDOF*` numeric. For example:

| Command | Reply numerics | Terminator |
|---|---|---|
| `WHO` | `352` (RPL_WHOREPLY) | `315` (RPL_ENDOFWHO) |
| `WHOIS` | `311`, `312`, `313`, `317`, `318`, `319`, etc. | `318` (RPL_ENDOFWHOIS) |
| `WHOWAS` | `314` (RPL_WHOWASUSER) | `369` (RPL_ENDOFWHOWAS) |
| `LIST` | `322` (RPL_LIST) | `323` (RPL_LISTEND) |
| `NAMES` | `353` (RPL_NAMREPLY) | `366` (RPL_ENDOFNAMES) |
| `BANLIST` (`MODE +b`) | `367` (RPL_BANLIST) | `368` (RPL_ENDOFBANLIST) |
| `LINKS` | `364` (RPL_LINKS) | `365` (RPL_ENDOFLINKS) |
| `INFO` | `371` (RPL_INFO) | `374` (RPL_ENDOFINFO) |

When `labeled-response` is unavailable, the bot can still correlate these
commands by watching for the corresponding terminator numeric and matching
on the command parameter (e.g. the nick in `WHO nick` appears in the
`315` reply).

## Changes

### 1. Define a command-to-terminator mapping

```csharp
/// <summary>
/// Maps IRC commands to their ENDOF* terminator numerics for
/// fallback response correlation.
/// </summary>
private static readonly Dictionary<string, string> EndOfNumerics = new(StringComparer.OrdinalIgnoreCase)
{
    ["WHO"] = "315",
    ["WHOIS"] = "318",
    ["WHOWAS"] = "369",
    ["LIST"] = "323",
    ["NAMES"] = "366",
    ["LINKS"] = "365",
    ["INFO"] = "374",
};
```

### 2. Implement fallback correlation in `SendAndAwaitAsync`

When `labeled-response` is not available **and** the command has a known
terminator, install a temporary message tap that:

1. Buffers all incoming numerics related to the command.
2. Matches on the command parameter (e.g. channel or nick) to avoid
   cross-talk with other pending requests.
3. Completes when the terminator numeric is received.
4. Times out after 30 seconds (same as labeled-response path).

```csharp
else if (EndOfNumerics.TryGetValue(message.Command, out var terminator))
{
    var matchParam = message.Parameters.Count > 0 ? message.Parameters[0] : null;
    var tcs = new TaskCompletionSource<IReadOnlyList<IrcMessage>>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var buffer = new List<IrcMessage>();
    var key = $"endof-{message.Command}-{matchParam}";

    _pendingEndOf[key] = (tcs, buffer, terminator, matchParam);

    await SendRawAsync(message, ct);

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

    try
    {
        return await tcs.Task.WaitAsync(timeoutCts.Token);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
        _pendingEndOf.TryRemove(key, out _);
        throw new TimeoutException(
            $"Timed out waiting for {terminator} response to {message.Command}.");
    }
}
else
{
    // Unknown command, no correlation possible — warn and throw
    _logger.LogWarning(
        "SendAndAwaitAsync: no labeled-response support and no known ENDOF* " +
        "terminator for command '{Command}'. Response correlation is not possible",
        message.Command);
    throw new NotSupportedException(
        $"Cannot correlate responses for '{message.Command}': the server does not " +
        $"support labeled-response and no ENDOF* fallback is defined for this command.");
}
```

### 3. Hook into the message processing loop

In the main message processing switch (or as a pre-dispatch check),
check incoming numerics against `_pendingEndOf`:

```csharp
// Check for ENDOF* fallback correlation
foreach (var (key, pending) in _pendingEndOf)
{
    var (tcs, buffer, terminator, matchParam) = pending;

    // Match: the numeric's parameter matches our expected param
    if (matchParam is not null && message.Parameters.Count > 1
        && !CaseComparer.Equals(message.Parameters[1], matchParam))
        continue;

    if (message.Command == terminator)
    {
        buffer.Add(message);
        _pendingEndOf.TryRemove(key, out _);
        tcs.TrySetResult(buffer.AsReadOnly());
    }
    else
    {
        // Buffer intermediate numerics
        buffer.Add(message);
    }
}
```

The numeric matching needs care: response numerics include the bot's nick
as the first parameter, with the queried target as the second. For
example, `315 BotNick targetNick :End of WHO list`. So the match should
compare `message.Parameters[1]` against the original command's first
parameter.

### 4. Add `_pendingEndOf` dictionary to `IrcBot`

```csharp
private readonly ConcurrentDictionary<string, (
    TaskCompletionSource<IReadOnlyList<IrcMessage>> Tcs,
    List<IrcMessage> Buffer,
    string Terminator,
    string? MatchParam)> _pendingEndOf = new();
```

Clear it in `RunAsync` alongside `_pendingLabels`.

## Design decisions

**Why not use the ENDOF* fallback always (even when labeled-response is
available)?** Labeled-response is strictly superior — it correlates any
command, not just ones with known terminators, and uses server-assigned
labels for unambiguous matching. The ENDOF* approach is inherently racy
when multiple requests for the same command/target are in flight.

**Why match on the command parameter?** Without parameter matching, two
concurrent `WHO #channel1` and `WHO #channel2` requests would
cross-contaminate. Parameter matching isn't perfect (servers may
normalize the parameter), but it handles the common case.

**What about commands not in the table?** The fallback logs a warning and
throws `NotSupportedException`. Silent empty returns would mask bugs —
a plugin calling `SendAndAwaitAsync` expects responses, and getting an
empty list with no indication that correlation failed is misleading.
Throwing forces the caller to handle the unsupported case explicitly
(e.g. falling back to `SendRawAsync` + `[OnRawMessage]`). The table
can be extended over time as new commands are identified.

**Why not a general "numeric sequence collector"?** Over-engineering. The
terminator pattern is well-established in the IRC protocol and covers all
the common query commands.

## Testing

- **Unit test:** Send WHO with labeled-response available — verify the
  labeled-response path is still used (existing behavior).
- **Unit test:** Send WHO without labeled-response — verify responses are
  collected up to and including `315`, then returned.
- **Unit test:** Send WHO without labeled-response, server doesn't respond
  — verify TimeoutException after 30s.
- **Unit test:** Two concurrent WHO requests for different targets —
  verify responses are correctly separated.
- **Unit test:** Send an unsupported command without labeled-response —
  verify a warning is logged and `NotSupportedException` is thrown.
- **Integration test:** Connect to ngircd (which does not support
  labeled-response), send WHO, verify the reply list is non-empty.

## Impact

- **Plugin DX:** `SendAndAwaitAsync` becomes useful on servers that lack
  `labeled-response` support, which includes most traditional IRC servers.
  The Moderation example plugin's `!who` command will work on these servers.
- **API surface:** No changes to `IBot`. Behavioral improvement only.
- **Risk:** Low — the labeled-response path is unchanged. The new fallback
  only activates when labeled-response is unavailable.
