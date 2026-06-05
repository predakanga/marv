# CS-003: Handler Filter Pipeline

**Source:** `downstream_suggestions/authorization.md` (full analysis)
**Scope:** Marv.Core.Plugin
**Complexity:** Medium
**Breaking changes:** None (additive virtual method + new interfaces)
**Depends on:** CS-002 (handler dispatch filters provide the attribute-level
context this builds on)

---

## Problem

Plugins need cross-cutting handler logic — primarily authorization, but also
rate limiting, auditing, and custom channel filtering. Currently this must be
implemented as boilerplate at the top of every handler method. The downstream
analysis evaluated six approaches and recommends a synthesis of approaches 2
and 5: a virtual `FilterHandlerAsync` method whose default implementation
evaluates self-describing filter attributes.

## Design

Two tiers of complexity for two audiences:

1. **Simple tier:** Plugin authors define attribute + evaluator pairs. The
   framework evaluates them automatically. No base class override needed.
2. **Advanced tier:** Plugin projects override `FilterHandlerAsync` for full
   control (OR conditions, context-dependent auth, custom denial messages).

The simple tier is implemented *via* the advanced tier — the default
`FilterHandlerAsync` implementation scans for `IFilteringAttribute` and
calls their evaluators. This avoids the "two parallel systems" problem.

## New types

### HandlerType enum

```csharp
/// <summary>The kind of handler being invoked.</summary>
public enum HandlerType
{
    Command,
    Regex,
    Event,
    RawMessage,
    Interval
}
```

### HandlerInvocation struct

```csharp
/// <summary>
/// Describes a handler about to be invoked. Passed to
/// <see cref="MarvPlugin.FilterHandlerAsync"/> for pre-invocation filtering.
/// </summary>
public readonly struct HandlerInvocation
{
    /// <summary>The handler method that will be invoked.</summary>
    public required MethodInfo Method { get; init; }

    /// <summary>The target object (plugin instance or handler group instance).</summary>
    public required object Target { get; init; }

    /// <summary>The handler type.</summary>
    public required HandlerType Type { get; init; }

    /// <summary>
    /// The context object passed to the handler. Cast to the appropriate type
    /// based on <see cref="Type"/>: CommandContext, RegexMatchContext, the
    /// event type, IrcMessage (raw), or null (interval).
    /// </summary>
    public required object? Context { get; init; }

    /// <summary>
    /// Custom attributes on the handler method, pre-cached during discovery.
    /// </summary>
    public required IReadOnlyList<Attribute> Attributes { get; init; }
}
```

### IFilteringAttribute interface

```csharp
/// <summary>
/// Marker interface for attributes that participate in handler filtering.
/// Attributes implementing this interface carry a reference to their evaluator
/// type, which is resolved from the DI container to perform the filter check.
/// </summary>
public interface IFilteringAttribute
{
    /// <summary>
    /// The evaluator type that implements <see cref="IFilterEvaluator"/>.
    /// Must be a concrete class resolvable from the DI container or via
    /// <see cref="IPluginActivator"/>.
    /// </summary>
    Type EvaluatorType { get; }
}
```

### IFilterEvaluator interface

```csharp
/// <summary>
/// Evaluates whether a handler should be invoked, based on the associated
/// <see cref="IFilteringAttribute"/> and the invocation context.
/// </summary>
public interface IFilterEvaluator
{
    /// <summary>
    /// Returns true if the handler should proceed, false to skip it.
    /// </summary>
    ValueTask<FilterResult> EvaluateAsync(
        IFilteringAttribute attribute,
        HandlerInvocation invocation,
        CancellationToken ct);
}
```

### FilterResult struct

```csharp
/// <summary>Result of a filter evaluation.</summary>
public readonly struct FilterResult
{
    /// <summary>Whether the handler is allowed to proceed.</summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// Optional denial message. If set and the context supports replies,
    /// the framework sends this as a reply before skipping the handler.
    /// </summary>
    public string? DenialMessage { get; init; }

    public static FilterResult Allowed => new() { IsAllowed = true };

    public static FilterResult Denied(string? message = null) =>
        new() { IsAllowed = false, DenialMessage = message };
}
```

### FilterEvaluator\<T\> base class

```csharp
/// <summary>
/// Typed base class for filter evaluators. Provides a type-safe bridge
/// so evaluators receive their specific attribute type without casting.
/// </summary>
public abstract class FilterEvaluator<TAttribute> : IFilterEvaluator
    where TAttribute : Attribute, IFilteringAttribute
{
    ValueTask<FilterResult> IFilterEvaluator.EvaluateAsync(
        IFilteringAttribute attribute,
        HandlerInvocation invocation,
        CancellationToken ct)
        => EvaluateAsync((TAttribute)attribute, invocation, ct);

    /// <summary>Evaluate the filter with the typed attribute.</summary>
    protected abstract ValueTask<FilterResult> EvaluateAsync(
        TAttribute attribute,
        HandlerInvocation invocation,
        CancellationToken ct);
}
```

## Changes to MarvPlugin

### 1. Pre-cache attributes during handler discovery

In `DiscoverHandlers`, after registering each handler, also cache all custom
attributes on the method. Store as a `IReadOnlyList<Attribute>` in the
registration record (or a parallel `Dictionary<MethodInfo, IReadOnlyList<Attribute>>`).

### 2. Add FilterHandlerAsync virtual method

```csharp
/// <summary>
/// Called before each handler invocation. Return false to skip the handler.
/// The default implementation evaluates any <see cref="IFilteringAttribute"/>
/// attributes on the handler method.
/// </summary>
protected virtual async ValueTask<bool> FilterHandlerAsync(
    HandlerInvocation invocation, CancellationToken ct)
{
    foreach (var attr in invocation.Attributes.OfType<IFilteringAttribute>())
    {
        var evaluator = ResolveEvaluator(attr.EvaluatorType);
        var result = await evaluator.EvaluateAsync(attr, invocation, ct);
        if (!result.IsAllowed)
        {
            if (result.DenialMessage is not null)
                await SendDenialReply(invocation, result.DenialMessage, ct);
            return false;
        }
    }
    return true;
}
```

### 3. Modify InvokeHandlerSafe

Add a `HandlerType` parameter and construct `HandlerInvocation` before
calling the handler:

```csharp
private async Task InvokeHandlerSafe(
    object target, MethodInfo method, object? arg,
    HandlerType type, CancellationToken ct)
{
    try
    {
        var invocation = new HandlerInvocation
        {
            Method = method,
            Target = target,
            Type = type,
            Context = arg,
            Attributes = GetCachedAttributes(method)
        };

        if (!await FilterHandlerAsync(invocation, ct))
            return;

        await InvokeHandler(target, method, arg!, ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "Handler {Type}.{Method} threw an exception",
            target.GetType().Name, method.Name);
    }
}
```

### 4. Evaluator resolution and caching

Resolve evaluators via `IPluginActivator.CreateInstance<T>()` on first use,
then cache them for the plugin's lifetime in a
`Dictionary<Type, IFilterEvaluator>`. This gives evaluators DI-injected
dependencies while avoiding per-invocation allocation.

### 5. Denial reply helper

A private `SendDenialReply` method that inspects the context type:

```csharp
private async Task SendDenialReply(
    HandlerInvocation invocation, string message, CancellationToken ct)
{
    switch (invocation.Context)
    {
        case CommandContext ctx:
            await ctx.ReplyAsync(message, ct);
            break;
        case RegexMatchContext ctx:
            await ctx.ReplyAsync(message, ct);
            break;
    }
}
```

## Usage examples

### Simple tier — self-evaluating attributes

```csharp
// In a downstream project — attribute definition
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireLevelAttribute(int level)
    : Attribute, IFilteringAttribute
{
    public int Level { get; } = level;
    public Type EvaluatorType => typeof(RequireLevelEvaluator);
}

// Evaluator — resolved from DI
public class RequireLevelEvaluator(IUserLevelService svc)
    : FilterEvaluator<RequireLevelAttribute>
{
    protected override async ValueTask<FilterResult> EvaluateAsync(
        RequireLevelAttribute attr, HandlerInvocation invocation,
        CancellationToken ct)
    {
        var sender = (invocation.Context as CommandContext)?.Sender;
        if (sender is null)
            return FilterResult.Allowed;

        var level = await svc.GetUserLevelAsync(sender, ct);
        return level >= attr.Level
            ? FilterResult.Allowed
            : FilterResult.Denied($"Requires level {attr.Level}.");
    }
}

// Handler usage
[OnCommand("gline")]
[RequireLevel(700)]
public async Task HandleGline(CommandContext ctx, CancellationToken ct) { ... }
```

### Advanced tier — FilterHandlerAsync override

```csharp
public abstract class MyProjectPlugin : MarvPlugin
{
    private readonly IMyAuthService _auth;

    protected MyProjectPlugin(IBot bot, IPluginActivator activator,
        ILoggerFactory loggerFactory, IMyAuthService auth)
        : base(bot, activator, loggerFactory)
    {
        _auth = auth;
    }

    protected override async ValueTask<bool> FilterHandlerAsync(
        HandlerInvocation invocation, CancellationToken ct)
    {
        // Custom OR logic: admin OR staff channel
        var requireAdmin = invocation.Attributes.OfType<RequireAdminAttribute>().Any();
        var staffBypass = invocation.Attributes.OfType<StaffChannelBypassAttribute>().Any();

        if (!requireAdmin && !staffBypass)
            return true;

        var sender = invocation.Context switch
        {
            CommandContext cmd => cmd.Sender,
            RegexMatchContext regex => regex.Sender,
            _ => null
        };
        if (sender is null) return true;

        var isAdmin = await _auth.IsAdminAsync(sender, ct);
        if (isAdmin) return true;

        if (staffBypass)
        {
            var channel = invocation.Context switch
            {
                CommandContext cmd => cmd.Channel?.Name,
                RegexMatchContext regex => regex.Channel?.Name,
                _ => null
            };
            if (channel == "#staff") return true;
        }

        if (invocation.Context is CommandContext ctx)
            await ctx.ReplyAsync("Permission denied.", ct);
        return false;
    }
}
```

## Design decisions

### Why not a middleware pipeline?

Overengineered for the typical IRC bot use case. One or two cross-cutting
concerns don't justify pipeline infrastructure. `FilterHandlerAsync` can
handle multiple concerns in a single override. Revisit if three or more
independent concerns emerge across multiple downstream projects.

### Why not `[RequireAuth]` in the framework?

The permission string is meaningless without knowing the auth model. Every
downstream project brings its own authorization model. The framework should
not prescribe one — `IFilteringAttribute` lets each project define its own
semantics. See the full analysis in `downstream_suggestions/authorization.md`.

### Handler groups and filtering

`FilterHandlerAsync` runs on the owning plugin, not the handler group. The
`HandlerInvocation.Target` field distinguishes the plugin from its groups,
so the filter can branch if needed. Handler groups should not independently
override filtering — they are owned by their plugin.

### FilterHandlerAsync exceptions

If `FilterHandlerAsync` throws, treat it like a handler exception: log the
error and skip the handler. This is the safe default — a broken filter
should not allow a handler to run unguarded.

## Framework surface area

| Type | Lines (approx) |
|---|---|
| `HandlerType` enum | 8 |
| `HandlerInvocation` struct | 20 |
| `IFilteringAttribute` interface | 8 |
| `IFilterEvaluator` interface | 8 |
| `FilterEvaluator<T>` base class | 15 |
| `FilterResult` struct | 15 |
| `FilterHandlerAsync` default impl | 15 |
| `InvokeHandlerSafe` changes | 10 |
| Evaluator cache + resolution | 15 |
| Attribute pre-caching | 10 |
| `SendDenialReply` helper | 12 |
| **Total** | **~136** |

## Impact

- **Plugin API:** Adds one virtual method to `MarvPlugin`, six new types in
  `Marv.Core.Plugin`. All additive, no breaking changes.
- **Existing plugins:** Unaffected. `FilterHandlerAsync` defaults to
  evaluating `IFilteringAttribute`; plugins with no such attributes see
  zero behavior change.
- **Performance:** One virtual call per handler invocation (~2ns). Negligible
  for IRC message volumes.

## Open questions

1. Should `HandlerInvocation` include handler registration metadata (matched
   command name, regex pattern)? Useful for logging but enlarges the struct.
   Recommendation: include it — it's cheap and aids debugging.
2. Should the framework ship a simple `[RequireAuth]` attribute that uses
   `IAuthorizationService` as a built-in `IFilteringAttribute`? This would
   give `Marv.Plugins.Auth` a declarative surface. Recommendation: yes, but
   ship it in `Marv.Plugins.Auth`, not in `Marv.Core`, so it's opt-in.
