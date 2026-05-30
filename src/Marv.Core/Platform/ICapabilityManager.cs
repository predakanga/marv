namespace Marv.Core.Platform;

/// <summary>
/// Manages IRCv3 capability negotiation state. Plugins can query which capabilities
/// are available and which have been successfully negotiated.
/// </summary>
public interface ICapabilityManager
{
    /// <summary>Returns whether the specified capability has been successfully negotiated.</summary>
    bool IsNegotiated(string capability);

    /// <summary>Returns whether the server advertises the specified capability.</summary>
    bool IsAvailable(string capability);

    /// <summary>The set of capabilities that have been successfully negotiated.</summary>
    IReadOnlySet<string> NegotiatedCapabilities { get; }

    /// <summary>
    /// All capabilities the server advertises, with their values (null if no value).
    /// </summary>
    IReadOnlyDictionary<string, string?> AvailableCapabilities { get; }

    /// <summary>Raised when capabilities change at runtime (from cap-notify).</summary>
    event EventHandler? CapabilitiesChanged;
}
