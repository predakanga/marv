using Marv.Core.Platform;
using NSubstitute;

namespace Marv.Testing;

/// <summary>
/// Factory for creating mock <see cref="IUser"/> instances with sensible defaults.
/// </summary>
public static class MockUser
{
    /// <summary>
    /// Creates a mock <see cref="IUser"/> with the specified nick and optional account.
    /// Defaults: User = "user", Host = "host.example.com", Hostmask = "nick!user@host.example.com".
    /// </summary>
    public static IUser Create(string nick = "testuser", string? account = null)
    {
        var user = Substitute.For<IUser>();
        user.Nick.Returns(nick);
        user.User.Returns("user");
        user.Host.Returns("host.example.com");
        user.Account.Returns(account);
        user.Hostmask.Returns($"{nick}!user@host.example.com");
        user.Channels.Returns(Array.Empty<IChannel>());
        return user;
    }
}
