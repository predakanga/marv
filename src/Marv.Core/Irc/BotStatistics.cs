using Marv.Core.Platform;

namespace Marv.Core.Irc;

/// <summary>
/// Mutable implementation of <see cref="IBotStatistics"/> backed by
/// <see cref="Interlocked"/> fields for thread-safe counter increments.
/// Owned by <see cref="IrcBot"/>; reset on each new connection.
/// </summary>
internal sealed class BotStatistics : IBotStatistics
{
    private long _bytesReceived;
    private long _bytesSent;
    private long _linesReceived;
    private long _linesSent;
    private long _handlersInvoked;

    /// <inheritdoc />
    public DateTimeOffset ConnectedAt { get; private set; }

    /// <inheritdoc />
    public TimeSpan Uptime => DateTimeOffset.UtcNow - ConnectedAt;

    /// <inheritdoc />
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    /// <inheritdoc />
    public long BytesSent => Interlocked.Read(ref _bytesSent);

    /// <inheritdoc />
    public long LinesReceived => Interlocked.Read(ref _linesReceived);

    /// <inheritdoc />
    public long LinesSent => Interlocked.Read(ref _linesSent);

    /// <inheritdoc />
    public long HandlersInvoked => Interlocked.Read(ref _handlersInvoked);

    /// <summary>Records bytes received from the server.</summary>
    internal void AddBytesReceived(long count) => Interlocked.Add(ref _bytesReceived, count);

    /// <summary>Records bytes sent to the server.</summary>
    internal void AddBytesSent(long count) => Interlocked.Add(ref _bytesSent, count);

    /// <summary>Records an inbound IRC line.</summary>
    internal void IncrementLinesReceived() => Interlocked.Increment(ref _linesReceived);

    /// <summary>Records an outbound IRC line.</summary>
    internal void IncrementLinesSent() => Interlocked.Increment(ref _linesSent);

    /// <summary>Records a handler invocation.</summary>
    internal void IncrementHandlersInvoked() => Interlocked.Increment(ref _handlersInvoked);

    /// <summary>Resets all counters for a new connection.</summary>
    internal void Reset(DateTimeOffset connectedAt)
    {
        ConnectedAt = connectedAt;
        Interlocked.Exchange(ref _bytesReceived, 0);
        Interlocked.Exchange(ref _bytesSent, 0);
        Interlocked.Exchange(ref _linesReceived, 0);
        Interlocked.Exchange(ref _linesSent, 0);
        Interlocked.Exchange(ref _handlersInvoked, 0);
    }
}
