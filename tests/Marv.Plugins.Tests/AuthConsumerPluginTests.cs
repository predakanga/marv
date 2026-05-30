using Xunit;
using NSubstitute;
using Marv.Core.Events;
using Marv.Core.Platform;
using Marv.Core.Plugin;
using Marv.Core.Protocol;
using Marv.Plugins.Auth;
using Marv.Plugins.AuthConsumer;

namespace Marv.Plugins.Tests;

/// <summary>
/// Tests for the <see cref="AuthConsumerPlugin"/> demonstrating
/// optional inter-plugin service consumption.
/// </summary>
public class AuthConsumerPluginTests
{
    private static readonly IrcMessage DummyMessage = new("PRIVMSG", ["#test", "!secret"]);

    private static (AuthConsumerPlugin Plugin, IBot Bot) CreatePlugin(IAuthorizationService? auth = null)
    {
        var bot = Substitute.For<IBot>();
        var activator = Substitute.For<IPluginActivator>();
        return (new AuthConsumerPlugin(bot, activator, auth), bot);
    }

    [Fact]
    public async Task Secret_WithoutAuth_AlwaysSucceeds()
    {
        var (plugin, bot) = CreatePlugin(auth: null);
        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("anyone");

        var evt = new MessageEvent
        {
            Channel = channel,
            Sender = user,
            Text = "!secret",
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        await plugin.HandleEventAsync(evt, CancellationToken.None);
        await bot.Received(1).SendMessageAsync("#test", "The secret is: 42", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Secret_WithAuth_Authorized_Succeeds()
    {
        var auth = Substitute.For<IAuthorizationService>();
        auth.IsAuthorizedAsync(Arg.Any<IUser>(), "secret.view", Arg.Any<CancellationToken>())
            .Returns(true);

        var (plugin, bot) = CreatePlugin(auth);
        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("admin");

        var evt = new MessageEvent
        {
            Channel = channel,
            Sender = user,
            Text = "!secret",
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        await plugin.HandleEventAsync(evt, CancellationToken.None);
        await bot.Received(1).SendMessageAsync("#test", "The secret is: 42", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Secret_WithAuth_Unauthorized_Denied()
    {
        var auth = Substitute.For<IAuthorizationService>();
        auth.IsAuthorizedAsync(Arg.Any<IUser>(), "secret.view", Arg.Any<CancellationToken>())
            .Returns(false);

        var (plugin, bot) = CreatePlugin(auth);
        var channel = Substitute.For<IChannel>();
        channel.Name.Returns("#test");
        var user = Substitute.For<IUser>();
        user.Nick.Returns("nobody");

        var evt = new MessageEvent
        {
            Channel = channel,
            Sender = user,
            Text = "!secret",
            Timestamp = DateTimeOffset.UtcNow,
            RawMessage = DummyMessage
        };

        await plugin.HandleEventAsync(evt, CancellationToken.None);
        await bot.Received(1).SendMessageAsync("#test", "Permission denied.", Arg.Any<CancellationToken>());
    }
}
