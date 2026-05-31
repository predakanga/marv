namespace Marv.Core.Irc;

/// <summary>
/// Token bucket rate limiter for outbound IRC messages.
/// Allows a configurable burst of messages, then throttles to a steady refill rate.
/// Can be disabled entirely for testing or unrestricted environments.
/// </summary>
internal sealed class TokenBucketRateLimiter
{
    private readonly bool _enabled;
    private readonly int _burstLimit;
    private readonly double _refillRatePerSecond;
    private double _tokens;
    private DateTimeOffset _lastRefill;
    private readonly object _lock = new();

    /// <summary>
    /// Creates a new token bucket rate limiter.
    /// </summary>
    /// <param name="enabled">Whether rate limiting is active. When false, <see cref="WaitAsync"/> returns immediately.</param>
    /// <param name="burstLimit">Maximum tokens (messages) available at once.</param>
    /// <param name="refillRatePerSecond">Tokens added per second (e.g. 0.5 = one token every 2 seconds).</param>
    public TokenBucketRateLimiter(bool enabled = true, int burstLimit = 5, double refillRatePerSecond = 0.5)
    {
        _enabled = enabled;
        _burstLimit = burstLimit;
        _refillRatePerSecond = refillRatePerSecond;
        _tokens = burstLimit;
        _lastRefill = DateTimeOffset.UtcNow;
    }

    /// <summary>Whether rate limiting is active.</summary>
    public bool Enabled => _enabled;

    /// <summary>Maximum burst size.</summary>
    public int BurstLimit => _burstLimit;

    /// <summary>Token refill rate per second.</summary>
    public double RefillRatePerSecond => _refillRatePerSecond;

    /// <summary>
    /// Waits until a token is available, consuming it before returning.
    /// Returns immediately when rate limiting is disabled.
    /// </summary>
    public async Task WaitAsync(CancellationToken ct)
    {
        if (!_enabled) return;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                var elapsed = (now - _lastRefill).TotalSeconds;
                _tokens = Math.Min(_burstLimit, _tokens + elapsed * _refillRatePerSecond);
                _lastRefill = now;

                if (_tokens >= 1.0)
                {
                    _tokens -= 1.0;
                    return;
                }
            }

            await Task.Delay(100, ct);
        }
    }

    /// <summary>
    /// Resets the token bucket to full capacity. Called when a new connection is established.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _tokens = _burstLimit;
            _lastRefill = DateTimeOffset.UtcNow;
        }
    }
}
