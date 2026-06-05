using Marv.Core.Platform;
using NSubstitute;
using Xunit;

namespace Marv.Testing.Tests;

public class CommandContextBuilderTests
{
    [Fact]
    public void Build_WithDefaults_CreatesDirectMessage()
    {
        var ctx = CommandContextBuilder.Create("hello").Build();

        Assert.Equal("hello", ctx.Command);
        Assert.Empty(ctx.Args);
        Assert.Equal("", ctx.ArgString);
        Assert.Null(ctx.Channel);
        Assert.True(ctx.IsDirect);
        Assert.Equal("testuser", ctx.Sender.Nick);
        Assert.Equal("Marv", ctx.Bot.Self.Nick);
        Assert.Equal("!", ctx.Bot.CommandPrefix);
    }

    [Fact]
    public void Build_WithArgs_ParsesArgs()
    {
        var ctx = CommandContextBuilder.Create("kick", "alice bad behavior").Build();

        Assert.Equal("kick", ctx.Command);
        Assert.Equal(["alice", "bad", "behavior"], ctx.Args);
        Assert.Equal("alice bad behavior", ctx.ArgString);
    }

    [Fact]
    public void Build_InChannel_SetsChannel()
    {
        var ctx = CommandContextBuilder.Create("hello")
            .InChannel("#general")
            .Build();

        Assert.NotNull(ctx.Channel);
        Assert.Equal("#general", ctx.Channel!.Name);
        Assert.False(ctx.IsDirect);
    }

    [Fact]
    public void Build_AsDirect_ClearsChannel()
    {
        var ctx = CommandContextBuilder.Create("hello")
            .InChannel("#general")
            .AsDirect()
            .Build();

        Assert.Null(ctx.Channel);
        Assert.True(ctx.IsDirect);
    }

    [Fact]
    public void Build_From_SetsSenderNickAndAccount()
    {
        var ctx = CommandContextBuilder.Create("hello")
            .From("alice", "alice_account")
            .Build();

        Assert.Equal("alice", ctx.Sender.Nick);
        Assert.Equal("alice_account", ctx.Sender.Account);
    }

    [Fact]
    public void Build_WithBot_UsesProvidedBot()
    {
        var bot = Substitute.For<IBot>();
        var self = Substitute.For<IUser>();
        self.Nick.Returns("CustomBot");
        bot.Self.Returns(self);
        bot.CommandPrefix.Returns(".");

        var ctx = CommandContextBuilder.Create("hello")
            .WithBot(bot)
            .Build();

        Assert.Same(bot, ctx.Bot);
        Assert.Equal("CustomBot", ctx.Bot.Self.Nick);
    }

    [Fact]
    public void Build_RawMessage_HasCorrectStructure()
    {
        var ctx = CommandContextBuilder.Create("hello", "world")
            .InChannel("#test")
            .From("alice")
            .Build();

        Assert.Equal("PRIVMSG", ctx.RawMessage.Command);
        Assert.Equal("#test", ctx.RawMessage.Parameters[0]);
        Assert.Equal("!hello world", ctx.RawMessage.Parameters[1]);
    }

    [Fact]
    public void Build_DirectMessage_RawMessageTargetsBotNick()
    {
        var ctx = CommandContextBuilder.Create("hello")
            .From("alice")
            .Build();

        Assert.Equal("Marv", ctx.RawMessage.Parameters[0]);
    }
}
