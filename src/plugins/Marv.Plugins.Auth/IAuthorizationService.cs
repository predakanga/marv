using Marv.Core.Platform;

namespace Marv.Plugins.Auth;

/// <summary>
/// Service interface for account-based authorization. Provided by the Auth plugin
/// and consumed by plugins that need permission checking.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Checks whether the specified user is authorized for the given permission.
    /// </summary>
    /// <param name="user">The user to check.</param>
    /// <param name="permission">The permission to check (e.g. "mod.kick").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the user is authorized.</returns>
    Task<bool> IsAuthorizedAsync(IUser user, string permission, CancellationToken ct);
}
