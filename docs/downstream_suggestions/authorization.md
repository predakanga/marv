# Upstream Authorization for Marv — Design Options

Analysis of how Marv could support declarative authorization for plugin
handlers, examining six approaches: attribute-based `[RequireAuth]`,
a virtual `FilterHandler` override, a hybrid, a middleware pipeline,
self-evaluating filter attributes, and `[RequireAuth]` with a dynamic
policy object.

---

## Background: Current State

### Marv's Existing Auth Surface

Marv ships a minimal `IAuthorizationService` in `Marv.Plugins.Auth`:

```csharp
public interface IAuthorizationService
{
    Task<bool> IsAuthorizedAsync(IUser user, string permission, CancellationToken ct);
}
```

The bundled implementation (`AccountBasedAuthService`) checks IRC
services account names against a configured admin list — a single
binary check with no permission hierarchy.

### How We Use Authorization

We do not use Marv's `IAuthorizationService` at all. We have our
own `IUserAuthService` which returns an `AuthResult` containing
`UserId`, `Username`, `UserClass`, and `IsAdmin`. Authorization
decisions are made through four distinct patterns:

| Pattern | Description | Used by |
|---|---|---|
| **A — Admin flag** | `auth.IsAdmin` (derived from hostmask class) | BanManagement, GeoIp, Gline, UserMod, Lookup, Twitter |
| **B — Hostmask class** | `HostmaskParser.IsAdminClass(class)` without full auth | StaffAccess, BotCommandHandlers |
| **C — Permission level** | DB query for `permissions.Level >= N` | Gline (≥700), Search (≥100), Calc (≥100), UserInfo |
| **D — Nick list** | `BotAdmins` config list | BotCommandHandlers (join/part/say/die) |
| **E — Staff channel** | Allow if message is in the staff channel | Combined with A in 6 plugins |

Most handlers combine patterns — the typical guard is "admin OR in
staff channel" (A+E), and Gline requires both admin status (A) and a
minimum permission level (C) for staff protection checks.

### Key Observation

Our authorization is fundamentally different from Marv's bundled auth.
Any upstream mechanism must accommodate both — and unknown future
projects with their own models.

---

## Approach 1: Attribute-Based `[RequireAuth]`

### Design

Add an attribute that declares authorization requirements on handler
methods. The `MarvPlugin` dispatch loop checks this attribute before
invoking the handler:

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireAuthAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}
```

Usage in a handler:

```csharp
[OnCommand("ban")]
[RequireAuth("admin")]
public async Task HandleBan(CommandContext ctx, CancellationToken ct) { ... }
```

The dispatch loop in `MarvPlugin.InvokeHandlerSafe` would check for
`[RequireAuth]` attributes and call `IAuthorizationService` before
invoking the handler. If authorization fails, the handler is skipped
(or a configurable denial message is sent).

### Strengths

- **Declarative and discoverable.** Authorization requirements are
  visible at the method signature, not buried in the first 10 lines
  of the method body.
- **Zero boilerplate per handler.** No manual auth check code needed.
- **Familiar pattern.** Matches ASP.NET `[Authorize]`, Discord.NET
  `[RequireUserPermission]`, DSharpPlus `[RequirePermissions]`.
- **Framework-enforced.** Can't forget to check — the attribute
  prevents the handler from running at all.

### Weaknesses

#### 1. The Permission String Problem

The attribute takes a `string permission`, but what does that string
mean? In Marv's bundled auth, it's ignored entirely —
`AccountBasedAuthService.IsAuthorizedAsync` returns true for admins
regardless of the permission string.

For us, the natural permissions would be:

```csharp
[RequireAuth("admin")]              // Pattern A: IsAdmin check
[RequireAuth("level:100")]          // Pattern C: permission level ≥ 100
[RequireAuth("level:700")]          // Pattern C: permission level ≥ 700
[RequireAuth("bot-admin")]          // Pattern D: nick in BotAdmins list
```

But this embeds authorization model semantics into string conventions.
A project using RBAC would want `[RequireAuth("moderator")]`. A
project using OAuth scopes would want `[RequireAuth("channels:manage")]`.
The attribute is universal, but the permission strings are
project-specific — there's no way for the framework to validate them.

#### 2. Compound Conditions Don't Fit

Our most common pattern is "admin OR staff channel" — a disjunction
of two different checks. This doesn't map cleanly to a single attribute:

```csharp
// Option A: Two attributes = AND (both required)? Or OR?
[RequireAuth("admin")]
[RequireAuth("staff-channel")]

// Option B: Special syntax in the string?
[RequireAuth("admin|staff-channel")]

// Option C: A separate attribute?
[RequireAuthAny("admin", "staff-channel")]
```

All of these are awkward. The fundamental issue is that authorization
policies are often more complex than "does the user have permission X?"
— they involve context (which channel?), combinations (admin OR
channel), and cascading checks (admin, then also check level for
certain operations).

#### 3. Context-Dependent Authorization

Some of our authorization depends on the handler's arguments or the
command context:

- Gline checks if the **target** user has a higher permission level
  than the **invoker** — this requires running the handler's argument
  parsing first.
- UserInfo returns different amounts of information based on the
  invoker's permission level — authorization isn't binary.
- BotCommandHandlers checks the nick list only for specific
  sub-commands (join, part, say, die, restart).

`[RequireAuth]` can only express "can this user invoke this method at
all?" — not "can this user invoke this method with these arguments?"

#### 4. Denial Response Customization

When authorization fails, what happens? Options:

1. **Silent skip** — handler doesn't run, no response. Bad UX.
2. **Generic "Permission denied"** — acceptable but impersonal.
3. **Custom denial** — "You need to be at least Power User (level
   100) to use this command." Requires the auth service to provide
   the denial reason, which couples the framework to the auth model.

We currently send custom denial messages from within handlers.
An attribute-based system would need a way to customize this — perhaps
a `DenialMessage` property on the attribute, or a
`FormatDenialAsync` method on `IAuthorizationService`.

### Verdict

**Good for simple, universal authorization (admin-or-not).** Becomes
increasingly awkward as authorization models grow more complex. Works
well as a complement to other approaches (handle the simple cases
declaratively, handle complex cases in code).

---

## Approach 2: Virtual `FilterHandler` on MarvPlugin

### Design

Add a virtual method to `MarvPlugin` that is called before every
handler invocation:

```csharp
public abstract class MarvPlugin
{
    /// <summary>
    /// Called before each handler invocation. Return false to prevent the
    /// handler from running. The implementation may send its own response
    /// (e.g. a "permission denied" message).
    /// </summary>
    protected virtual ValueTask<bool> FilterHandlerAsync(
        HandlerInvocation invocation, CancellationToken ct)
    {
        return ValueTask.FromResult(true);
    }
}
```

Where `HandlerInvocation` provides the handler metadata and context:

```csharp
public readonly struct HandlerInvocation
{
    /// <summary>The handler method that will be invoked.</summary>
    public MethodInfo Method { get; init; }

    /// <summary>The target object (plugin or handler group instance).</summary>
    public object Target { get; init; }

    /// <summary>The handler type (Command, Regex, Event, RawMessage, Interval).</summary>
    public HandlerType Type { get; init; }

    /// <summary>
    /// The context object that will be passed to the handler.
    /// Cast to CommandContext, RegexMatchContext, or the event type as needed.
    /// </summary>
    public object Context { get; init; }

    /// <summary>
    /// All custom attributes on the handler method.
    /// Useful for reading project-specific attributes (e.g. [RequireLevel(100)]).
    /// </summary>
    public IReadOnlyList<Attribute> Attributes { get; init; }
}
```

A project like ours would create a base class:

```csharp
public abstract class SamplePlugin : MarvPlugin
{
    private readonly IUserAuthService _auth;

    protected SamplePlugin(IBot bot, IPluginActivator activator,
        ILoggerFactory loggerFactory, IUserAuthService auth)
        : base(bot, activator, loggerFactory)
    {
        _auth = auth;
    }

    protected override async ValueTask<bool> FilterHandlerAsync(
        HandlerInvocation invocation, CancellationToken ct)
    {
        // Read project-specific attributes
        var requireLevel = invocation.Attributes
            .OfType<RequireLevelAttribute>()
            .FirstOrDefault();

        if (requireLevel == null)
            return true; // No auth required

        // Get the sender from whichever context type
        var sender = invocation.Context switch
        {
            CommandContext cmd => cmd.Sender,
            RegexMatchContext regex => regex.Sender,
            _ => null
        };

        if (sender == null)
            return true;

        var auth = await _auth.AuthenticateAsync(sender, ct);
        if (auth.IsAdmin)
            return true;

        // Check permission level
        // ... query DB for level, compare to requireLevel.MinLevel

        // Send denial
        if (invocation.Context is CommandContext ctx)
            await ctx.ReplyAsync("Permission denied.");

        return false;
    }
}
```

We would then define our own attributes:

```csharp
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireLevelAttribute(int minLevel) : Attribute
{
    public int MinLevel { get; } = minLevel;
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireAdminAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class StaffChannelBypassAttribute : Attribute;
```

And use them on handlers:

```csharp
[OnCommand("ban")]
[RequireAdmin]
[StaffChannelBypass]
public async Task HandleBan(CommandContext ctx, CancellationToken ct) { ... }

[OnCommand("gline")]
[RequireLevel(700)]
public async Task HandleGline(CommandContext ctx, CancellationToken ct) { ... }
```

### Strengths

#### 1. Fully Flexible Authorization Model

The plugin project defines its own attributes and its own
`FilterHandlerAsync` logic. RBAC, integer levels, capability flags,
OAuth scopes — any model works. The framework doesn't need to
understand the semantics.

#### 2. Context-Aware Filtering

`FilterHandlerAsync` receives the full invocation context, so it can
inspect the channel, the sender, the arguments — anything needed for
the authorization decision.

#### 3. Not Just Authorization

The same mechanism supports other cross-cutting concerns:
- **Channel filtering:** Skip handlers that should only run in
  specific channels (replaces the ubiquitous
  `if (ctx.IsDirect) return;` guard).
- **Rate limiting:** Track invocation frequency per user.
- **Logging/auditing:** Log all command invocations centrally.
- **Cooldowns:** Prevent command spam.

#### 4. Single Override Point

One method in the base class handles all filtering. Plugin authors
write handlers that focus on business logic — the authorization is
handled uniformly by the base class.

#### 5. Incremental Adoption

Projects that don't need filtering just don't override the method.
Existing plugins continue to work unchanged.

### Weaknesses

#### 1. Boilerplate for Plugin Authors

The primary concern. To use this pattern, a plugin project must:

1. Create a base class (`ExplorationPlugin : MarvPlugin`)
2. Override `FilterHandlerAsync` with the project's auth logic
3. Define custom attributes
4. Update all plugin constructors to inject auth services and pass
   them to the base class
5. Update all handler group constructors if they need to participate

This is a significant amount of infrastructure code. For a project
with 2-3 plugins, it's arguably not worth it — you'd write more
framework code than you save in handler guards.

**Mitigation:** This is a one-time cost per project, and it pays
off as the number of plugins and handlers grows. We have 16 plugins
with ~30 authorized handlers — the base class approach saves
~5 lines per handler at the cost of ~50 lines of base class setup.
The break-even point is around 10 authorized handlers.

#### 2. Handler Groups Complicate the Picture

Handler groups are instantiated by `IPluginActivator` and their
handlers are dispatched by the owning plugin. `FilterHandlerAsync`
would naturally apply to handler group methods too — but the handler
group might need its own authorization logic.

The `HandlerInvocation.Target` field distinguishes the plugin from
its handler groups, so the filter can branch on this. But it means
the base class must understand the handler group's authorization
needs, which could get complicated.

**Mitigation:** Handler groups could define their own attributes and
the base class reads them the same way. Since `invocation.Attributes`
contains the method's attributes regardless of which class defines
the method, this works naturally.

#### 3. Authorization Happens Before Argument Parsing

Some handlers need to parse arguments before making authorization
decisions (e.g. Gline checks the target user's level).
`FilterHandlerAsync` runs before the handler, so it doesn't have
access to parsed arguments.

This is inherent to any pre-handler filter. Handlers with
argument-dependent authorization would still need inline checks.

**Mitigation:** Document this as a known limitation. In practice,
this affects only Gline's staff protection check — the primary "can
the user invoke this at all?" check still works in the filter.

#### 4. Reflection-Based Attribute Reading

Reading `invocation.Attributes` on every handler call involves
attribute reflection. This is cheap (attributes are cached by the
CLR after first read), but it's a pattern that some developers find
surprising in hot paths.

**Mitigation:** Pre-read attributes during handler discovery and
store them in the handler registration. The `HandlerInvocation` then
carries the pre-computed list.

#### 5. Virtual Method on Every Dispatch

Every handler invocation — including regex handlers that fire on
every message — would call `FilterHandlerAsync`. The base
implementation returns `true` immediately, but the virtual call
dispatch still has a cost.

**Mitigation:** The cost is negligible. A virtual method call is
~2ns. IRC bots process maybe 100 messages/second at peak.

### Implementation in MarvPlugin

The change to `MarvPlugin` is small. In `InvokeHandlerSafe`:

```csharp
private async Task InvokeHandlerSafe(
    object target, MethodInfo method, object arg, CancellationToken ct)
{
    try
    {
        var invocation = new HandlerInvocation
        {
            Method = method,
            Target = target,
            Type = /* determined from registration */,
            Context = arg,
            Attributes = _attributeCache.GetOrAdd(method,
                m => m.GetCustomAttributes(true).Cast<Attribute>().ToList())
        };

        if (!await FilterHandlerAsync(invocation, ct))
            return;

        await InvokeHandler(target, method, arg, ct);
    }
    catch ...
}
```

This is roughly 10 lines of framework code plus the
`HandlerInvocation` struct (~20 lines) and the `HandlerType` enum
(~5 lines). Minimal framework surface change.

### Verdict

**The most flexible approach.** Accommodates any authorization model
without the framework needing to understand it. The cost is
up-front boilerplate for plugin projects — worth it for projects
with many handlers, potentially not for small projects.

---

## Approach 3: Hybrid — Framework Attribute + Virtual Filter

### Design

Provide *both* mechanisms:

1. A simple `[RequireAuth("permission")]` attribute in Marv.Core that
   uses `IAuthorizationService` for the common case.
2. A virtual `FilterHandlerAsync` for projects that need custom
   authorization logic.

The dispatch order:

```
1. FilterHandlerAsync (custom project logic)
2. [RequireAuth] check via IAuthorizationService (framework logic)
3. Handler invocation
```

Or reversed — `FilterHandlerAsync` could run *after* the framework
check, acting as an additional layer. The order depends on whether
project-specific filters should be able to override the framework
check (allow even if `[RequireAuth]` would deny) or only restrict
further (deny even if `[RequireAuth]` would allow).

### The Problem: Two Systems to Learn

This combines the weaknesses of both approaches:

- Plugin authors must understand both `[RequireAuth]` and
  `FilterHandlerAsync` to know which to use.
- The interaction between them (ordering, override semantics) adds
  cognitive load.
- A project like ours would likely ignore `[RequireAuth]`
  entirely and use only `FilterHandlerAsync`, making the attribute
  dead weight.
- A project using only `[RequireAuth]` would never touch
  `FilterHandlerAsync`, making the virtual method dead weight.

### When It Works

The hybrid shines if Marv establishes a standard permission model
that most projects use, while `FilterHandlerAsync` serves as an
escape hatch. If Marv's `IAuthorizationService` evolves to support
permission hierarchies and the `[RequireAuth]` attribute covers 80%
of use cases, the filter handles the remaining 20%.

But that's a bet on the future shape of Marv's auth — and the
exploration experience suggests that projects will bring their own
authorization models.

### Verdict

**Not recommended as a starting point.** If `[RequireAuth]` proves
sufficient for most projects, `FilterHandlerAsync` can always be
added later. If `FilterHandlerAsync` is needed from the start,
`[RequireAuth]` adds complexity without pulling its weight. Pick one
and add the other only if a concrete need emerges.

---

## Approach 4 (Bonus): Middleware Pipeline

### Design

Instead of a single virtual method, implement a middleware pipeline
similar to ASP.NET Core:

```csharp
public abstract class MarvPlugin
{
    protected void UseMiddleware<T>() where T : IHandlerMiddleware;
}

public interface IHandlerMiddleware
{
    Task InvokeAsync(HandlerInvocation invocation,
        Func<Task> next, CancellationToken ct);
}
```

Projects compose middleware:

```csharp
public class AuthMiddleware(IUserAuthService auth) : IHandlerMiddleware
{
    public async Task InvokeAsync(HandlerInvocation invocation,
        Func<Task> next, CancellationToken ct)
    {
        if (!await Authorize(invocation, ct))
            return;
        await next();
    }
}
```

### Why Not

This is overengineered for an IRC bot framework. The middleware
pipeline pattern is valuable when you have many independent
cross-cutting concerns that compose arbitrarily (ASP.NET: auth,
CORS, routing, compression, logging). An IRC bot plugin typically
has one cross-cutting concern: authorization. Building a pipeline
infrastructure for one middleware is a poor trade-off.

If a second cross-cutting concern emerges (rate limiting, auditing),
`FilterHandlerAsync` can still handle it — it's just one virtual
method, not a single-concern system.

### Verdict

**Over-engineered.** Revisit if three or more independent
cross-cutting concerns emerge.

---

## Approach 5: Self-Evaluating Filter Attributes

### Design

Each filter attribute carries a `Type` reference to its own evaluator
class. The framework discovers `IFilteringAttribute` on handler methods,
resolves the evaluator from the DI container, and calls it. No base
class override needed — the framework handles all plumbing.

```csharp
// Framework-provided interfaces
public interface IFilterEvaluator
{
    ValueTask<bool> FilterAsync(IFilteringAttribute attr,
        HandlerInvocation invocation, CancellationToken ct);
}

public interface IFilteringAttribute
{
    Type Evaluator { get; }
}
```

A project defines attribute + evaluator pairs:

```csharp
// Attribute — declares the requirement
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireLevelAttribute(int level)
    : Attribute, IFilteringAttribute
{
    public int Level { get; } = level;
    public Type Evaluator => typeof(RequireLevelFilter);
}

// Evaluator — implements the check, resolved from DI
public class RequireLevelFilter(IUserLevelService svc) : IFilterEvaluator
{
    public async ValueTask<bool> FilterAsync(
        IFilteringAttribute attr, HandlerInvocation invocation,
        CancellationToken ct)
    {
        if (attr is not RequireLevelAttribute levelAttr)
            return true;

        var sender = invocation.Context switch
        {
            CommandContext cmd => cmd.Sender,
            RegexMatchContext regex => regex.Sender,
            _ => null
        };
        if (sender == null) return true;

        var level = await svc.GetUserLevelAsync(sender, ct);
        return level >= levelAttr.Level;
    }
}
```

Usage on handlers:

```csharp
[OnCommand("gline")]
[RequireLevel(700)]
public async Task HandleGline(CommandContext ctx, CancellationToken ct) { ... }
```

The `MarvPlugin` dispatch loop:

1. During handler discovery, scan each method for `IFilteringAttribute`
   instances and cache them alongside the handler registration.
2. Before invoking a handler, iterate its cached filter attributes.
3. For each, resolve the `Evaluator` type from the DI container
   (or via `IPluginActivator`) and call `FilterAsync`.
4. If any evaluator returns `false`, skip the handler.

### Type-Safe Variant with Generics

The base interface requires a cast from `IFilteringAttribute` to the
concrete type. A generic version eliminates this:

```csharp
// Non-generic for framework dispatch
public interface IFilterEvaluator
{
    ValueTask<bool> FilterAsync(IFilteringAttribute attr,
        HandlerInvocation invocation, CancellationToken ct);
}

// Generic for plugin authors — provides type-safe bridge
public abstract class FilterEvaluator<TAttribute> : IFilterEvaluator
    where TAttribute : Attribute, IFilteringAttribute
{
    public ValueTask<bool> FilterAsync(IFilteringAttribute attr,
        HandlerInvocation invocation, CancellationToken ct)
        => FilterAsync((TAttribute)attr, invocation, ct);

    protected abstract ValueTask<bool> FilterAsync(TAttribute attr,
        HandlerInvocation invocation, CancellationToken ct);
}

// Plugin author implements the generic version
public class RequireLevelFilter(IUserLevelService svc)
    : FilterEvaluator<RequireLevelAttribute>
{
    protected override async ValueTask<bool> FilterAsync(
        RequireLevelAttribute attr, HandlerInvocation invocation,
        CancellationToken ct)
    {
        // No cast needed — attr is already RequireLevelAttribute
        var sender = (invocation.Context as CommandContext)?.Sender;
        if (sender == null) return true;
        return await svc.GetUserLevelAsync(sender, ct) >= attr.Level;
    }
}
```

### Strengths

1. **No base class required.** Plugin authors define attributes and
   register evaluator services. No `ExplorationPlugin : MarvPlugin`
   needed. This directly addresses the boilerplate concern from
   Approach 2.

2. **Open/closed.** New filter types are added by writing an attribute
   + evaluator pair. No existing code changes. No base class grows
   a new `if` branch.

3. **Composable.** Multiple `IFilteringAttribute`s on one method are
   naturally AND'd. Each evaluator is independent and testable.

4. **DI-native.** Evaluators inject whatever services they need. Same
   pattern plugin authors already know from handler groups.

5. **Discoverable.** The attribute on the method tells you what's
   required; the `Evaluator` type tells you where the logic lives.

### Weaknesses

#### 1. OR Conditions Are Awkward

Our most common pattern is "admin OR staff channel." Multiple
attributes are naturally AND'd. To express OR, you'd need either:

- **Grouping:** `[RequireAdmin(Group = "access")]`,
  `[StaffChannelBypass(Group = "access")]` — same-group attributes
  are OR'd. Adds framework complexity (group tracking, evaluation
  semantics).
- **Composite attribute:** A single `[RequireAdminOrStaffChannel]`
  with one evaluator. Defeats composability.
- **Evaluator handles disjunction internally:** The evaluator checks
  multiple conditions. But then the attribute declaration doesn't
  fully describe the policy.

This is the pattern's most significant limitation for our needs.

#### 2. Event Type Breadth

The evaluator receives a `HandlerInvocation` with the context as
`object`. Handlers fire on `CommandContext`, `RegexMatchContext`,
typed events (`UserJoinedEvent`), `IrcMessage` (raw), or nothing
(intervals). Evaluators must handle the types they care about and
pass through the rest. This is workable but requires each evaluator
to include a `switch`/`is` check on the context type.

#### 3. Denial Responses

The evaluator returns `bool` but can't send a reply. Options:

- Return a richer type: `FilterResult { Allowed, DenialMessage? }`
- Give the evaluator access to the bot/context for sending
- Add `GetDenialMessage()` to `IFilteringAttribute`

Each adds framework surface area. The simplest is probably a
`FilterResult` struct with an optional message, and have `MarvPlugin`
send it via the appropriate reply mechanism.

#### 4. Evaluator Lifecycle

When are evaluators resolved? Options:

- **Per-invocation:** Correct scoping but allocates on every handler
  call. For an IRC bot, the volume is low enough that this is fine.
- **Per-plugin (cached):** Resolve once during `OnLoadAsync`,
  store in a dictionary keyed by evaluator type. More efficient
  but assumes singleton-like evaluators.
- **DI-managed lifetime:** Register evaluators as services, let the
  DI container control lifetime. Most idiomatic but requires
  evaluators to be registered in `ConfigureServices`.

Recommendation: resolve per-plugin (cache after first resolution).
Evaluators that inject services will naturally share the service's
lifetime.

#### 5. Two Artifacts Per Filter

Every filter requires both an attribute class and an evaluator class.
For our ~5 filter types, that's 10 classes plus service
registrations. More boilerplate than `FilterHandlerAsync` (1 base
class + 5 attribute classes = 6 files), though each individual file
is smaller and more focused.

#### 6. Framework Surface Area

The framework needs:
- `IFilteringAttribute` interface (~5 lines)
- `IFilterEvaluator` interface (~5 lines)
- `FilterEvaluator<T>` abstract base class (~15 lines)
- `FilterResult` struct (~10 lines)
- Resolution and caching logic in `MarvPlugin` (~30 lines)
- Changes to `InvokeHandlerSafe` (~10 lines)

**Total: ~75-100 lines.** More than `FilterHandlerAsync` (~48 lines)
but still modest.

### Compared to FilterHandlerAsync

| Concern | FilterHandler | Self-evaluating attribute |
|---|---|---|
| Base class required | Yes | No |
| OR conditions | Natural (code) | Needs grouping mechanism |
| New filter type | Add branch to override | Add attribute + evaluator |
| Framework surface | ~48 lines | ~80-100 lines |
| Plugin author artifacts | 1 base class + N attributes | 2N classes (attr + evaluator) |
| Denial messages | Direct (has context) | Needs `FilterResult` or similar |
| Event type coverage | Natural (cast in one place) | Each evaluator handles types |
| Testability | Test the base class | Test each evaluator independently |

### Verdict

**Strong alternative to `FilterHandlerAsync`.** Eliminates the base
class requirement at the cost of the OR-condition problem and slightly
more framework surface. Best suited for projects with simple AND-only
authorization. Projects needing disjunctive policies (like our
"admin OR staff channel") would find it awkward.

### Possible Synthesis

Ship `FilterHandlerAsync` *and* the self-evaluating attribute
infrastructure, where the **default** `FilterHandlerAsync`
implementation checks for `IFilteringAttribute` and evaluates them.
This avoids the hybrid problem from Approach 3 because the attribute
system is implemented *via* the filter method, not as a parallel
mechanism:

```csharp
// In MarvPlugin — the default implementation
protected virtual async ValueTask<bool> FilterHandlerAsync(
    HandlerInvocation invocation, CancellationToken ct)
{
    foreach (var attr in invocation.Attributes.OfType<IFilteringAttribute>())
    {
        var evaluator = ResolveEvaluator(attr.Evaluator);
        if (!await evaluator.FilterAsync(attr, invocation, ct))
            return false;
    }
    return true;
}
```

- Simple projects use self-evaluating attributes and never touch
  `FilterHandlerAsync`.
- Complex projects (needing OR logic, context mutation, etc.)
  override `FilterHandlerAsync` and handle it themselves — they
  can still read `IFilteringAttribute` if they want, or ignore
  the system entirely.
- The two mechanisms compose naturally because one is built on top
  of the other.

---

## Approach 6: `[RequireAuth]` with Dynamic Policy Object

### The Question

Can we avoid the string permission problem from Approach 1 by passing
a dynamic or structured object to `[RequireAuth]` instead?

### C# Attribute Parameter Constraints

C# attributes can only carry values of these types in their
constructor or property initializers:

- Primitive types (`bool`, `int`, `string`, etc.)
- `System.Type`
- Enum types
- One-dimensional arrays of the above

You **cannot** pass:
- Class instances (`new AdminPolicy()`)
- Anonymous types (`new { Level = 100 }`)
- Interfaces
- Records or structs (unless they're enum-valued)
- `dynamic` or `object` with non-primitive values

This is a CLR limitation, not a C# language choice — attribute
arguments must be compile-time constants embeddable in metadata.

### What Can Be Done Within the Constraints

#### Option A: Named Properties on the Attribute

```csharp
[RequireAuth(Admin = true)]
[RequireAuth(MinLevel = 100)]
[RequireAuth(Admin = true, StaffChannelBypass = true)]
```

Implementation:

```csharp
public sealed class RequireAuthAttribute : Attribute
{
    public bool Admin { get; init; }
    public int MinLevel { get; init; }
    public bool StaffChannelBypass { get; init; }
    public string? Permission { get; init; }
}
```

**Problem:** Every project's authorization concepts must be
properties on this one attribute. We need `Admin`, `MinLevel`,
`StaffChannelBypass`. An RBAC project needs `Role`. An OAuth
project needs `Scope`. The attribute becomes a union of all
possible authorization models — it's `RequireAuth` trying to
be everything to everyone.

Adding a new property to support a new project means changing the
framework. This is the opposite of extensible.

#### Option B: `Type` as Policy Reference

```csharp
[RequireAuth(typeof(AdminPolicy))]
[RequireAuth(typeof(LevelPolicy), Level = 100)]
```

Implementation:

```csharp
public sealed class RequireAuthAttribute(Type policyType) : Attribute
{
    public Type PolicyType { get; } = policyType;

    // Optional parameters for the policy — but what shape are they?
    public int Level { get; init; }
    public string? Role { get; init; }
}
```

**Problem:** This is essentially the self-evaluating attribute
pattern (Approach 5) with a worse API. The policy type reference
is the same as `IFilteringAttribute.Evaluator`. But the optional
parameters (`Level`, `Role`) are back to the union problem — the
attribute must pre-declare every property any policy might need.

If we instead make the parameters generic:

```csharp
[RequireAuth(typeof(LevelPolicy), Args = new object[] { 100 })]
```

This compiles (arrays of primitives are allowed) but is stringly-
typed and fragile. The policy has to parse `Args[0]` as an int and
hope the caller got it right.

#### Option C: Enum-Based Policy Selector

```csharp
public enum AuthPolicy { Admin, Level100, Level700, StaffBypass }

[RequireAuth(AuthPolicy.Admin)]
[RequireAuth(AuthPolicy.Level100)]
```

**Problem:** Every authorization check must be a member of the enum.
New levels or policies require changing the enum — again, framework
changes for project-specific needs. And the enum can't carry
parameters (you can't express "level ≥ N" for arbitrary N).

#### Option D: Policy Interface + `Type` (No Attribute Parameters)

```csharp
public interface IAuthPolicy
{
    ValueTask<bool> EvaluateAsync(HandlerInvocation invocation,
        CancellationToken ct);
}

[RequireAuth(typeof(AdminOrStaffChannelPolicy))]
public async Task HandleBan(...) { ... }
```

Where `AdminOrStaffChannelPolicy : IAuthPolicy` is resolved from DI
and contains all the logic.

**This is exactly Approach 5** (self-evaluating attributes), just
with the attribute named `[RequireAuth]` instead of a custom name.
The only difference is cosmetic — the attribute doesn't carry
parameters because the policy class handles everything internally.

This actually *loses* information compared to Approach 5, because
`[RequireLevel(700)]` tells you the threshold at the call site,
while `[RequireAuth(typeof(Level700Policy))]` either bakes the
threshold into the class name or hides it inside the implementation.

### Verdict

**The dynamic object idea doesn't work within C# attribute
constraints.** Every attempt to make `[RequireAuth]` carry
structured authorization requirements either:

1. Embeds all possible project models into one attribute (Option A)
   — violates extensibility.
2. Uses `Type` as an indirection to a policy class (Options B, D)
   — reinvents the self-evaluating attribute pattern with a less
   expressive API.
3. Uses enums (Option C) — too rigid for parameterized checks.
4. Uses `object[]` (Option B variant) — loses type safety.

The fundamental issue is that C# attributes are static metadata.
Authorization policies are runtime logic. Bridging these requires
either a `Type` reference to runtime code (which is what Approach 5
already does, better) or moving the logic out of attributes entirely
(which is what Approach 2 does).

---

## Recommendation

**Implement `FilterHandlerAsync` (Approach 2) with built-in
`IFilteringAttribute` support (from Approach 5).**

This is the synthesis described at the end of Approach 5: the default
`FilterHandlerAsync` implementation scans for `IFilteringAttribute`
and evaluates them via their associated evaluator classes. Projects
with simple AND-only filters never touch `FilterHandlerAsync` — they
just use self-evaluating attributes. Projects with complex needs
(OR conditions, context mutation, custom denial messages) override
`FilterHandlerAsync` and handle it themselves.

Reasoning:

1. **Marv shouldn't prescribe an authorization model.** The framework
   doesn't know whether its users will implement RBAC, integer
   levels, capability flags, or hostmask-based class checks.
   `[RequireAuth("permission")]` forces projects to map their model
   into a string-based permission check — a leaky abstraction.
   Using `Type` as a dynamic policy object doesn't help (see
   Approach 6) — it's just Approach 5 with a less expressive API.

2. **Two tiers of complexity.** Simple projects use self-evaluating
   attributes (no base class, just attribute + evaluator pairs).
   Complex projects override `FilterHandlerAsync` for full control.
   Both work through the same mechanism — no parallel systems to
   learn.

3. **The boilerplate concern is addressed at both tiers.** The
   attribute tier requires no base class at all. The override tier
   requires a base class but only for projects that genuinely need
   OR-logic or other complex filters — and those projects will have
   enough handlers to justify the cost.

4. **It's more than authorization.** The same mechanism naturally
   handles channel filtering (`if (ctx.IsDirect) return;`), which
   is the other ubiquitous guard in our project. Two problems solved
   by one mechanism.

5. **It's additive.** Adding `FilterHandlerAsync` doesn't break any
   existing plugins. It's a virtual method with a default
   implementation that evaluates `IFilteringAttribute`s. The
   framework surface change is modest (~80-100 lines including the
   self-evaluating attribute infrastructure).

6. **No `[RequireAuth]` needed.** The self-evaluating attribute
   pattern makes `[RequireAuth]` unnecessary. Each project defines
   attributes that carry their own semantics (`[RequireLevel(100)]`,
   `[RequireRole("moderator")]`, `[RequireScope("channels:manage")]`)
   — the framework doesn't need a universal `[RequireAuth]` that
   tries to express all models.

### Minimal Framework Changes Required

1. Add `HandlerInvocation` struct to `Marv.Core.Plugin` (~20 lines)
2. Add `HandlerType` enum to `Marv.Core.Plugin` (~5 lines)
3. Add `IFilteringAttribute` interface to `Marv.Core.Plugin` (~5 lines)
4. Add `IFilterEvaluator` interface to `Marv.Core.Plugin` (~5 lines)
5. Add `FilterEvaluator<T>` abstract base class (~15 lines)
6. Add `FilterHandlerAsync` virtual method to `MarvPlugin` with
   default `IFilteringAttribute` evaluation (~20 lines)
7. Modify `InvokeHandlerSafe` to call `FilterHandlerAsync` (~5 lines)
8. Add evaluator resolution and caching in `MarvPlugin` (~20 lines)
9. Pre-cache attributes in handler discovery (~10 lines)

**Total: ~105 lines of framework code.** No new dependencies. No
breaking changes. Two interfaces, one abstract base class, one
struct, one enum, and one virtual method.

---

## Open Questions for Implementation

1. **Should handler groups participate in filtering?** If a handler
   group class also overrides `FilterHandlerAsync`, whose filter
   runs? The plugin's or the group's? Recommendation: only the
   plugin's filter runs, since handler groups are owned by the
   plugin and shouldn't have independent authorization.

2. **Should `FilterHandlerAsync` receive the handler registration
   metadata** (e.g. the matched command name, the regex pattern)?
   This is available from the registration but not currently part
   of `HandlerInvocation`. It could be useful for logging but adds
   to the struct size.

3. **Error handling:** If `FilterHandlerAsync` throws, should the
   handler still run? Recommendation: treat it like a handler
   exception — log and skip the handler. This is the safe default.
