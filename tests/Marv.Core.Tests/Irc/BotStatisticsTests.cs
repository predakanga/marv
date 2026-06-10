using Marv.Core.Irc;
using Xunit;

namespace Marv.Core.Tests.Irc;

/// <summary>
/// Tests for <see cref="BotStatistics"/> counter increments, resets,
/// and thread-safety guarantees.
/// </summary>
public class BotStatisticsTests
{
    [Fact]
    public void Reset_SetsConnectedAtAndZerosAllCounters()
    {
        var stats = new BotStatistics();
        stats.AddBytesReceived(100);
        stats.AddBytesSent(200);
        stats.IncrementLinesReceived();
        stats.IncrementLinesSent();
        stats.IncrementHandlersInvoked();

        var now = DateTimeOffset.UtcNow;
        stats.Reset(now);

        Assert.Equal(now, stats.ConnectedAt);
        Assert.Equal(0, stats.BytesReceived);
        Assert.Equal(0, stats.BytesSent);
        Assert.Equal(0, stats.LinesReceived);
        Assert.Equal(0, stats.LinesSent);
        Assert.Equal(0, stats.HandlersInvoked);
    }

    [Fact]
    public void ByteCounters_AccumulateCorrectly()
    {
        var stats = new BotStatistics();
        stats.Reset(DateTimeOffset.UtcNow);

        stats.AddBytesReceived(512);
        stats.AddBytesReceived(256);
        stats.AddBytesSent(128);

        Assert.Equal(768, stats.BytesReceived);
        Assert.Equal(128, stats.BytesSent);
    }

    [Fact]
    public void LineCounters_IncrementCorrectly()
    {
        var stats = new BotStatistics();
        stats.Reset(DateTimeOffset.UtcNow);

        stats.IncrementLinesReceived();
        stats.IncrementLinesReceived();
        stats.IncrementLinesReceived();
        stats.IncrementLinesSent();

        Assert.Equal(3, stats.LinesReceived);
        Assert.Equal(1, stats.LinesSent);
    }

    [Fact]
    public void HandlersInvoked_IncrementsCorrectly()
    {
        var stats = new BotStatistics();
        stats.Reset(DateTimeOffset.UtcNow);

        stats.IncrementHandlersInvoked();
        stats.IncrementHandlersInvoked();

        Assert.Equal(2, stats.HandlersInvoked);
    }

    [Fact]
    public void Uptime_ReflectsTimeSinceConnectedAt()
    {
        var stats = new BotStatistics();
        var pastTime = DateTimeOffset.UtcNow.AddSeconds(-5);
        stats.Reset(pastTime);

        Assert.True(stats.Uptime >= TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task Counters_AreThreadSafe()
    {
        var stats = new BotStatistics();
        stats.Reset(DateTimeOffset.UtcNow);

        const int iterations = 10_000;
        var tasks = new[]
        {
            Task.Run(() => { for (var i = 0; i < iterations; i++) stats.IncrementLinesReceived(); }),
            Task.Run(() => { for (var i = 0; i < iterations; i++) stats.IncrementLinesSent(); }),
            Task.Run(() => { for (var i = 0; i < iterations; i++) stats.IncrementHandlersInvoked(); }),
            Task.Run(() => { for (var i = 0; i < iterations; i++) stats.AddBytesReceived(1); }),
            Task.Run(() => { for (var i = 0; i < iterations; i++) stats.AddBytesSent(1); }),
        };

        await Task.WhenAll(tasks);

        Assert.Equal(iterations, stats.LinesReceived);
        Assert.Equal(iterations, stats.LinesSent);
        Assert.Equal(iterations, stats.HandlersInvoked);
        Assert.Equal(iterations, stats.BytesReceived);
        Assert.Equal(iterations, stats.BytesSent);
    }
}
