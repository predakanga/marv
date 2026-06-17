using Xunit;
using Marv.Core.Protocol;

namespace Marv.Core.Tests.Protocol;

/// <summary>
/// Tests for <see cref="IrcSerializer"/>, validated against the ircdocs/parser-tests
/// msg-join test vectors from https://github.com/ircdocs/parser-tests.
/// </summary>
public class IrcSerializerTests
{
    [Fact]
    public void Serialize_SimpleVerbAndParams()
    {
        var msg = new IrcMessage("foo", ["bar", "baz", "asdf"]);
        var result = IrcSerializer.Serialize(msg);
        Assert.Equal("FOO bar baz asdf", result);
    }

    [Fact]
    public void Serialize_SourceAndNoParams()
    {
        var msg = new IrcMessage(null, new MessageSource("src", null, null), "AWAY", []);
        var result = IrcSerializer.Serialize(msg);
        Assert.Equal(":src AWAY", result);
    }

    [Fact]
    public void Serialize_SourceAndEmptyTrailing()
    {
        var msg = new IrcMessage(null, new MessageSource("src", null, null), "AWAY", [""]);
        var result = IrcSerializer.Serialize(msg);
        Assert.Equal(":src AWAY :", result);
    }

    [Fact]
    public void Serialize_TrailingWithSpaces()
    {
        var msg = new IrcMessage("foo", ["bar", "baz", "asdf quux"]);
        var result = IrcSerializer.Serialize(msg);
        Assert.Equal("FOO bar baz :asdf quux", result);
    }

    [Fact]
    public void Serialize_TrailingWithColon()
    {
        var msg = new IrcMessage("foo", ["bar", "baz", ":asdf"]);
        var result = IrcSerializer.Serialize(msg);
        Assert.Equal("FOO bar baz ::asdf", result);
    }

    [Fact]
    public void Serialize_TagsWithEscaping()
    {
        var tags = new Dictionary<string, string?>
        {
            ["a"] = "b\\and\nk",
            ["d"] = "gh;764"
        };
        var msg = new IrcMessage(tags, "foo", ["par1", "par2"]);
        var result = IrcSerializer.Serialize(msg);
        // Tags may be in any order
        Assert.Contains("a=b\\\\and\\nk", result);
        Assert.Contains("d=gh\\:764", result);
        Assert.Contains("FOO par1 par2", result);
    }

    [Fact]
    public void Serialize_EmptyTrailing()
    {
        var msg = new IrcMessage("foo", ["bar", "baz", ""]);
        var result = IrcSerializer.Serialize(msg);
        Assert.Equal("FOO bar baz :", result);
    }

    [Fact]
    public void Serialize_CommandOnly()
    {
        var msg = new IrcMessage("COMMAND", []);
        var result = IrcSerializer.Serialize(msg);
        Assert.Equal("COMMAND", result);
    }

    [Fact]
    public void Serialize_RoundTrip()
    {
        var input = "@tag1=value1;tag2 :nick!user@host PRIVMSG #channel :hello world";
        var parsed = IrcParser.Parse(input)!;
        var serialized = IrcSerializer.Serialize(parsed);
        var reparsed = IrcParser.Parse(serialized)!;

        Assert.Equal(parsed.Command, reparsed.Command);
        Assert.Equal(parsed.Parameters, reparsed.Parameters);
        Assert.Equal(parsed.Tags["tag1"], reparsed.Tags["tag1"]);
        Assert.Equal(parsed.Source!.Nick, reparsed.Source!.Nick);
    }
}
