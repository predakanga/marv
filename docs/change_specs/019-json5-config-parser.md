# CS-019: JSON5 Configuration Parser

**Source:** `TODO.md` item 9
**Scope:** Marv (host application)
**Complexity:** Small-Medium
**Breaking changes:** None

---

## Problem

The JSON configuration parser uses `Microsoft.Extensions.Configuration`'s
built-in `AddJsonFile`, which only supports strict JSON (RFC 8259). This
means configuration files cannot contain:

- Comments (single-line `//` or block `/* */`)
- Trailing commas after the last element in arrays/objects
- Unquoted keys
- Single-quoted strings
- Multiline strings

Comments are the most impactful limitation. Operators commonly want to
annotate their configuration files with explanations, comment out
sections for debugging, or leave notes about non-obvious settings. YAML
configuration already supports comments, but JSON is the default format
(the example config is `marv.example.json` and the default is
`marv.json`).

## Decisions

- Replace the standard JSON configuration provider with one that supports
  JSON5 (or at minimum, comments and trailing commas).
- The configuration file extension remains `.json` — JSON5 is a superset
  of JSON, so existing valid JSON files continue to work.
- Do not introduce a `.json5` extension. Operators use `.json` and expect
  it to work with comments. A separate extension would fragment
  configuration.

## Approach options

### Option A: Custom `IConfigurationProvider` using `System.Text.Json`

`System.Text.Json` supports `JsonCommentHandling.Skip` and
`AllowTrailingCommas` via `JsonDocumentOptions`. These cover the two most
requested JSON5 features without adding a dependency.

This requires implementing a custom `IConfigurationSource` and
`IConfigurationProvider` that reads the JSON file with permissive options
and flattens it into the key-value pairs that `IConfiguration` expects.

### Option B: Pre-process with a JSON5 library

Use a JSON5 parsing library (e.g., `Json5.Net` or manual preprocessing)
to convert JSON5 to strict JSON before passing to `AddJsonFile`. This
supports the full JSON5 spec but adds a dependency and a preprocessing
step.

### Recommendation: Option A

Comments and trailing commas are the only practical pain points. The full
JSON5 spec (unquoted keys, single quotes, hex literals, multiline
strings) adds complexity without clear value for a configuration file.
Option A uses only the standard library and requires no external
dependencies.

## Changes

### 1. Create `PermissiveJsonConfigurationSource`

```csharp
namespace Marv;

/// <summary>
/// Configuration source that reads JSON files with support for comments
/// and trailing commas, using System.Text.Json's permissive parsing.
/// </summary>
internal sealed class PermissiveJsonConfigurationSource : IConfigurationSource
{
    public required string Path { get; init; }
    public bool Optional { get; init; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new PermissiveJsonConfigurationProvider(this);
}
```

### 2. Create `PermissiveJsonConfigurationProvider`

```csharp
internal sealed class PermissiveJsonConfigurationProvider
    : ConfigurationProvider
{
    private readonly PermissiveJsonConfigurationSource _source;

    public PermissiveJsonConfigurationProvider(
        PermissiveJsonConfigurationSource source)
    {
        _source = source;
    }

    public override void Load()
    {
        var path = _source.Path;
        if (!File.Exists(path))
        {
            if (_source.Optional)
                return;
            throw new FileNotFoundException(
                $"Configuration file '{path}' not found.");
        }

        using var stream = File.OpenRead(path);
        var options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        using var doc = JsonDocument.Parse(stream, options);
        Data = JsonConfigurationFlattener.Flatten(doc.RootElement);
    }
}
```

### 3. Create `JsonConfigurationFlattener`

A utility that recursively walks a `JsonElement` tree and produces the
flat `Dictionary<string, string?>` that `ConfigurationProvider.Data`
expects, using `:` as the key separator (matching the standard JSON
config provider's behavior):

```csharp
internal static class JsonConfigurationFlattener
{
    public static Dictionary<string, string?> Flatten(JsonElement root)
    {
        var data = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase);
        Visit(data, "", root);
        return data;
    }

    private static void Visit(
        Dictionary<string, string?> data, string prefix, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix)
                        ? prop.Name
                        : $"{prefix}:{prop.Name}";
                    Visit(data, key, prop.Value);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Visit(data, $"{prefix}:{index}", item);
                    index++;
                }
                break;
            default:
                data[prefix] = element.ValueKind == JsonValueKind.Null
                    ? null
                    : element.ToString();
                break;
        }
    }
}
```

### 4. Update `AddConfigFile` in `Program.cs`

Replace the `AddJsonFile` call with the permissive provider:

```csharp
case ".json":
    config.Add(new PermissiveJsonConfigurationSource
    {
        Path = path,
        Optional = !required,
    });
    break;
```

### 5. Update `marv.example.json`

Add comments to the example configuration to demonstrate the feature:

```json
{
    // Connection settings
    "Server": "irc.example.com",
    "Port": 6697,
    "UseTls": true,

    // Bot identity
    "Nick": "Marv",

    // Channels to join on connect
    "Channels": [
        "#general",
        "#dev",  // trailing comma OK
    ],

    "CommandPrefix": "!",
    "PluginDirectories": ["plugins"],
    "Plugins": ["Greet"],
}
```

## Design decisions

**Why not support the full JSON5 spec?** JSON5 features beyond comments
and trailing commas (unquoted keys, single-quoted strings, hex literals,
multiline strings, `Infinity`/`NaN`) are unusual in configuration files
and would require a third-party parser or significant custom code.
`System.Text.Json`'s built-in permissive options handle the common cases
with zero dependencies.

**Why not just recommend YAML?** YAML is already supported and handles
comments natively. However, JSON is the default format, the example config
is JSON, and many operators prefer JSON's explicit syntax. Telling users
"switch to YAML for comments" is a workaround, not a solution.

**Why implement a custom provider instead of patching the stream?** A
stream-preprocessing approach (stripping comments before passing to
`AddJsonFile`) is fragile — it must handle comments inside strings,
escaped characters, and edge cases. `System.Text.Json`'s parser handles
these correctly with `JsonCommentHandling.Skip`.

## Testing

- **Unit tests for flattener:** Verify that nested objects, arrays, null
  values, and mixed types produce the correct flat key-value pairs.
- **Unit tests for permissive parsing:** Verify that JSON with comments
  (single-line and block), trailing commas, and standard JSON all parse
  correctly.
- **Integration test:** Load a configuration file with comments and
  verify the resulting `MarvConfiguration` properties are correct.
- **Regression test:** Verify that existing `marv.example.json` (without
  comments) continues to load correctly.

## Impact

- **Configuration:** JSON config files now support `//` comments, `/* */`
  block comments, and trailing commas. Existing valid JSON files work
  without changes.
- **Dependencies:** No new dependencies. Uses `System.Text.Json` which is
  already part of the .NET runtime.
- **Plugin API:** No changes.
