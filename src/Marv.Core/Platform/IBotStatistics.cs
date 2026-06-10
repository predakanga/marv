namespace Marv.Core.Platform;

/// <summary>
/// Read-only view of operational statistics for the current connection.
/// All counters reset when the bot reconnects. All properties are
/// thread-safe and may be read from any thread at any time — this is
/// important for OpenTelemetry observable instrument callbacks which
/// run on arbitrary threads.
/// </summary>
public interface IBotStatistics
{
    /// <summary>When the current connection was established (UTC).</summary>
    DateTimeOffset ConnectedAt { get; }

    /// <summary>Time elapsed since the connection was established.</summary>
    TimeSpan Uptime { get; }

    /// <summary>Total bytes received from the server.</summary>
    long BytesReceived { get; }

    /// <summary>Total bytes sent to the server.</summary>
    long BytesSent { get; }

    /// <summary>Total IRC lines received from the server.</summary>
    long LinesReceived { get; }

    /// <summary>Total IRC lines sent to the server.</summary>
    long LinesSent { get; }

    /// <summary>Total handler invocations (commands, events, regex matches, etc.).</summary>
    long HandlersInvoked { get; }
}
