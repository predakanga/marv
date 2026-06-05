using Xunit;

namespace Marv.Testing.Tests;

public class MockHelperTests
{
    [Fact]
    public void MockUser_Create_DefaultNick()
    {
        var user = MockUser.Create();
        Assert.Equal("testuser", user.Nick);
        Assert.Null(user.Account);
        Assert.Equal("user", user.User);
        Assert.Equal("host.example.com", user.Host);
    }

    [Fact]
    public void MockUser_Create_CustomNickAndAccount()
    {
        var user = MockUser.Create("alice", "alice_acct");
        Assert.Equal("alice", user.Nick);
        Assert.Equal("alice_acct", user.Account);
    }

    [Fact]
    public void MockChannel_Create_DefaultName()
    {
        var channel = MockChannel.Create();
        Assert.Equal("#test", channel.Name);
    }

    [Fact]
    public void MockChannel_Create_CustomName()
    {
        var channel = MockChannel.Create("#general");
        Assert.Equal("#general", channel.Name);
    }

    [Fact]
    public void MockBot_Create_Defaults()
    {
        var bot = MockBot.Create();
        Assert.Equal("Marv", bot.Self.Nick);
        Assert.Equal("!", bot.CommandPrefix);
        Assert.NotNull(bot.ServerInfo);
        Assert.NotNull(bot.Capabilities);
        Assert.Empty(bot.Channels);
        Assert.Empty(bot.Users);
    }

    [Fact]
    public void MockBot_Create_CustomNickAndPrefix()
    {
        var bot = MockBot.Create("TestBot", ".");
        Assert.Equal("TestBot", bot.Self.Nick);
        Assert.Equal(".", bot.CommandPrefix);
    }

    [Fact]
    public void DummyIrcMessage_Privmsg_HasExpectedCommand()
    {
        Assert.Equal("PRIVMSG", DummyIrcMessage.Privmsg.Command);
    }

    [Fact]
    public void DummyIrcMessage_PrivmsgFrom_CustomValues()
    {
        var msg = DummyIrcMessage.PrivmsgFrom("alice", "#test", "hello");
        Assert.Equal("PRIVMSG", msg.Command);
        Assert.Equal("#test", msg.Parameters[0]);
        Assert.Equal("hello", msg.Parameters[1]);
        Assert.Equal("alice", msg.Source!.Nick);
    }
}
