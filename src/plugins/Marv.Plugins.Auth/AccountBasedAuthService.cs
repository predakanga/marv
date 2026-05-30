using Marv.Core.Platform;
using Microsoft.Extensions.Options;

namespace Marv.Plugins.Auth;

/// <summary>
/// Authorization service that grants admin permissions to users whose services account
/// is in the configured admin accounts list.
/// </summary>
public class AccountBasedAuthService(IOptions<AuthPluginConfig> config) : IAuthorizationService
{
    /// <inheritdoc />
    public Task<bool> IsAuthorizedAsync(IUser user, string permission, CancellationToken ct)
    {
        var isAdmin = user.Account is not null &&
            config.Value.AdminAccounts.Contains(user.Account, StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(isAdmin);
    }
}
