using Marv.Core;
using Marv.Core.Irc;
using Xunit;

namespace Marv.Core.Tests.Irc;

/// <summary>
/// Tests for the <see cref="IrcUtils.BatchChannels"/> batching logic
/// used by <see cref="Marv.Core.Irc.IrcBot.JoinMultipleAsync"/>.
/// </summary>
public class IrcBotBulkJoinTests
{
    [Fact]
    public void BatchChannels_EmptyList_ReturnsNoBatches()
    {
        var batches = IrcUtils.BatchChannels([], 505).ToList();
        Assert.Empty(batches);
    }

    [Fact]
    public void BatchChannels_SingleChannel_ReturnsSingleBatch()
    {
        var batches = IrcUtils.BatchChannels(["#test"], 505).ToList();

        Assert.Single(batches);
        Assert.Equal(["#test"], batches[0]);
    }

    [Fact]
    public void BatchChannels_MultipleChannels_FitInOneBatch()
    {
        var channels = new[] { "#alpha", "#beta", "#gamma" };
        var batches = IrcUtils.BatchChannels(channels, 505).ToList();

        Assert.Single(batches);
        Assert.Equal(channels, batches[0]);
    }

    [Fact]
    public void BatchChannels_ExceedsMaxLength_SplitsIntoBatches()
    {
        // ~42 chars each; with 505 limit, 505 / 43 ≈ 11 per batch
        var channels = Enumerable.Range(1, 20)
            .Select(i => $"#channel-with-a-long-name-for-test-{i:D3}")
            .ToList();

        var batches = IrcUtils.BatchChannels(channels, 505).ToList();

        Assert.True(batches.Count >= 2, $"Expected at least 2 batches, got {batches.Count}");

        // All channels must be present in order
        var flattened = batches.SelectMany(b => b).ToList();
        Assert.Equal(channels, flattened);
    }

    [Fact]
    public void BatchChannels_NoBatchExceedsMaxPayloadLength()
    {
        var channels = Enumerable.Range(1, 50)
            .Select(i => $"#long-channel-name-padding-{i:D4}")
            .ToList();

        var batches = IrcUtils.BatchChannels(channels, 505).ToList();

        foreach (var batch in batches)
        {
            var commaJoined = string.Join(',', batch);
            Assert.True(commaJoined.Length <= 505,
                $"Batch payload is {commaJoined.Length} bytes, exceeds 505 limit");
        }
    }

    [Fact]
    public void BatchChannels_SmallMaxLength_ForcesManySplits()
    {
        var channels = new[] { "#aaa", "#bbb", "#ccc", "#ddd" };
        // maxPayloadLength = 9 → "#aaa,#bbb" = 9, fits; "#aaa,#bbb,#ccc" = 14, doesn't
        var batches = IrcUtils.BatchChannels(channels, maxPayloadLength: 9).ToList();

        Assert.Equal(2, batches.Count);
        Assert.Equal(["#aaa", "#bbb"], batches[0]);
        Assert.Equal(["#ccc", "#ddd"], batches[1]);
    }

    [Fact]
    public void BatchChannels_SingleLargeChannel_IsItsOwnBatch()
    {
        var longName = "#" + new string('x', 504);
        var channels = new[] { "#small", longName, "#other" };

        var batches = IrcUtils.BatchChannels(channels, 505).ToList();

        Assert.Equal(3, batches.Count);
        Assert.Equal(["#small"], batches[0]);
        Assert.Equal([longName], batches[1]);
        Assert.Equal(["#other"], batches[2]);
    }

    [Fact]
    public void BatchChannels_MaxTargets_SplitsByCount()
    {
        var channels = new[] { "#a", "#b", "#c", "#d", "#e" };

        var batches = IrcUtils.BatchChannels(channels, 505, maxTargets: 2).ToList();

        Assert.Equal(3, batches.Count);
        Assert.Equal(["#a", "#b"], batches[0]);
        Assert.Equal(["#c", "#d"], batches[1]);
        Assert.Equal(["#e"], batches[2]);
    }

    [Fact]
    public void BatchChannels_MaxTargets_PayloadLimitStillApplies()
    {
        var channels = new[] { "#aaa", "#bbb", "#ccc", "#ddd" };
        // Payload limit of 9 forces splits at 2 channels, even though maxTargets allows 10
        var batches = IrcUtils.BatchChannels(channels, maxPayloadLength: 9, maxTargets: 10).ToList();

        Assert.Equal(2, batches.Count);
        Assert.Equal(["#aaa", "#bbb"], batches[0]);
        Assert.Equal(["#ccc", "#ddd"], batches[1]);
    }

    [Fact]
    public void BatchChannels_MaxTargetsNull_NoCountLimit()
    {
        var channels = Enumerable.Range(1, 20).Select(i => $"#{i}").ToList();

        var batches = IrcUtils.BatchChannels(channels, 505, maxTargets: null).ToList();

        Assert.Single(batches);
        Assert.Equal(channels, batches[0]);
    }

    [Fact]
    public void BatchChannels_MaxTargetsZero_NoCountLimit()
    {
        var channels = Enumerable.Range(1, 20).Select(i => $"#{i}").ToList();

        var batches = IrcUtils.BatchChannels(channels, 505, maxTargets: 0).ToList();

        Assert.Single(batches);
        Assert.Equal(channels, batches[0]);
    }

    [Theory]
    [InlineData("JOIN:4,PRIVMSG:1,KICK:4", "JOIN", 4)]
    [InlineData("PRIVMSG:1,JOIN:8,KICK:4", "JOIN", 8)]
    [InlineData("JOIN:,PRIVMSG:1", "JOIN", null)]
    [InlineData("PRIVMSG:1,KICK:4", "JOIN", null)]
    [InlineData("", "JOIN", null)]
    [InlineData(null, "JOIN", null)]
    public void ParseTargMax_ExtractsCorrectLimit(string? targmax, string command, int? expected)
    {
        Assert.Equal(expected, IrcUtils.ParseTargMax(targmax, command));
    }

    [Theory]
    [InlineData("4", 4)]
    [InlineData("1", 1)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("abc", null)]
    public void ParseMaxTargets_ExtractsCorrectLimit(string? maxtargets, int? expected)
    {
        Assert.Equal(expected, IrcUtils.ParseMaxTargets(maxtargets));
    }

    [Fact]
    public void GetMaxTargets_PrefersTargMax()
    {
        var info = new ServerInfo();
        info.SetToken("TARGMAX", "JOIN:4,PRIVMSG:1");
        info.SetToken("MAXTARGETS", "8");

        Assert.Equal(4, IrcUtils.GetMaxTargets(info, "JOIN"));
    }

    [Fact]
    public void GetMaxTargets_FallsBackToMaxTargets()
    {
        var info = new ServerInfo();
        info.SetToken("MAXTARGETS", "6");

        Assert.Equal(6, IrcUtils.GetMaxTargets(info, "JOIN"));
    }

    [Fact]
    public void GetMaxTargets_ReturnsNull_WhenNeitherSet()
    {
        var info = new ServerInfo();

        Assert.Null(IrcUtils.GetMaxTargets(info, "JOIN"));
    }

    [Fact]
    public void GetMaxTargets_FallsBackToMaxTargets_WhenCommandNotInTargMax()
    {
        var info = new ServerInfo();
        info.SetToken("TARGMAX", "PRIVMSG:1,KICK:4");
        info.SetToken("MAXTARGETS", "3");

        Assert.Equal(3, IrcUtils.GetMaxTargets(info, "JOIN"));
    }
}
