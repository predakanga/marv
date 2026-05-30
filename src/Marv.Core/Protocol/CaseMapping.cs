namespace Marv.Core.Protocol;

/// <summary>
/// IRC case mapping rules as defined by the CASEMAPPING ISUPPORT token.
/// Determines how nicknames and channel names are compared.
/// </summary>
public enum CaseMappingType
{
    /// <summary>
    /// RFC 1459 case mapping: A-Z maps to a-z, and additionally
    /// [ \ ] map to { | } respectively.
    /// </summary>
    Rfc1459,

    /// <summary>
    /// Strict RFC 1459 case mapping: same as RFC 1459 but also
    /// ^ maps to ~.
    /// </summary>
    StrictRfc1459,

    /// <summary>
    /// ASCII case mapping: only A-Z maps to a-z. No special character mappings.
    /// </summary>
    Ascii
}

/// <summary>
/// Provides case-folding and comparison utilities for IRC nicknames and channel names,
/// respecting the server's advertised CASEMAPPING.
/// </summary>
public static class CaseMapping
{
    /// <summary>
    /// Converts a character to its lowercase equivalent under the specified case mapping.
    /// </summary>
    public static char ToLower(char c, CaseMappingType mapping)
    {
        if (c >= 'A' && c <= 'Z')
            return (char)(c + 32);

        return mapping switch
        {
            CaseMappingType.Rfc1459 => c switch
            {
                '[' => '{',
                '\\' => '|',
                ']' => '}',
                _ => c
            },
            CaseMappingType.StrictRfc1459 => c switch
            {
                '[' => '{',
                '\\' => '|',
                ']' => '}',
                '^' => '~',
                _ => c
            },
            _ => c
        };
    }

    /// <summary>
    /// Folds a string to lowercase under the specified case mapping.
    /// </summary>
    public static string Fold(string value, CaseMappingType mapping)
    {
        var chars = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
            chars[i] = ToLower(value[i], mapping);
        return new string(chars);
    }

    /// <summary>
    /// Compares two strings for equality under the specified case mapping.
    /// </summary>
    public static bool AreEqual(string a, string b, CaseMappingType mapping)
    {
        if (a.Length != b.Length)
            return false;

        for (var i = 0; i < a.Length; i++)
        {
            if (ToLower(a[i], mapping) != ToLower(b[i], mapping))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Creates an <see cref="IEqualityComparer{T}"/> for strings using the specified case mapping.
    /// </summary>
    public static IEqualityComparer<string> GetComparer(CaseMappingType mapping) =>
        new IrcCaseComparer(mapping);

    private sealed class IrcCaseComparer(CaseMappingType mapping) : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return AreEqual(x, y, mapping);
        }

        public int GetHashCode(string obj)
        {
            var hash = new HashCode();
            foreach (var c in obj)
                hash.Add(ToLower(c, mapping));
            return hash.ToHashCode();
        }
    }
}
