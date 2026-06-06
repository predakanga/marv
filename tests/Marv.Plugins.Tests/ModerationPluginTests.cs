using Xunit;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Marv.Core.Events;
using Marv.Core.Platform;
using Marv.Core.Plugin;
using Marv.Plugins.Auth;
using Marv.Plugins.Moderation;
using Marv.Testing;

namespace Marv.Plugins.Tests;

/// <summary>
/// Tests for the Moderation example plugin. Demonstrates testing patterns
/// using <see cref="PluginTestHarness{T}"/>, <see cref="CommandContextBuilder"/>,
/// and <see cref="EventBuilder{T}"/> from the Marv.Testing package.
/// </summary>
public class ModerationPluginTests
{
    private static PluginTestHarness<ModerationPlugin> CreateHarness(
        IAuthorizationService? auth = null)
    {
        return PluginTestHarness<ModerationPlugin>.Create(
            configureServices: services =>
            {
                services.AddSingleton(Options.Create(new ModerationConfig
                {
                    AuditChannel = "#audit",
                    BanDurationMinutes = 30
                }));
                if (auth is not null)
                    services.AddSingleton(auth);
            });
    }

    [Fact]
    public async Task Kick_SendsKickCommand()
    {
        var auth = Substitute.For<IAuthorizationService>();
        auth.IsAuthorizedAsync(Arg.Any<IUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var harness = CreateHarness(auth);
        await harness.LoadAsync();
        await harness.ConnectedAsync();

        var ctx = CommandContextBuilder.Create("kick", "baduser being rude")
            .InChannel("#test")
            .From("moderator", "mod_account")
            .WithBot(harness.Bot)
            .Build();
        await harness.HandleEventAsync(MessageEventFrom(ctx));

        await harness.Bot.Received().KickAsync("#test", "baduser", "being rude", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ban_SetsBanMode_AndTracksExpiry()
    {
        var auth = Substitute.For<IAuthorizationService>();
        auth.IsAuthorizedAsync(Arg.Any<IUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var harness = CreateHarness(auth);
        await harness.LoadAsync();
        await harness.ConnectedAsync();

        var ctx = CommandContextBuilder.Create("ban", "spammer")
            .InChannel("#test")
            .From("moderator")
            .WithBot(harness.Bot)
            .Build();
        await harness.HandleEventAsync(MessageEventFrom(ctx));

        await harness.Bot.Received().SetModeAsync("#test", "+b", "spammer!*@*", Arg.Any<CancellationToken>());
        Assert.True(harness.Plugin.ActiveBans.ContainsKey("spammer"));
    }

    [Fact]
    public async Task Ban_Alias_B_Works()
    {
        var auth = Substitute.For<IAuthorizationService>();
        auth.IsAuthorizedAsync(Arg.Any<IUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var harness = CreateHarness(auth);
        await harness.LoadAsync();
        await harness.ConnectedAsync();

        var ctx = CommandContextBuilder.Create("b", "spammer")
            .InChannel("#test")
            .From("moderator")
            .WithBot(harness.Bot)
            .Build();
        await harness.HandleEventAsync(MessageEventFrom(ctx));

        await harness.Bot.Received().SetModeAsync("#test", "+b", "spammer!*@*", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequireAuth_DeniesUnauthorizedUser()
    {
        var auth = Substitute.For<IAuthorizationService>();
        auth.IsAuthorizedAsync(Arg.Any<IUser>(), "mod.kick", Arg.Any<CancellationToken>())
            .Returns(false);
        var harness = CreateHarness(auth);
        await harness.LoadAsync();
        await harness.ConnectedAsync();

        var ctx = CommandContextBuilder.Create("kick", "someone")
            .InChannel("#test")
            .From("nobody")
            .WithBot(harness.Bot)
            .Build();
        await harness.HandleEventAsync(MessageEventFrom(ctx));

        // KickAsync should NOT have been called
        await harness.Bot.DidNotReceive().KickAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        // Should have sent denial reply
        await harness.Bot.Received().SendMessageAsync("#test", "Permission denied.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleJoin_SendsNotice_ForOtherUsers()
    {
        var harness = CreateHarness();
        await harness.LoadAsync();
        await harness.ConnectedAsync();

        var evt = EventBuilder<UserJoinedEvent>.Create(raw => new UserJoinedEvent
        {
            Channel = MockChannel.Create("#test"),
            User = MockUser.Create("alice"),
            RawMessage = raw
        }).Build();
        await harness.HandleEventAsync(evt);

        await harness.Bot.Received().SendNoticeAsync("#test",
            "Welcome, alice. This channel is moderated.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleJoin_DoesNotGreetSelf()
    {
        var harness = CreateHarness();
        await harness.LoadAsync();
        await harness.ConnectedAsync();

        var evt = EventBuilder<UserJoinedEvent>.Create(raw => new UserJoinedEvent
        {
            Channel = MockChannel.Create("#test"),
            User = MockUser.Create("Marv"),
            RawMessage = raw
        }).Build();
        await harness.HandleEventAsync(evt);

        await harness.Bot.DidNotReceive().SendNoticeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleKicked_SendsAuditMessage()
    {
        var harness = CreateHarness();
        await harness.LoadAsync();
        await harness.ConnectedAsync();

        var evt = EventBuilder<UserKickedEvent>.Create(raw => new UserKickedEvent
        {
            Channel = MockChannel.Create("#test"),
            Kicker = MockUser.Create("op"),
            Kicked = MockUser.Create("victim"),
            Reason = "spam",
            RawMessage = raw
        }).Build();
        await harness.HandleEventAsync(evt);

        await harness.Bot.Received().SendMessageAsync("#audit",
            "[Mod] victim was kicked from #test by op: spam", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnected_RebuildsBanDictionary()
    {
        var auth = Substitute.For<IAuthorizationService>();
        auth.IsAuthorizedAsync(Arg.Any<IUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var harness = CreateHarness(auth);
        await harness.LoadAsync();
        await harness.ConnectedAsync();

        // Ban a user
        var ctx = CommandContextBuilder.Create("ban", "spammer")
            .InChannel("#test").From("mod").WithBot(harness.Bot).Build();
        await harness.HandleEventAsync(MessageEventFrom(ctx));
        Assert.True(harness.Plugin.ActiveBans.ContainsKey("spammer"));

        // Reconnect — bans should be cleared (connection-scoped state)
        await harness.Plugin.OnDisconnectedAsync();
        await harness.ConnectedAsync();
        Assert.Empty(harness.Plugin.ActiveBans);
    }

    /// <summary>
    /// Creates a MessageEvent from a CommandContext, needed because
    /// HandleEventAsync dispatches on event type.
    /// </summary>
    private static MessageEvent MessageEventFrom(CommandContext ctx)
    {
        return EventBuilder<MessageEvent>.Create(raw => new MessageEvent
        {
            Channel = ctx.Channel,
            Sender = ctx.Sender,
            Text = ctx.RawMessage.Parameters[^1],
            RawMessage = raw
        }).Build();
    }
}
