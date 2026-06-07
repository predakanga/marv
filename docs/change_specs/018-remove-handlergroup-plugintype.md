# CS-018: Remove PluginType from HandlerGroupAttribute

**Source:** `TODO.md` item 8
**Scope:** Marv.Core.Plugin
**Complexity:** Small
**Breaking changes:** Yes (attribute constructor signature change)

---

## Problem

`HandlerGroupAttribute` requires a `Type pluginType` constructor argument
that specifies which plugin owns the handler group:

```csharp
[HandlerGroup(typeof(ModerationPlugin))]
public class ModerationAdminCommands { ... }
```

This has several issues:

1. **Redundancy:** Handler groups are discovered by scanning the plugin's
   own assembly. `DiscoverHandlerGroups` already filters by
   `attr.PluginType == pluginType` — but since the scan starts from the
   plugin's assembly, a handler group in that assembly can only belong to
   a plugin in the same assembly. The `PluginType` property duplicates
   information that the framework already knows.

2. **Coupling:** The handler group must reference the concrete plugin
   class, creating a circular type dependency if the plugin also
   references the handler group. More practically, it means handler groups
   in a separate assembly (a possible future pattern) would need a
   project reference back to the plugin assembly just for the attribute.

3. **Boilerplate:** Every handler group requires importing and specifying
   the plugin type, which is ceremony without value since there's only
   one plugin per assembly in practice.

## Decisions

- Remove the `Type pluginType` constructor parameter from
  `HandlerGroupAttribute`.
- `HandlerGroupAttribute` becomes a simple marker attribute with no
  required arguments.
- `DiscoverHandlerGroups` scans the plugin's assembly for any class
  marked with `[HandlerGroup]` and treats them all as belonging to the
  current plugin.
- If an assembly contains multiple plugins (an unusual but valid
  configuration), handler groups are associated with all plugins in the
  same assembly. This is acceptable — the scenario is rare and the
  handler group's handlers execute in the context of each plugin's
  dispatch loop. If more precise ownership is needed later, an optional
  `PluginType` property can be reintroduced.

## Changes

### 1. Simplify `HandlerGroupAttribute`

```csharp
/// <summary>
/// Marks a class as a handler group. Handler groups are discovered in the
/// plugin's assembly and instantiated by <see cref="MarvPlugin"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HandlerGroupAttribute : Attribute;
```

### 2. Update `DiscoverHandlerGroups` in `MarvPlugin`

Remove the `PluginType` filter:

```csharp
private void DiscoverHandlerGroups(IPluginActivator activator)
{
    var assembly = GetType().Assembly;

    var groupTypes = assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false })
        .Where(t => t.GetCustomAttribute<HandlerGroupAttribute>() is not null);

    foreach (var groupType in groupTypes)
    {
        var createMethod = typeof(IPluginActivator)
            .GetMethod(nameof(IPluginActivator.CreateInstance))!
            .MakeGenericMethod(groupType);

        var group = createMethod.Invoke(activator, [Array.Empty<object>()])!;
        _handlerGroups.Add(group);
        DiscoverHandlers(group, groupType);
    }
}
```

### 3. Update example plugins

Update `ModerationAdminCommands`:

```csharp
// Before
[HandlerGroup(typeof(ModerationPlugin))]
public class ModerationAdminCommands { ... }

// After
[HandlerGroup]
public class ModerationAdminCommands { ... }
```

### 4. Update `docs/PLUGIN_API.md`

Document the simplified `[HandlerGroup]` attribute usage.

### 5. Update `Marv.Testing` if applicable

If `PluginTestHarness` or other test infrastructure references
`HandlerGroupAttribute.PluginType`, update accordingly.

## Design decisions

**Why not make `PluginType` optional instead of removing it?** An optional
property adds complexity for a feature with no current use case. The only
scenario where explicit ownership matters (multiple plugins in one
assembly) is unusual enough that it can be addressed if and when it
arises. Keeping the attribute simple follows YAGNI.

**Why not use a different discovery mechanism (e.g., nested classes)?**
Nested classes would enforce ownership through language syntax, but C#
nested classes are awkward for DI-instantiated types and create deeply
qualified names. The assembly-scanning approach is consistent with how
plugins themselves are discovered.

## Impact

- **Plugin API:** **Breaking change** to `HandlerGroupAttribute`.
  Existing handler groups must remove the `typeof(XPlugin)` argument.
  This is a one-line change per handler group.
- **Binary compatibility:** Broken — plugins must be recompiled.
  Acceptable pre-1.0.
- **Existing plugins:** `Marv.Plugins.Moderation`'s
  `ModerationAdminCommands` is the only handler group in the codebase.
  One-line change.
- **Tests:** Update any tests that construct `HandlerGroupAttribute`
  with a type argument.
