# CS-015: Regex Options for OnRegex Attribute — COMPLETED

**Source:** `TODO.md` item 4
**Scope:** Marv.Core.Plugin
**Complexity:** Small
**Breaking changes:** None
**Status:** Completed

---

## Problem

The `[OnRegex]` attribute compiles patterns with only `RegexOptions.Compiled`.
Plugin authors cannot specify additional regex options such as
`RegexOptions.IgnoreCase`, `RegexOptions.Singleline`, or
`RegexOptions.IgnorePatternWhitespace`. This forces authors to embed
inline flags in the pattern itself (e.g., `(?i)hello`) or to use
`[OnRawMessage]` with manual regex handling.

The `OnCommandAttribute` already supports several optional properties
(`ChannelOnly`, `DirectOnly`, `Channel`, `Prefix`). Adding `Options` to
`OnRegexAttribute` follows the same pattern.

## Decisions

- Add an `Options` property to `OnRegexAttribute` of type
  `RegexOptions`.
- Default value is `RegexOptions.None` — the framework always adds
  `RegexOptions.Compiled` internally, so the author only specifies
  behavioral options.
- The property uses `init` style, consistent with other optional
  properties on the attribute.

## Changes

### 1. Add `Options` property to `OnRegexAttribute`

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class OnRegexAttribute(string pattern) : Attribute
{
    /// <summary>The regular expression pattern to match against message text.</summary>
    public string Pattern { get; } = pattern;

    /// <summary>
    /// Additional <see cref="RegexOptions"/> applied when compiling the pattern.
    /// <see cref="RegexOptions.Compiled"/> is always added by the framework.
    /// Defaults to <see cref="RegexOptions.None"/>.
    /// </summary>
    public RegexOptions Options { get; init; } = RegexOptions.None;

    /// <summary>If true, handler only fires for channel messages (skips DMs).</summary>
    public bool ChannelOnly { get; init; }

    /// <summary>If true, handler only fires for direct/private messages (skips channels).</summary>
    public bool DirectOnly { get; init; }

    /// <summary>
    /// If set, handler only fires when the message is in this channel.
    /// Compared case-insensitively.
    /// </summary>
    public string? Channel { get; init; }
}
```

### 2. Update regex compilation in `MarvPlugin.DiscoverHandlers`

In the handler discovery loop, combine the author-specified options with
`RegexOptions.Compiled`:

```csharp
// [OnRegex] handlers
foreach (var regexAttr in method.GetCustomAttributes<OnRegexAttribute>())
{
    WarnOnConflictingFilters(regexAttr.ChannelOnly, regexAttr.DirectOnly, regexAttr.Channel,
        target.GetType().Name, method.Name, "OnRegex");
    _regexHandlers.Add(new RegexRegistration(
        target, method,
        new Regex(regexAttr.Pattern, RegexOptions.Compiled | regexAttr.Options),
        regexAttr.ChannelOnly, regexAttr.DirectOnly, regexAttr.Channel));
}
```

### 3. Update `docs/PLUGIN_API.md`

Document the new `Options` property with examples.

## Usage examples

```csharp
// Case-insensitive matching
[OnRegex(@"hello\s+(\w+)", Options = RegexOptions.IgnoreCase)]
public async Task HandleHello(RegexMatchContext ctx, CancellationToken ct)
{
    await ctx.ReplyAsync($"Hi {ctx.Match.Groups[1].Value}!", ct);
}

// Verbose pattern with comments
[OnRegex(@"
    ^!roll\s+          # command prefix
    (\d+)d(\d+)        # NdM dice notation
    (?:\+(\d+))?       # optional modifier
    $",
    Options = RegexOptions.IgnorePatternWhitespace)]
public async Task HandleRoll(RegexMatchContext ctx, CancellationToken ct) { ... }
```

## Impact

- **Plugin API:** Adds one optional property to `OnRegexAttribute`
  (additive, non-breaking). Default `RegexOptions.None` preserves current
  behavior — existing plugins compile unchanged.
- **Performance:** No change. The `Compiled` flag is always present.
  Additional options like `IgnoreCase` have negligible runtime impact.
- **Tests:** Add tests for: default options (Compiled only),
  IgnoreCase matching, combined options, IgnorePatternWhitespace.
