using Xunit;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;
using Marv.Core.Events;
using Marv.Core.Platform;
using Marv.Plugins.Auth;
using Marv.Plugins.AuthConsumer;
using Marv.Testing;

namespace Marv.Plugins.Tests;

/// <summary>
/// Tests for the <see cref="AuthConsumerPlugin"/> demonstrating
/// optional inter-plugin service consumption.
/// </summary>
public class AuthConsumerPluginTests
{
    [Fact]
    public async Task Secret_WithoutAuth_AlwaysSucceeds()
    {
        var harness = PluginTestHarness<AuthConsumerPlugin>.Create();
        var evt = EventBuilder<MessageEvent>.Create(raw => new MessageEvent
        {
            Channel = MockChannel.Create("#test"),
            Sender = MockUser.Create("anyone"),
            Text = "!secret",
            RawMessage = raw
        }).Build();

        await harness.HandleEventAsync(evt);
        await harness.Bot.Received(1).SendMessageAsync("#test", "The secret is: 42", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Secret_WithAuth_Authorized_Succeeds()
    {
        var auth = Substitute.For<IAuthorizationService>();
        auth.IsAuthorizedAsync(Arg.Any<IUser>(), "secret.view", Arg.Any<CancellationToken>())
            .Returns(true);

        var harness = PluginTestHarness<AuthConsumerPlugin>.Create(services =>
        {
            services.AddSingleton(auth);
        });
        var evt = EventBuilder<MessageEvent>.Create(raw => new MessageEvent
        {
            Channel = MockChannel.Create("#test"),
            Sender = MockUser.Create("admin"),
            Text = "!secret",
            RawMessage = raw
        }).Build();

        await harness.HandleEventAsync(evt);
        await harness.Bot.Received(1).SendMessageAsync("#test", "The secret is: 42", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Secret_WithAuth_Unauthorized_Denied()
    {
        var auth = Substitute.For<IAuthorizationService>();
        auth.IsAuthorizedAsync(Arg.Any<IUser>(), "secret.view", Arg.Any<CancellationToken>())
            .Returns(false);

        var harness = PluginTestHarness<AuthConsumerPlugin>.Create(services =>
        {
            services.AddSingleton(auth);
        });
        var evt = EventBuilder<MessageEvent>.Create(raw => new MessageEvent
        {
            Channel = MockChannel.Create("#test"),
            Sender = MockUser.Create("nobody"),
            Text = "!secret",
            RawMessage = raw
        }).Build();

        await harness.HandleEventAsync(evt);
        await harness.Bot.Received(1).SendMessageAsync("#test", "Permission denied.", Arg.Any<CancellationToken>());
    }
}
