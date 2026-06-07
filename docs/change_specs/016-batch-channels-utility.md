# CS-016: Extract BatchChannels to Utility Class

**Source:** `TODO.md` item 6
**Scope:** Marv.Core
**Complexity:** Trivial
**Breaking changes:** None (method is `internal`)

---

## Problem

`IrcBot.BatchChannels` is an `internal static` method that splits a list
of channel names into batches fitting the 512-byte IRC line length limit.
This logic is useful to any plugin that needs to batch IRC parameters
(e.g., sending MODE commands for multiple channels, or WHO queries). Since
it's `internal` to `IrcBot`, plugins cannot use it.

## Decisions

- Move the method to a new `public static` utility class
  `IrcMessageUtility` in `Marv.Core.Irc`.
- Keep the existing `IrcBot` code calling the moved method.
- Name the method `BatchParameters` to reflect its general purpose
  (batching any comma-separated parameter list within the IRC line limit),
  not just channels.
- Keep an `internal` forwarding call from `IrcBot` to avoid churn in the
  existing code, or update `IrcBot.JoinMultipleAsync` to call the utility
  directly.

## Changes

### 1. Create `IrcMessageUtility` class

```csharp
namespace Marv.Core.Irc;

/// <summary>
/// Utility methods for constructing IRC messages within protocol limits.
/// </summary>
public static class IrcMessageUtility
{
    /// <summary>
    /// Splits a list of values into batches that fit within the IRC
    /// line length limit when joined with commas. Useful for batching
    /// JOIN, MODE, WHO, and other commands that accept comma-separated
    /// parameter lists.
    /// </summary>
    /// <param name="values">The values to batch.</param>
    /// <param name="maxPayloadLength">
    /// Maximum byte length for the comma-separated list. Defaults to 505,
    /// accounting for a typical command prefix (5 bytes) and CRLF (2 bytes)
    /// within the 512-byte IRC line limit.
    /// </param>
    public static IEnumerable<List<string>> BatchParameters(
        IReadOnlyList<string> values, int maxPayloadLength = 505)
    {
        // Implementation moved from IrcBot.BatchChannels
    }
}
```

### 2. Update `IrcBot`

Replace the `BatchChannels` method body with a call to the utility:

```csharp
internal static IEnumerable<List<string>> BatchChannels(
    IReadOnlyList<string> channels, int maxPayloadLength = 505)
    => IrcMessageUtility.BatchParameters(channels, maxPayloadLength);
```

Alternatively, update `JoinMultipleAsync` to call
`IrcMessageUtility.BatchParameters` directly and remove `BatchChannels`
entirely. Since `BatchChannels` is `internal`, this has no external
impact.

### 3. Move existing tests

Any existing tests for `IrcBot.BatchChannels` should be updated to test
`IrcMessageUtility.BatchParameters`. Add a test that verifies `IrcBot`'s
join batching still works (integration-level, not unit-level on the
moved method).

## Impact

- **Plugin API:** Adds `IrcMessageUtility.BatchParameters` as a new public
  utility. Non-breaking.
- **Existing code:** `IrcBot.JoinMultipleAsync` behavior is unchanged.
- **Tests:** Existing batch tests move to the new class. No behavior
  change.
