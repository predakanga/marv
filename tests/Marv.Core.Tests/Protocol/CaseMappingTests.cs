using Xunit;
using Marv.Core.Protocol;

namespace Marv.Core.Tests.Protocol;

/// <summary>
/// Tests for IRC case mapping as defined by CASEMAPPING ISUPPORT.
/// Covers RFC 1459, strict-RFC 1459, and ASCII case folding.
/// See: https://modern.ircdocs.horse/#casemapping-parameter
/// </summary>
public class CaseMappingTests
{
    [Theory]
    [InlineData("Nick", "nick", CaseMappingType.Ascii)]
    [InlineData("NICK", "nick", CaseMappingType.Ascii)]
    [InlineData("#Channel", "#channel", CaseMappingType.Ascii)]
    public void Fold_AsciiMapping(string input, string expected, CaseMappingType mapping)
    {
        Assert.Equal(expected, CaseMapping.Fold(input, mapping));
    }

    [Theory]
    [InlineData("[Nick]", "{nick}", CaseMappingType.Rfc1459)]
    [InlineData("Nick\\Test", "nick|test", CaseMappingType.Rfc1459)]
    [InlineData("Nick^Test", "nick^test", CaseMappingType.Rfc1459)] // ^ is NOT mapped in RFC 1459
    public void Fold_Rfc1459Mapping(string input, string expected, CaseMappingType mapping)
    {
        Assert.Equal(expected, CaseMapping.Fold(input, mapping));
    }

    [Theory]
    [InlineData("[Nick]", "{nick}", CaseMappingType.StrictRfc1459)]
    [InlineData("Nick^Test", "nick~test", CaseMappingType.StrictRfc1459)] // ^ IS mapped in strict
    public void Fold_StrictRfc1459Mapping(string input, string expected, CaseMappingType mapping)
    {
        Assert.Equal(expected, CaseMapping.Fold(input, mapping));
    }

    [Theory]
    [InlineData("Nick", "nick", CaseMappingType.Rfc1459, true)]
    [InlineData("[test]", "{test}", CaseMappingType.Rfc1459, true)]
    [InlineData("[test]", "{test}", CaseMappingType.Ascii, false)] // Not equal under ASCII
    [InlineData("Nick", "NICK", CaseMappingType.Ascii, true)]
    [InlineData("abc", "abd", CaseMappingType.Ascii, false)]
    public void AreEqual_VariousMappings(string a, string b, CaseMappingType mapping, bool expected)
    {
        Assert.Equal(expected, CaseMapping.AreEqual(a, b, mapping));
    }

    [Fact]
    public void GetComparer_WorksInDictionary()
    {
        var comparer = CaseMapping.GetComparer(CaseMappingType.Rfc1459);
        var dict = new Dictionary<string, int>(comparer)
        {
            ["[Nick]"] = 1
        };

        Assert.True(dict.ContainsKey("{nick}"));
        Assert.Equal(1, dict["{nick}"]);
    }

    [Fact]
    public void AreEqual_DifferentLengths_ReturnsFalse()
    {
        Assert.False(CaseMapping.AreEqual("abc", "ab", CaseMappingType.Ascii));
    }
}
