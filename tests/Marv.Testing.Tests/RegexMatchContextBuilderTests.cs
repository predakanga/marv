using Xunit;

namespace Marv.Testing.Tests;

public class RegexMatchContextBuilderTests
{
    [Fact]
    public void Build_WithDefaults_CreatesDirectMessage()
    {
        var ctx = RegexMatchContextBuilder.Create(@"hello (\w+)", "hello world").Build();

        Assert.True(ctx.Match.Success);
        Assert.Equal("world", ctx.Match.Groups[1].Value);
        Assert.Null(ctx.Channel);
        Assert.True(ctx.IsDirect);
        Assert.Equal("testuser", ctx.Sender.Nick);
    }

    [Fact]
    public void Build_InChannel_SetsChannel()
    {
        var ctx = RegexMatchContextBuilder.Create(@"\d+", "42")
            .InChannel("#math")
            .Build();

        Assert.NotNull(ctx.Channel);
        Assert.Equal("#math", ctx.Channel!.Name);
        Assert.False(ctx.IsDirect);
    }

    [Fact]
    public void Build_From_SetsSender()
    {
        var ctx = RegexMatchContextBuilder.Create(@".*", "test")
            .From("alice", "alice_acct")
            .Build();

        Assert.Equal("alice", ctx.Sender.Nick);
        Assert.Equal("alice_acct", ctx.Sender.Account);
    }

    [Fact]
    public void Build_PatternDoesNotMatch_Throws()
    {
        var builder = RegexMatchContextBuilder.Create(@"^xyz$", "abc");
        Assert.Throws<ArgumentException>(() => builder.Build());
    }

    [Fact]
    public void Build_RawMessage_ContainsInput()
    {
        var ctx = RegexMatchContextBuilder.Create(@"hello", "hello world")
            .InChannel("#test")
            .From("alice")
            .Build();

        Assert.Equal("PRIVMSG", ctx.RawMessage.Command);
        Assert.Equal("#test", ctx.RawMessage.Parameters[0]);
        Assert.Equal("hello world", ctx.RawMessage.Parameters[1]);
    }
}
