using Xunit;
using Marv.Core.Irc;
using Marv.Core.Protocol;

namespace Marv.Core.Tests.Irc;

/// <summary>
/// Tests for <see cref="IrcChannel"/> member management and state tracking.
/// </summary>
public class IrcChannelTests
{
    private static IEqualityComparer<string> Comparer =>
        CaseMapping.GetComparer(CaseMappingType.Rfc1459);

    [Fact]
    public void AddMember_TracksUser()
    {
        var channel = new IrcChannel("#test", Comparer);
        var user = new IrcUser("nick", Comparer);
        channel.AddMember(user);
        Assert.True(channel.HasMember("nick"));
        Assert.Contains(channel.Members, u => u.Nick == "nick");
    }

    [Fact]
    public void RemoveMember_RemovesUser()
    {
        var channel = new IrcChannel("#test", Comparer);
        var user = new IrcUser("nick", Comparer);
        channel.AddMember(user);
        channel.RemoveMember("nick");
        Assert.False(channel.HasMember("nick"));
    }

    [Fact]
    public void HasMember_CaseInsensitive()
    {
        var channel = new IrcChannel("#test", Comparer);
        var user = new IrcUser("Nick", Comparer);
        channel.AddMember(user);
        Assert.True(channel.HasMember("nick"));
        Assert.True(channel.HasMember("NICK"));
    }

    [Fact]
    public void AddPrefix_TracksOpAndVoice()
    {
        var channel = new IrcChannel("#test", Comparer);
        var user = new IrcUser("nick", Comparer);
        channel.AddMember(user);
        channel.AddPrefix("nick", '@');
        Assert.True(channel.IsOp("nick"));
        Assert.False(channel.IsVoiced("nick"));

        channel.AddPrefix("nick", '+');
        Assert.True(channel.IsVoiced("nick"));
    }

    [Fact]
    public void RemovePrefix_RemovesPrefix()
    {
        var channel = new IrcChannel("#test", Comparer);
        var user = new IrcUser("nick", Comparer);
        channel.AddMember(user, ['@']);
        Assert.True(channel.IsOp("nick"));

        channel.RemovePrefix("nick", '@');
        Assert.False(channel.IsOp("nick"));
    }

    [Fact]
    public void RenameMember_PreservesState()
    {
        var channel = new IrcChannel("#test", Comparer);
        var user = new IrcUser("oldnick", Comparer);
        channel.AddMember(user, ['@']);
        channel.RenameMember("oldnick", "newnick", user);

        Assert.False(channel.HasMember("oldnick"));
        Assert.True(channel.HasMember("newnick"));
        Assert.True(channel.IsOp("newnick"));
    }

    [Fact]
    public void SetMode_TracksChannelModes()
    {
        var channel = new IrcChannel("#test", Comparer);
        channel.SetMode('i', null);
        channel.SetMode('k', "secret");

        Assert.True(channel.Modes.ContainsKey('i'));
        Assert.Equal("secret", channel.Modes['k']);
    }

    [Fact]
    public void UnsetMode_RemovesMode()
    {
        var channel = new IrcChannel("#test", Comparer);
        channel.SetMode('i', null);
        channel.UnsetMode('i');
        Assert.False(channel.Modes.ContainsKey('i'));
    }

    [Fact]
    public void Topic_CanBeSetAndRead()
    {
        var channel = new IrcChannel("#test", Comparer);
        channel.Topic = "Hello World";
        channel.TopicSetBy = "admin";
        Assert.Equal("Hello World", channel.Topic);
        Assert.Equal("admin", channel.TopicSetBy);
    }
}
