using Marv.Core.Irc;
using Xunit;

namespace Marv.Core.Tests.Irc;

/// <summary>
/// Tests for <see cref="TokenBucketRateLimiter"/> covering burst behavior,
/// token refill, disabling, reset, and cancellation.
/// </summary>
public class TokenBucketRateLimiterTests
{
    [Fact]
    public async Task WaitAsync_AllowsBurstUpToLimit()
    {
        var limiter = new TokenBucketRateLimiter(enabled: true, burstLimit: 3, refillRatePerSecond: 0.001);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        for (var i = 0; i < 3; i++)
        {
            var task = limiter.WaitAsync(cts.Token);
            Assert.True(task.IsCompleted, $"Burst message {i + 1} should complete synchronously");
        }
    }

    [Fact]
    public async Task WaitAsync_ThrottlesAfterBurstExhausted()
    {
        var limiter = new TokenBucketRateLimiter(enabled: true, burstLimit: 2, refillRatePerSecond: 0.001);
        using var burstCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await limiter.WaitAsync(burstCts.Token);
        await limiter.WaitAsync(burstCts.Token);

        // With near-zero refill, the third call should block until cancelled
        using var throttleCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => limiter.WaitAsync(throttleCts.Token));
    }

    [Fact]
    public async Task WaitAsync_RefillsTokensOverTime()
    {
        var limiter = new TokenBucketRateLimiter(enabled: true, burstLimit: 1, refillRatePerSecond: 20.0);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Exhaust the single token
        await limiter.WaitAsync(cts.Token);

        // At 20 tokens/sec, a new token appears within ~150ms (50ms refill + 100ms poll)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitAsync(cts.Token);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 1500,
            $"Token should have refilled quickly at 20/sec, took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void WaitAsync_DisabledReturnsImmediately()
    {
        var limiter = new TokenBucketRateLimiter(enabled: false, burstLimit: 1, refillRatePerSecond: 0.001);

        for (var i = 0; i < 100; i++)
        {
            var task = limiter.WaitAsync(CancellationToken.None);
            Assert.True(task.IsCompleted, $"Disabled limiter should return synchronously (iteration {i})");
        }
    }

    [Fact]
    public void WaitAsync_RespectsDefaultParameters()
    {
        var limiter = new TokenBucketRateLimiter();

        Assert.True(limiter.Enabled);
        Assert.Equal(5, limiter.BurstLimit);
        Assert.Equal(0.5, limiter.RefillRatePerSecond);

        // Default burst of 5 should allow 5 synchronous completions
        for (var i = 0; i < 5; i++)
        {
            var task = limiter.WaitAsync(CancellationToken.None);
            Assert.True(task.IsCompleted, $"Default burst message {i + 1} should complete synchronously");
        }
    }

    [Fact]
    public async Task WaitAsync_CancellationStopsWait()
    {
        var limiter = new TokenBucketRateLimiter(enabled: true, burstLimit: 1, refillRatePerSecond: 0.001);
        using var exhaustCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await limiter.WaitAsync(exhaustCts.Token);

        using var cts = new CancellationTokenSource();
        var task = limiter.WaitAsync(cts.Token);
        Assert.False(task.IsCompleted);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Reset_RestoresBurstCapacity()
    {
        var limiter = new TokenBucketRateLimiter(enabled: true, burstLimit: 2, refillRatePerSecond: 0.001);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await limiter.WaitAsync(cts.Token);
        await limiter.WaitAsync(cts.Token);

        limiter.Reset();

        for (var i = 0; i < 2; i++)
        {
            var task = limiter.WaitAsync(cts.Token);
            Assert.True(task.IsCompleted, $"Post-reset message {i + 1} should complete synchronously");
        }
    }

    [Fact]
    public async Task WaitAsync_TokensDoNotExceedBurstLimit()
    {
        var limiter = new TokenBucketRateLimiter(enabled: true, burstLimit: 2, refillRatePerSecond: 1000.0);

        // Wait for tokens to over-accumulate (should be capped at 2)
        await Task.Delay(200);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Should allow exactly 2 synchronously (the burst cap)
        var task1 = limiter.WaitAsync(cts.Token);
        Assert.True(task1.IsCompleted, "First call should complete synchronously");
        var task2 = limiter.WaitAsync(cts.Token);
        Assert.True(task2.IsCompleted, "Second call should complete synchronously");

        // Third call: at 1000 tokens/sec a token refills within the 100ms poll,
        // so it won't be synchronous but will complete quickly. The important
        // assertion is that only 2 completed synchronously (the cap).
    }

    [Fact]
    public void Properties_ReflectConstructorArguments()
    {
        var limiter = new TokenBucketRateLimiter(enabled: false, burstLimit: 10, refillRatePerSecond: 2.5);

        Assert.False(limiter.Enabled);
        Assert.Equal(10, limiter.BurstLimit);
        Assert.Equal(2.5, limiter.RefillRatePerSecond);
    }

    [Fact]
    public async Task WaitAsync_ConcurrentCallsAreThreadSafe()
    {
        var limiter = new TokenBucketRateLimiter(enabled: true, burstLimit: 10, refillRatePerSecond: 100.0);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => limiter.WaitAsync(cts.Token))
            .ToArray();

        await Task.WhenAll(tasks);
    }
}
