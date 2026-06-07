# CS-019: JSON5 Configuration Parser — COMPLETED

**Source:** `TODO.md` item 9
**Scope:** Marv (host application)
**Complexity:** Trivial
**Breaking changes:** None
**Status:** Completed

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

- Use the [`Json5.Configuration`](https://www.nuget.org/packages/Json5.Configuration)
  NuGet package, which provides `AddJson5File` — a drop-in replacement
  for `AddJsonFile` with full JSON5 support.
- Handle both `.json` and `.json5` extensions via `AddJson5File`, since
  JSON5 is a strict superset of JSON. Existing valid JSON files continue
  to work unchanged.
- The default configuration filename remains `marv.json`.

## Changes

### 1. Add the `Json5.Configuration` NuGet package to `Marv.csproj`

```xml
<PackageReference Include="Json5.Configuration" Version="1.0.4" />
```

### 2. Update `AddConfigFile` in `Program.cs`

Replace `AddJsonFile` with `AddJson5File` for JSON files, and add
`.json5` as a supported extension:

```csharp
using Json5;

static void AddConfigFile(IConfigurationBuilder config, string path, bool required)
{
    var extension = Path.GetExtension(path).ToLowerInvariant();

    switch (extension)
    {
        case ".json" or ".json5":
            config.AddJson5File(path, optional: !required, reloadOnChange: false);
            break;
        case ".yaml" or ".yml":
            config.AddYamlFile(path, optional: !required, reloadOnChange: false);
            break;
        case ".xml":
            config.AddXmlFile(path, optional: !required, reloadOnChange: false);
            break;
        default:
            throw new InvalidOperationException(
                $"Unsupported configuration file format: '{extension}'. " +
                "Supported formats: .json, .json5, .yaml, .yml, .xml");
    }
}
```

### 3. Update `marv.example.json`

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
        "#dev",
    ],

    "CommandPrefix": "!",
    "PluginDirectories": ["plugins"],
    "Plugins": ["Greet"],
}
```

## Design decisions

**Why `Json5.Configuration` instead of a custom provider?** The package
provides exactly what we need — `AddJson5File` as a drop-in `AddJsonFile`
replacement with full JSON5 support (comments, trailing commas, unquoted
keys, single-quoted strings, etc.). Implementing our own provider would
duplicate work that's already been done and tested. The package is from
[devlooped](https://github.com/devlooped/json5), a reputable open-source
maintainer, targets .NET 8+ and is compatible with .NET 10.

**Why not just recommend YAML?** YAML is already supported and handles
comments natively. However, JSON is the default format, the example config
is JSON, and many operators prefer JSON's explicit syntax. Telling users
"switch to YAML for comments" is a workaround, not a solution.

**Why support `.json5` as an extension?** Some users may prefer to use the
`.json5` extension to signal that the file uses JSON5 features, and some
editors provide better syntax highlighting for `.json5` files. Supporting
both costs nothing since the parser handles both.

## Testing

- **Regression test:** Verify that existing `marv.example.json` continues
  to load correctly through `AddJson5File`.
- **Integration test:** Load a configuration file containing comments and
  trailing commas and verify the resulting `MarvConfiguration` properties
  are correct.

## Impact

- **Configuration:** JSON config files now support all JSON5 features.
  Existing valid JSON files work without changes.
- **Dependencies:** Adds `Json5.Configuration` (which depends on `Json5`
  and `Microsoft.Extensions.Configuration.FileExtensions`).
- **Plugin API:** No changes.
