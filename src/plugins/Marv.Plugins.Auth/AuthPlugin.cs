using Marv.Core.Platform;
using Marv.Core.Plugin;
using Microsoft.Extensions.DependencyInjection;

namespace Marv.Plugins.Auth;

/// <summary>
/// Plugin that provides <see cref="IAuthorizationService"/> for account-based authorization.
/// Other plugins can consume this service to check user permissions.
/// Demonstrates service registration and inter-plugin service provision.
/// </summary>
[ProvidesService(typeof(IAuthorizationService))]
public class AuthPlugin : MarvPlugin
{
    /// <summary>
    /// Creates a new <see cref="AuthPlugin"/>.
    /// </summary>
    public AuthPlugin(IBot bot, IPluginActivator activator)
        : base(bot, activator) { }

    /// <summary>
    /// Registers the <see cref="IAuthorizationService"/> implementation in the DI container.
    /// </summary>
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationService, AccountBasedAuthService>();
    }
}
