using Xunit;
using Microsoft.Extensions.Options;
using Marv.Plugins.Auth;
using Marv.Testing;

namespace Marv.Plugins.Tests;

/// <summary>
/// Tests for the <see cref="AccountBasedAuthService"/> and the inter-plugin
/// service mechanism.
/// </summary>
public class AuthPluginTests
{
    [Fact]
    public async Task IsAuthorized_AdminAccount_ReturnsTrue()
    {
        var config = Options.Create(new AuthPluginConfig { AdminAccounts = ["adminuser"] });
        var service = new AccountBasedAuthService(config);
        var user = MockUser.Create("user", "adminuser");

        Assert.True(await service.IsAuthorizedAsync(user, "any.permission", CancellationToken.None));
    }

    [Fact]
    public async Task IsAuthorized_NonAdminAccount_ReturnsFalse()
    {
        var config = Options.Create(new AuthPluginConfig { AdminAccounts = ["adminuser"] });
        var service = new AccountBasedAuthService(config);
        var user = MockUser.Create("user", "regularuser");

        Assert.False(await service.IsAuthorizedAsync(user, "any.permission", CancellationToken.None));
    }

    [Fact]
    public async Task IsAuthorized_NullAccount_ReturnsFalse()
    {
        var config = Options.Create(new AuthPluginConfig { AdminAccounts = ["adminuser"] });
        var service = new AccountBasedAuthService(config);
        var user = MockUser.Create("user");

        Assert.False(await service.IsAuthorizedAsync(user, "any.permission", CancellationToken.None));
    }

    [Fact]
    public async Task IsAuthorized_CaseInsensitive()
    {
        var config = Options.Create(new AuthPluginConfig { AdminAccounts = ["AdminUser"] });
        var service = new AccountBasedAuthService(config);
        var user = MockUser.Create("user", "adminuser");

        Assert.True(await service.IsAuthorizedAsync(user, "any.permission", CancellationToken.None));
    }
}
