namespace Marv.Core.Events;

/// <summary>Raised when IRC registration is complete (001 received).</summary>
public sealed class ConnectedEvent : MarvEvent;

/// <summary>Raised when the IRC connection is lost or closed.</summary>
public sealed class DisconnectedEvent : MarvEvent;

/// <summary>Raised when capabilities change at runtime (from cap-notify).</summary>
public sealed class CapabilitiesChangedEvent : MarvEvent;
