using System.Collections.Concurrent;
using Marv.Core.Platform;

namespace Marv.Core.Irc;

/// <summary>
/// Manages IRCv3 capability negotiation state. Tracks which capabilities the server
/// advertises and which have been successfully negotiated.
/// </summary>
public sealed class CapabilityManager : ICapabilityManager
{
    private readonly ConcurrentDictionary<string, string?> _available = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _negotiated = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool IsNegotiated(string capability) => _negotiated.ContainsKey(capability);

    /// <inheritdoc />
    public bool IsAvailable(string capability) => _available.ContainsKey(capability);

    /// <inheritdoc />
    public IReadOnlySet<string> NegotiatedCapabilities =>
        new HashSet<string>(_negotiated.Keys, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string?> AvailableCapabilities => _available;

    /// <inheritdoc />
    public event EventHandler? CapabilitiesChanged;

    /// <summary>
    /// Records capabilities advertised by the server in CAP LS responses.
    /// </summary>
    internal void SetAvailable(string capability, string? value)
    {
        _available[capability] = value;
    }

    /// <summary>
    /// Records a capability as successfully negotiated (from CAP ACK).
    /// </summary>
    internal void SetNegotiated(string capability)
    {
        _negotiated[capability] = 0;
    }

    /// <summary>
    /// Removes a capability (from CAP DEL via cap-notify).
    /// </summary>
    internal void RemoveCapability(string capability)
    {
        _available.TryRemove(capability, out _);
        _negotiated.TryRemove(capability, out _);
        CapabilitiesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds a newly available capability (from CAP NEW via cap-notify).
    /// </summary>
    internal void AddNewCapability(string capability, string? value)
    {
        _available[capability] = value;
        CapabilitiesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Resets all state, typically on disconnection.</summary>
    internal void Reset()
    {
        _available.Clear();
        _negotiated.Clear();
    }
}
