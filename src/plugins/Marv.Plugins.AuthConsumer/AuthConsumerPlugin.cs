using Marv.Core.Platform;
using Marv.Core.Plugin;
using Microsoft.Extensions.Logging;
using Marv.Plugins.Auth;

namespace Marv.Plugins.AuthConsumer;

/// <summary>
/// Plugin that consumes <see cref="IAuthorizationService"/> from the Auth plugin.
/// Demonstrates inter-plugin service consumption with an optional dependency.
/// </summary>
public class AuthConsumerPlugin : MarvPlugin
{
    private readonly IAuthorizationService? _auth;

    /// <summary>
    /// Creates a new <see cref="AuthConsumerPlugin"/>. The auth service is optional —
    /// if no plugin provides <see cref="IAuthorizationService"/>, the plugin loads
    /// normally but commands always succeed.
    /// </summary>
    public AuthConsumerPlugin(
        IBot bot,
        IPluginActivator activator,
        ILoggerFactory loggerFactory,
        IAuthorizationService? auth = null)
        : base(bot, activator, loggerFactory)
    {
        _auth = auth;
    }

    /// <summary>
    /// A protected command that checks authorization before responding.
    /// </summary>
    [OnCommand("secret")]
    private async Task HandleSecret(CommandContext ctx, CancellationToken ct)
    {
        if (_auth is not null &&
            !await _auth.IsAuthorizedAsync(ctx.Sender, "secret.view", ct))
        {
            await ctx.ReplyAsync("Permission denied.", ct);
            return;
        }

        await ctx.ReplyAsync("The secret is: 42", ct);
    }

    /// <summary>
    /// Reports whether the auth service is available.
    /// </summary>
    [OnCommand("authstatus")]
    private async Task HandleAuthStatus(CommandContext ctx, CancellationToken ct)
    {
        var status = _auth is not null ? "available" : "not available";
        await ctx.ReplyAsync($"Auth service is {status}.", ct);
    }
}
