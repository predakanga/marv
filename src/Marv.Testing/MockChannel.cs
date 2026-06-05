using Marv.Core.Platform;
using NSubstitute;

namespace Marv.Testing;

/// <summary>
/// Factory for creating mock <see cref="IChannel"/> instances with sensible defaults.
/// </summary>
public static class MockChannel
{
    /// <summary>
    /// Creates a mock <see cref="IChannel"/> with the specified name.
    /// Defaults: no topic, empty member list, empty mode list.
    /// </summary>
    public static IChannel Create(string name = "#test")
    {
        var channel = Substitute.For<IChannel>();
        channel.Name.Returns(name);
        channel.Members.Returns(Array.Empty<IUser>());
        channel.Modes.Returns(new Dictionary<char, string?>());
        return channel;
    }
}
