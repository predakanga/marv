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

## Recommendations for C# / Marv

### Primary: Static helper class + IrcColor enum

Follow Sopel's proven pattern, adapted to C# idioms:

```csharp
// Simple wrapping
IrcFormat.Bold("important")
IrcFormat.Color("warning", IrcColor.Red)
IrcFormat.Color("alert", IrcColor.White, IrcColor.Red)

// Composable via concatenation or interpolation
$"{IrcFormat.Bold("Nick")}: {IrcFormat.Color("message", IrcColor.Green)}"
```

This gives IntelliSense discoverability, compile-time type safety on colors,
and follows familiar C# patterns. The static methods are pure string
transforms with no allocations beyond the result string.

### Complement: String extension methods

For terser usage in plugins:

```csharp
"important".Bold()
"warning".Color(IrcColor.Red)
"alert".Color(IrcColor.White, IrcColor.Red)

// Chaining reads naturally
"text".Bold().Underline()
```

### Optional: Format stripping

A `IrcFormat.Strip(text)` method for removing all formatting codes from a
string. Useful for logging, length calculations, or plugins that need plain
text.

### Avoid

- **Template/escape approaches** -- curly braces and dollar signs conflict
  with C# interpolated strings, and provide no compile-time safety.
- **Builder pattern** -- adds complexity without much benefit since string
  concatenation and interpolation already compose well with the static/extension
  method approaches.
