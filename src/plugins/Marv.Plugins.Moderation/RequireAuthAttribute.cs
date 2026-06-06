using Marv.Core.Platform;
using Marv.Core.Plugin;
using Marv.Plugins.Auth;

namespace Marv.Plugins.Moderation;

/// <summary>
/// Filter attribute that requires the sender to be authorized for a specific permission.
/// Demonstrates the <see cref="IFilteringAttribute"/> + <see cref="FilterEvaluator{T}"/>
/// pattern from CS-005. Apply to any handler method to enforce authorization declaratively.
/// </summary>
/// <example>
/// <code>
/// [RequireAuth("mod.kick")]
/// [OnCommand("kick", ChannelOnly = true)]
/// private async Task HandleKick(CommandContext ctx, CancellationToken ct) { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireAuthAttribute(string permission) : Attribute, IFilteringAttribute
{
    /// <summary>The permission string to check (e.g. "mod.kick").</summary>
    public string Permission { get; } = permission;

    /// <inheritdoc />
    public Type EvaluatorType => typeof(RequireAuthEvaluator);
}

/// <summary>
/// Evaluator for <see cref="RequireAuthAttribute"/>. Checks the sender's authorization
/// via <see cref="IAuthorizationService"/> and sends a denial reply if unauthorized.
/// </summary>
public class RequireAuthEvaluator : FilterEvaluator<RequireAuthAttribute>
{
    private readonly IAuthorizationService? _auth;

    /// <summary>
    /// Creates a new evaluator. The auth service is optional — if no plugin provides it,
    /// all authorization checks pass (fail-open when auth is not configured).
    /// </summary>
    public RequireAuthEvaluator(IAuthorizationService? auth = null)
    {
        _auth = auth;
    }

    /// <inheritdoc />
    protected override async ValueTask<FilterResult> EvaluateAsync(
        RequireAuthAttribute attribute, HandlerInvocation invocation,
        IBot bot, CancellationToken ct)
    {
        if (_auth is null)
            return FilterResult.Allowed;

        if (invocation.Context is CommandContext cmd)
        {
            if (!await _auth.IsAuthorizedAsync(cmd.Sender, attribute.Permission, ct))
            {
                await cmd.ReplyAsync("Permission denied.", ct);
                return FilterResult.Denied;
            }
        }

        return FilterResult.Allowed;
    }
}
