namespace Marv.Core.Events;

/// <summary>
/// Raised for every inbound message before any higher-level event is dispatched.
/// Plugins that need to handle protocol messages not covered by typed events
/// can subscribe to this.
/// </summary>
public sealed class RawMessageEvent : MarvEvent;
