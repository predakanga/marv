using Xunit;
using Marv.Core.Irc;
using Marv.Core.Protocol;

namespace Marv.Core.Tests.Irc;

/// <summary>
/// Tests for <see cref="ServerInfo"/> ISUPPORT token processing.
/// </summary>
public class ServerInfoTests
{
    [Fact]
    public void SetToken_Network()
    {
        var info = new ServerInfo();
        info.SetToken("NETWORK", "Libera.Chat");
        Assert.Equal("Libera.Chat", info.NetworkName);
    }

    [Fact]
    public void SetToken_CaseMapping()
    {
        var info = new ServerInfo();
        info.SetToken("CASEMAPPING", "ascii");
        Assert.Equal(CaseMappingType.Ascii, info.CaseMapping);
    }

    [Fact]
    public void SetToken_CaseMapping_StrictRfc1459()
    {
        var info = new ServerInfo();
        info.SetToken("CASEMAPPING", "strict-rfc1459");
        Assert.Equal(CaseMappingType.StrictRfc1459, info.CaseMapping);
    }

    [Fact]
    public void SetToken_ChanModes()
    {
        var info = new ServerInfo();
        info.SetToken("CHANMODES", "beI,k,l,imnpst");
        Assert.Contains('b', info.ChannelModes.TypeA);
        Assert.Contains('k', info.ChannelModes.TypeB);
        Assert.Contains('l', info.ChannelModes.TypeC);
        Assert.Contains('i', info.ChannelModes.TypeD);
    }

    [Fact]
    public void SetToken_Prefix()
    {
        var info = new ServerInfo();
        info.SetToken("PREFIX", "(qaohv)~&@%+");
        Assert.Equal('@', info.Prefix.GetPrefix('o'));
        Assert.Equal('o', info.Prefix.GetMode('@'));
        Assert.Equal('~', info.Prefix.GetPrefix('q'));
    }

    [Fact]
    public void SetToken_NickLen()
    {
        var info = new ServerInfo();
        info.SetToken("NICKLEN", "30");
        Assert.Equal(30, info.MaxNickLength);
    }

    [Fact]
    public void SetToken_ChanTypes()
    {
        var info = new ServerInfo();
        info.SetToken("CHANTYPES", "#&!");
        Assert.Contains('#', info.ChannelTypes);
        Assert.Contains('&', info.ChannelTypes);
        Assert.Contains('!', info.ChannelTypes);
    }

    [Fact]
    public void Supports_ReturnsTrueForSetTokens()
    {
        var info = new ServerInfo();
        info.SetToken("WHOX", null);
        Assert.True(info.Supports("WHOX"));
        Assert.False(info.Supports("MISSING"));
    }

    [Fact]
    public void Motd_NullBeforeAnyMotdReceived()
    {
        var info = new ServerInfo();
        Assert.Null(info.Motd);
    }

    [Fact]
    public void Motd_CollectsLines()
    {
        var info = new ServerInfo();
        info.BeginMotd();
        info.AppendMotdLine("- Welcome to TestNet!");
        info.AppendMotdLine("- Please read the rules.");
        Assert.NotNull(info.Motd);
        Assert.Equal(2, info.Motd.Count);
        Assert.Equal("- Welcome to TestNet!", info.Motd[0]);
        Assert.Equal("- Please read the rules.", info.Motd[1]);
    }

    [Fact]
    public void Motd_EmptyWhenNoLinesReceived()
    {
        var info = new ServerInfo();
        info.BeginMotd();
        Assert.NotNull(info.Motd);
        Assert.Empty(info.Motd);
    }

    [Fact]
    public void Reset_ClearsMotd()
    {
        var info = new ServerInfo();
        info.BeginMotd();
        info.AppendMotdLine("- test");
        info.Reset();
        Assert.Null(info.Motd);
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var info = new ServerInfo();
        info.SetToken("NETWORK", "TestNet");
        info.SetToken("CASEMAPPING", "ascii");
        info.Reset();
        Assert.Null(info.NetworkName);
        Assert.Equal(CaseMappingType.Rfc1459, info.CaseMapping);
    }
}
