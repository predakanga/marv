using Marv.Core.Platform;
using NSubstitute;

namespace Marv.Testing;

/// <summary>
/// Factory for creating mock <see cref="IBot"/> instances with sensible defaults.
/// </summary>
public static class MockBot
{
    /// <summary>
    /// Creates a mock <see cref="IBot"/> with a <see cref="IBot.Self"/> user
    /// whose nick is "Marv" and a command prefix of "!".
    /// All send methods return completed tasks.
    /// </summary>
    public static IBot Create(string nick = "Marv", string commandPrefix = "!")
    {
        var bot = Substitute.For<IBot>();
        var self = MockUser.Create(nick);
        bot.Self.Returns(self);
        bot.CommandPrefix.Returns(commandPrefix);
        bot.Channels.Returns(new Dictionary<string, IChannel>());
        bot.Users.Returns(new Dictionary<string, IUser>());

        var serverInfo = Substitute.For<IServerInfo>();
        bot.ServerInfo.Returns(serverInfo);

        var caps = Substitute.For<ICapabilityManager>();
        caps.NegotiatedCapabilities.Returns(new HashSet<string>());
        caps.AvailableCapabilities.Returns(new Dictionary<string, string?>());
        bot.Capabilities.Returns(caps);

        return bot;
    }
}
