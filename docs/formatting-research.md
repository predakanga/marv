# IRC Formatting API Research

## Raw Control Codes

IRC formatting uses inline control characters (originally from mIRC). Each
formatting toggle wraps styled text: `\x02bold text\x02` renders **bold text**.

| Format        | Hex    | Dec | Notes                                        |
|---------------|--------|-----|----------------------------------------------|
| Bold          | `0x02` |  2  | Toggle                                       |
| Italic        | `0x1D` | 29  | Toggle                                       |
| Underline     | `0x1F` | 31  | Toggle                                       |
| Strikethrough | `0x1E` | 30  | Toggle; less widely supported                |
| Monospace     | `0x11` | 17  | Toggle; IRCv3 extension, limited support     |
| Color         | `0x03` |  3  | Followed by `fg[,bg]` digits; bare = reset   |
| Hex Color     | `0x04` |  4  | Followed by `RRGGBB[,RRGGBB]`; bare = reset |
| Reverse       | `0x16` | 22  | Swaps foreground/background                  |
| Reset         | `0x0F` | 15  | Clears all formatting                        |

### mIRC Color Palette

Colors 0-15 are the standard palette:

| Code | Color        | Code | Color         |
|------|--------------|------|---------------|
|  0   | White        |  8   | Yellow        |
|  1   | Black        |  9   | Light Green   |
|  2   | Blue (Navy)  | 10   | Cyan (Teal)   |
|  3   | Green        | 11   | Light Cyan    |
|  4   | Red          | 12   | Light Blue    |
|  5   | Brown        | 13   | Pink          |
|  6   | Purple       | 14   | Grey          |
|  7   | Orange       | 15   | Light Grey    |

Colors 16-98 form an extended palette (roughly mapping to ANSI 256-color
ranges). Color 99 is the "default" (reset to client default).

Color syntax: `\x03<fg>` or `\x03<fg>,<bg>`. Leading zeros are significant:
`\x031` is color 1 (black), `\x0301` is also color 1 but avoids ambiguity
when followed by a digit.

## Survey of Existing Approaches

### 1. Sopel (Python) -- Static Functions

Sopel provides a `sopel.formatting` module with simple wrapper functions:

```python
from sopel.formatting import bold, color, italic, colors

message = bold("Important: ") + color("warning text", colors.RED)
```

Key functions: `bold(text)`, `italic(text)`, `underline(text)`,
`strikethrough(text)`, `monospace(text)`, `color(text, fg, bg=None)`,
`hex_color(text, fg, bg=None)`, `plain(text)` (strips formatting).

The `colors` enum provides named constants for the 16 standard colors.

**Pros:** Simple, discoverable, composable via concatenation.
**Cons:** Nesting is manual string concatenation; deeply formatted messages
get verbose.

### 2. Limnoria/Supybot (Python) -- Module Functions

Similar pattern to Sopel via `ircutils`:

```python
from supybot.ircutils import bold, mircColor, underline

message = bold(mircColor("text", "red"))
```

**Pros:** Same simplicity as Sopel.
**Cons:** Older codebase, missing newer codes (strikethrough, monospace, hex
color). Color names are strings rather than enums.

### 3. irc-colors (Node.js) -- Fluent/Chainable API

Uses JavaScript Proxy to allow chaining of format modifiers:

```javascript
const c = require('irc-colors');
c.bold.red.bgyellow("formatted text")
c.rainbow("colorful text")
```

Also supports a global `String.prototype` extension mode:
```javascript
"text".bold().red().bgyellow()
```

**Pros:** Very ergonomic for JS; reads naturally.
**Cons:** Relies on Proxy/getter magic that doesn't translate to statically
typed languages. Implicit state in the chain.

### 4. Cinch (Ruby) -- Symbol-Based Formatting

Uses a single `format` method with symbol arguments:

```ruby
Format(:bold, :red, "text")
```

**Pros:** Concise, all formatting in one call.
**Cons:** Not very discoverable; symbol names must be memorized.

### 5. girc (Go) -- Template Tags

Uses a `Fmt()` function with brace-delimited tags:

```go
girc.Fmt("{b}bold{b} and {red}red{c}")
```

Tag names: `{b}` bold, `{i}` italic, `{u}` underline, `{s}` strikethrough,
`{m}` monospace, `{c}` color reset, `{red}` / `{blue}` etc. for named colors.

**Pros:** Very readable; complex formatting stays concise.
**Cons:** Curly braces conflict with C# string interpolation (`$"..."`).
No compile-time validation of tag names. Runtime parsing cost.

### 6. ergochat/irc-go ircfmt (Go) -- Escape Notation + Structured Parsing

Uses `$` as escape prefix:

```go
ircfmt.Unescape("$bhello$b $c[red]world$c")
```

Also provides `ircfmt.Split(text)` which returns `[]FormattedSubstring`,
each carrying its text content and active format flags. This enables
bidirectional formatting: both producing and analyzing formatted text.

**Pros:** Bidirectional (format and parse). Structured output for analysis.
**Cons:** Custom escape syntax to learn. Dollar-sign may collide with
template patterns in some contexts.

## Patterns Summary

| Pattern             | Examples              | Type Safety | Discoverability | Nesting | C# Fit |
|---------------------|-----------------------|-------------|-----------------|---------|--------|
| Static functions    | Sopel, Limnoria       | Medium      | High (IDE)      | Manual  | Good   |
| Fluent/chainable    | irc-colors            | Low         | High            | Built-in| Medium |
| Symbol/enum args    | Cinch                 | Medium      | Low             | N/A     | Medium |
| Template tags       | girc                  | None        | Low             | Built-in| Poor   |
| Escape notation     | ergochat              | None        | Low             | Built-in| Poor   |
| Extension methods   | (no IRC example)      | High        | High            | Natural | Good   |

## Real-World Example Analysis

The following message is representative of the formatting patterns plugins
will actually produce -- a multi-field info line with colored brackets,
labels, and values:

```
\x0310,01[\x037 Community \x0310] :: [\x033 Network: \x037NBC \x0310] :: [ \x033Runtime:\x037 25 minutes \x0310] :: [\x033 Rating:\x037 \x02TV-PG\x02\x0310 ] :: [\x0314 https://thetvdb.com/series/community \x0310]\017
```

Rendered (conceptually):
**[** Community **] :: [** Network: NBC **] :: [** Runtime: 25 minutes **] :: [** Rating: **TV-PG** **] :: [** https://... **]**
(brackets/separators in teal-on-black, labels in green, values in orange,
URL in grey, bold on the rating, reset at end)

Key observations:
- Colors change mid-flow without resetting -- `\x037` switches fg to orange
  without closing the previous green. This is a **color push**, not a
  wrap-and-reset pattern.
- The background (`01`) is set once on the first bracket and persists via
  IRC's sticky color semantics until reset.
- Bold is toggled inline within a color span (`\x02TV-PG\x02`).
- The message ends with a single `\x0F` reset.
- There are no balanced open/close pairs for colors; formatting is
  **stateful**, not **structural**.

### Approach 1: Static wrap functions (Sopel-style)

```csharp
// Awkward: Color() wraps text and resets, but the real message relies on
// sticky colors that bleed across segments.
IrcFormat.Color("[", IrcColor.Cyan, IrcColor.Black)
+ IrcFormat.Color(" Community ", IrcColor.Orange)
+ IrcFormat.Color("] :: [", IrcColor.Cyan)
+ IrcFormat.Color(" Network: ", IrcColor.Green)
// ...
```

**Problems:**
- Each `Color()` call inserts a color code at the start *and* resets at the
  end, which fights against the sticky-color style this message uses.
- The background color set in the first segment would be reset by the second
  `Color()` call, requiring it to be re-specified repeatedly.
- Deeply nested: bold inside color inside a segment requires
  `IrcFormat.Color(IrcFormat.Bold("TV-PG"), IrcColor.Orange)`, but this
  inserts a color reset between the bold close and the next segment's color.
- The result would be **significantly longer on the wire** than the
  hand-written version due to redundant color codes.

### Approach 2: Extension methods (chaining)

```csharp
"[".Color(IrcColor.Cyan, IrcColor.Black)
+ " Community ".Color(IrcColor.Orange)
+ "] :: [".Color(IrcColor.Cyan)
// ...
```

**Same problems as approach 1** -- extension methods are syntactic sugar
over the same wrap-and-reset model.

### Approach 3: Template tags (girc-style)

Ignoring the C# interpolation conflict for a moment:

```
{cyan,black}[ {orange}Community {cyan}] :: [ {green}Network: {orange}NBC {cyan}] :: [ {green}Runtime: {orange}25 minutes {cyan}] :: [ {green}Rating: {orange}{b}TV-PG{b} {cyan}] :: [ {grey}https://thetvdb.com/series/community {cyan}]{reset}
```

**This reads almost identically to the raw IRC**, because the template model
matches IRC's stateful semantics: tags change the current state without
implying a close. The 1:1 correspondence makes it easy to reason about what
will be sent on the wire.

### Approach 4: Hybrid -- low-level color codes + high-level wrappers

Provide both:
- **`IrcColor` constants** that expand to raw `\x03N` sequences for the
  stateful/push pattern
- **`IrcFormat.Color(text, fg, bg?)`** wrappers for the simple
  wrap-and-reset pattern
- **`IrcFormat.Bold(text)`** etc. for toggle formats

```csharp
// Using color constants for the stateful pattern:
$"{IrcColor.Cyan.OnBlack()}[ {IrcColor.Orange} Community {IrcColor.Cyan}]"
+ $" :: [ {IrcColor.Green}Network: {IrcColor.Orange}NBC {IrcColor.Cyan}]"
+ $" :: [ {IrcColor.Green}Runtime: {IrcColor.Orange}25 minutes {IrcColor.Cyan}]"
+ $" :: [ {IrcColor.Green}Rating: {IrcColor.Orange}{IrcFormat.Bold("TV-PG")} {IrcColor.Cyan}]"
+ $" :: [ {IrcColor.Grey} https://thetvdb.com/series/community {IrcColor.Cyan}]"
+ IrcFormat.Reset

// Simple cases still use the convenient wrappers:
IrcFormat.Bold("important")
"warning".Color(IrcColor.Red)
```

This works because `IrcColor.Orange` in an interpolated string simply
emits `\x037` -- it changes the foreground without resetting anything. The
`IrcFormat.Color(text, fg)` wrapper remains available for simple cases where
you want balanced open/close semantics.

### Conclusions

The example reveals a fundamental mismatch: **IRC formatting is stateful
(like a terminal), but the wrap-and-reset API pattern (Sopel-style) assumes
structural/balanced formatting (like HTML).** Simple messages work fine
with wrappers, but the bread-and-butter output of info-line plugins requires
direct access to the raw color-change codes.

The best API provides **both levels**:

1. **Low-level**: `IrcColor` enum/constants that emit raw codes, usable
   directly in string interpolation for stateful formatting.
2. **High-level**: `IrcFormat.Bold(text)`, `IrcFormat.Color(text, fg, bg)`
   wrappers for the common simple case.
3. **Utility**: `IrcFormat.Reset`, `IrcFormat.Strip(text)`.

This avoids forcing plugin authors to memorize hex codes while still
supporting the full expressiveness of IRC formatting.

## Recommendations for C# / Marv

### Layer 1: IrcColor enum with ToString() / string interpolation

An `IrcColor` enum whose members produce raw `\x03N` sequences when used in
string interpolation. This is the foundation for stateful formatting:

```csharp
$"{IrcColor.Cyan.OnBlack()}[{IrcColor.Orange} Community {IrcColor.Cyan}]"
```

The enum should provide:
- Implicit or explicit conversion to the `\x03N` string
- An `On(IrcColor bg)` or `OnBlack()` etc. method for `\x03fg,bg`

### Layer 2: IrcFormat static helpers for wrap-and-reset

For simple cases where balanced formatting is natural:

```csharp
IrcFormat.Bold("important")
IrcFormat.Color("warning", IrcColor.Red)
IrcFormat.Color("alert", IrcColor.White, IrcColor.Red)
```

These wrap the text with the appropriate open/close codes.

### Layer 3: String extension methods

Optional ergonomic sugar for the wrap-and-reset pattern:

```csharp
"important".Bold()
"warning".Color(IrcColor.Red)
```

### Constants

`IrcFormat.Reset` -- emits `\x0F`.
`IrcFormat.Bold()` (no-arg) -- emits bare `\x02` toggle for stateful use.

### Utility

`IrcFormat.Strip(text)` -- removes all formatting codes. Useful for logging,
length calculations, or plugins that need plain text.

### Avoid

- **Template/escape approaches** -- curly braces and dollar signs conflict
  with C# interpolated strings, and provide no compile-time safety.
- **Builder pattern** -- adds complexity without benefit; string interpolation
  with the layered API above is more readable and more flexible.
- **Wrap-and-reset only** -- as the example analysis shows, real plugin
  output relies heavily on stateful color changes. An API that only provides
  wrapping forces plugin authors to fall back to raw control characters for
  their most common use case.
