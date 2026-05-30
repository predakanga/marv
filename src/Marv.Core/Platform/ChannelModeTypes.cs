namespace Marv.Core.Platform;

/// <summary>
/// Classifies channel modes into types A through D, as defined by the CHANMODES ISUPPORT token.
/// Mode types determine whether a mode takes a parameter when set/unset.
/// </summary>
public sealed class ChannelModeTypes
{
    /// <summary>Type A: list modes (e.g. ban lists). Always have a parameter.</summary>
    public IReadOnlySet<char> TypeA { get; }

    /// <summary>Type B: modes that always have a parameter (e.g. channel key).</summary>
    public IReadOnlySet<char> TypeB { get; }

    /// <summary>Type C: modes that have a parameter only when set (e.g. channel limit).</summary>
    public IReadOnlySet<char> TypeC { get; }

    /// <summary>Type D: modes that never have a parameter (e.g. no-external-messages).</summary>
    public IReadOnlySet<char> TypeD { get; }

    /// <summary>
    /// Creates a new <see cref="ChannelModeTypes"/> from the four mode categories.
    /// </summary>
    public ChannelModeTypes(
        IReadOnlySet<char> typeA,
        IReadOnlySet<char> typeB,
        IReadOnlySet<char> typeC,
        IReadOnlySet<char> typeD)
    {
        TypeA = typeA;
        TypeB = typeB;
        TypeC = typeC;
        TypeD = typeD;
    }

    /// <summary>
    /// Parses the CHANMODES ISUPPORT value (e.g. "beI,k,l,imnpst").
    /// </summary>
    public static ChannelModeTypes Parse(string chanmodesValue)
    {
        var parts = chanmodesValue.Split(',');
        return new ChannelModeTypes(
            parts.Length > 0 ? new HashSet<char>(parts[0]) : new HashSet<char>(),
            parts.Length > 1 ? new HashSet<char>(parts[1]) : new HashSet<char>(),
            parts.Length > 2 ? new HashSet<char>(parts[2]) : new HashSet<char>(),
            parts.Length > 3 ? new HashSet<char>(parts[3]) : new HashSet<char>()
        );
    }

    /// <summary>Default mode types for servers that don't advertise CHANMODES.</summary>
    public static ChannelModeTypes Default { get; } = Parse("beI,k,l,imnpst");
}
