namespace Marv.Core.Events;

/// <summary>Raised when a BATCH group begins.</summary>
public sealed class BatchStartEvent : MarvEvent
{
    /// <summary>The unique batch identifier.</summary>
    public required string BatchRefTag { get; init; }

    /// <summary>The batch type (e.g. "netsplit", "netjoin").</summary>
    public required string Type { get; init; }

    /// <summary>Additional batch parameters.</summary>
    public required IReadOnlyList<string> Parameters { get; init; }
}

/// <summary>Raised when a BATCH group ends.</summary>
public sealed class BatchEndEvent : MarvEvent
{
    /// <summary>The unique batch identifier.</summary>
    public required string BatchRefTag { get; init; }
}
