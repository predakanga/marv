using Xunit;
using Marv.Core.Platform;

namespace Marv.Core.Tests.Platform;

/// <summary>
/// Tests for <see cref="PrefixMapping"/> PREFIX ISUPPORT parsing.
/// </summary>
public class PrefixMappingTests
{
    [Fact]
    public void Parse_StandardPrefix()
    {
        var mapping = PrefixMapping.Parse("(ov)@+");
        Assert.Equal('@', mapping.GetPrefix('o'));
        Assert.Equal('+', mapping.GetPrefix('v'));
        Assert.Equal('o', mapping.GetMode('@'));
        Assert.Equal('v', mapping.GetMode('+'));
    }

    [Fact]
    public void Parse_ExtendedPrefix()
    {
        var mapping = PrefixMapping.Parse("(qaohv)~&@%+");
        Assert.Equal('~', mapping.GetPrefix('q'));
        Assert.Equal('&', mapping.GetPrefix('a'));
        Assert.Equal('%', mapping.GetPrefix('h'));
    }

    [Fact]
    public void IsPrefix_ReturnsCorrectly()
    {
        var mapping = PrefixMapping.Parse("(ov)@+");
        Assert.True(mapping.IsPrefix('@'));
        Assert.True(mapping.IsPrefix('+'));
        Assert.False(mapping.IsPrefix('~'));
    }

    [Fact]
    public void Parse_Empty_ReturnsDefault()
    {
        var mapping = PrefixMapping.Parse("");
        Assert.Equal('@', mapping.GetPrefix('o'));
        Assert.Equal('+', mapping.GetPrefix('v'));
    }

    [Fact]
    public void GetPrefix_UnknownMode_ReturnsNull()
    {
        var mapping = PrefixMapping.Parse("(ov)@+");
        Assert.Null(mapping.GetPrefix('q'));
    }
}
