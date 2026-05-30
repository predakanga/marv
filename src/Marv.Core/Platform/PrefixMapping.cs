namespace Marv.Core.Platform;

/// <summary>
/// Maps channel membership mode characters to their display prefixes,
/// as defined by the PREFIX ISUPPORT token (e.g. "(ov)@+").
/// </summary>
public sealed class PrefixMapping
{
    private readonly IReadOnlyList<(char Mode, char Prefix)> _entries;
    private readonly Dictionary<char, char> _modeToPrefix;
    private readonly Dictionary<char, char> _prefixToMode;

    /// <summary>
    /// Creates a new <see cref="PrefixMapping"/> from ordered mode/prefix pairs.
    /// Entries are ordered by descending privilege (highest first).
    /// </summary>
    public PrefixMapping(IReadOnlyList<(char Mode, char Prefix)> entries)
    {
        _entries = entries;
        _modeToPrefix = entries.ToDictionary(e => e.Mode, e => e.Prefix);
        _prefixToMode = entries.ToDictionary(e => e.Prefix, e => e.Mode);
    }

    /// <summary>All known prefix characters, ordered by descending privilege.</summary>
    public IEnumerable<char> Prefixes => _entries.Select(e => e.Prefix);

    /// <summary>All known mode characters, ordered by descending privilege.</summary>
    public IEnumerable<char> Modes => _entries.Select(e => e.Mode);

    /// <summary>Gets the display prefix for a mode character (e.g. 'o' → '@').</summary>
    public char? GetPrefix(char mode) => _modeToPrefix.TryGetValue(mode, out var p) ? p : null;

    /// <summary>Gets the mode character for a display prefix (e.g. '@' → 'o').</summary>
    public char? GetMode(char prefix) => _prefixToMode.TryGetValue(prefix, out var m) ? m : null;

    /// <summary>Returns whether the given character is a known prefix.</summary>
    public bool IsPrefix(char c) => _prefixToMode.ContainsKey(c);

    /// <summary>
    /// Parses the PREFIX ISUPPORT value (e.g. "(ov)@+").
    /// </summary>
    public static PrefixMapping Parse(string prefixValue)
    {
        if (string.IsNullOrEmpty(prefixValue) || prefixValue[0] != '(')
            return Default;

        var closeIndex = prefixValue.IndexOf(')');
        if (closeIndex < 0)
            return Default;

        var modes = prefixValue[1..closeIndex];
        var prefixes = prefixValue[(closeIndex + 1)..];

        if (modes.Length != prefixes.Length)
            return Default;

        var entries = new List<(char, char)>(modes.Length);
        for (var i = 0; i < modes.Length; i++)
            entries.Add((modes[i], prefixes[i]));

        return new PrefixMapping(entries);
    }

    /// <summary>Default prefix mapping for servers that don't advertise PREFIX.</summary>
    public static PrefixMapping Default { get; } = Parse("(ov)@+");
}
