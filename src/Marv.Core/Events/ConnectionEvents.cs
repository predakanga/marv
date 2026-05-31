namespace Marv.Core.Events;

/// <summary>Raised when IRC registration is complete (001 received).</summary>
public sealed class ConnectedEvent : MarvEvent;

/// <summary>
/// Raised when the bot is fully ready: registration is complete, all configured
/// authentication (NickServ, OPER) has finished or timed out, and channels are
/// about to be joined. Plugins that need to act before channel joins should
/// handle this event. This event fires regardless of whether any authentication
/// is configured.
/// </summary>
public sealed class ReadyEvent : MarvEvent;

/// <summary>Raised when the IRC connection is lost or closed.</summary>
public sealed class DisconnectedEvent : MarvEvent;

/// <summary>Raised when capabilities change at runtime (from cap-notify).</summary>
public sealed class CapabilitiesChangedEvent : MarvEvent;
