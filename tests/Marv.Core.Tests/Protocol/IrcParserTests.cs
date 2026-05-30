using Xunit;
using Marv.Core.Protocol;

namespace Marv.Core.Tests.Protocol;

/// <summary>
/// Tests for <see cref="IrcParser"/>, validated against the ircdocs/parser-tests
/// test vectors from https://github.com/ircdocs/parser-tests.
/// Test vectors are CC0 public domain by Daniel Oaks, with contributions from
/// grawity (WTFPL v2), Mozilla (public domain), and SadieCat.
/// </summary>
public class IrcParserTests
{
    [Fact]
    public void Parse_SimpleVerbAndParams()
    {
        var msg = IrcParser.Parse("foo bar baz asdf")!;
        Assert.Equal("FOO", msg.Command);
        Assert.Equal(["bar", "baz", "asdf"], msg.Parameters);
        Assert.Null(msg.Source);
        Assert.Empty(msg.Tags);
    }

    [Fact]
    public void Parse_SourceAndParams()
    {
        var msg = IrcParser.Parse(":coolguy foo bar baz asdf")!;
        Assert.Equal("FOO", msg.Command);
        Assert.Equal("coolguy", msg.Source!.Nick);
        Assert.Equal(["bar", "baz", "asdf"], msg.Parameters);
    }

    [Fact]
    public void Parse_TrailingParameter()
    {
        var msg = IrcParser.Parse("foo bar baz :asdf quux")!;
        Assert.Equal("FOO", msg.Command);
        Assert.Equal(["bar", "baz", "asdf quux"], msg.Parameters);
    }

    [Fact]
    public void Parse_EmptyTrailingParameter()
    {
        var msg = IrcParser.Parse("foo bar baz :")!;
        Assert.Equal(["bar", "baz", ""], msg.Parameters);
    }

    [Fact]
    public void Parse_TrailingWithColon()
    {
        var msg = IrcParser.Parse("foo bar baz ::asdf")!;
        Assert.Equal(["bar", "baz", ":asdf"], msg.Parameters);
    }

    [Fact]
    public void Parse_SourceAndTrailingWithSpaces()
    {
        var msg = IrcParser.Parse(":coolguy foo bar baz :  asdf quux ")!;
        Assert.Equal("coolguy", msg.Source!.Nick);
        Assert.Equal(["bar", "baz", "  asdf quux "], msg.Parameters);
    }

    [Fact]
    public void Parse_PrivmsgWithTrailingContainingColon()
    {
        var msg = IrcParser.Parse(":coolguy PRIVMSG bar :lol :) ")!;
        Assert.Equal("PRIVMSG", msg.Command);
        Assert.Equal(["bar", "lol :) "], msg.Parameters);
    }

    [Fact]
    public void Parse_SourceAndEmptyTrailing()
    {
        var msg = IrcParser.Parse(":coolguy foo bar baz :")!;
        Assert.Equal(["bar", "baz", ""], msg.Parameters);
    }

    [Fact]
    public void Parse_TrailingOnlySpaces()
    {
        var msg = IrcParser.Parse(":coolguy foo bar baz :  ")!;
        Assert.Equal(["bar", "baz", "  "], msg.Parameters);
    }

    [Fact]
    public void Parse_Tags()
    {
        var msg = IrcParser.Parse("@a=b;c=32;k;rt=ql7 foo")!;
        Assert.Equal("FOO", msg.Command);
        Assert.Equal("b", msg.Tags["a"]);
        Assert.Equal("32", msg.Tags["c"]);
        Assert.Equal("", msg.Tags["k"]);
        Assert.Equal("ql7", msg.Tags["rt"]);
    }

    [Fact]
    public void Parse_TagValueEscaping()
    {
        // @a=b\\and\nk;c=72\s45;d=gh\:764 foo
        var msg = IrcParser.Parse("@a=b\\\\and\\nk;c=72\\s45;d=gh\\:764 foo")!;
        Assert.Equal("b\\and\nk", msg.Tags["a"]);
        Assert.Equal("72 45", msg.Tags["c"]);
        Assert.Equal("gh;764", msg.Tags["d"]);
    }

    [Fact]
    public void Parse_TagsWithEmptyValues()
    {
        var msg = IrcParser.Parse("@c;h=;a=b :quux ab cd")!;
        Assert.Equal("", msg.Tags["c"]);
        Assert.Equal("", msg.Tags["h"]);
        Assert.Equal("b", msg.Tags["a"]);
        Assert.Equal("quux", msg.Source!.Nick);
        Assert.Equal("AB", msg.Command);
        Assert.Equal(["cd"], msg.Parameters);
    }

    [Fact]
    public void Parse_SourceJoinChannel()
    {
        var msg = IrcParser.Parse(":src JOIN #chan")!;
        Assert.Equal("src", msg.Source!.Nick);
        Assert.Equal("JOIN", msg.Command);
        Assert.Equal(["#chan"], msg.Parameters);
    }

    [Fact]
    public void Parse_SourceJoinChannelTrailing()
    {
        var msg = IrcParser.Parse(":src JOIN :#chan")!;
        Assert.Equal(["#chan"], msg.Parameters);
    }

    [Fact]
    public void Parse_SourceAwayNoParams()
    {
        var msg = IrcParser.Parse(":src AWAY")!;
        Assert.Equal("AWAY", msg.Command);
        Assert.Empty(msg.Parameters);
    }

    [Fact]
    public void Parse_SourceAwayTrailingSpace()
    {
        // Trailing space should be ignored (no param)
        var msg = IrcParser.Parse(":src AWAY ")!;
        Assert.Equal("AWAY", msg.Command);
        Assert.Empty(msg.Parameters);
    }

    [Fact]
    public void Parse_SourceWithTab()
    {
        var msg = IrcParser.Parse(":cool\tguy foo bar baz")!;
        Assert.Equal("cool\tguy", msg.Source!.Nick);
    }

    [Fact]
    public void Parse_FullSourcePrefix()
    {
        var msg = IrcParser.Parse(":coolguy!ag@net\x035w\x03ork.admin PRIVMSG foo :bar baz")!;
        Assert.Equal("coolguy", msg.Source!.Nick);
        Assert.Equal("ag", msg.Source.User);
        Assert.Equal("net\x035w\x03ork.admin", msg.Source.Host);
        Assert.Equal(["foo", "bar baz"], msg.Parameters);
    }

    [Fact]
    public void Parse_ComplexTagsAndSource()
    {
        var msg = IrcParser.Parse(
            "@tag1=value1;tag2;vendor1/tag3=value2;vendor2/tag4= :irc.example.com COMMAND param1 param2 :param3 param3")!;
        Assert.Equal("value1", msg.Tags["tag1"]);
        Assert.Equal("", msg.Tags["tag2"]);
        Assert.Equal("value2", msg.Tags["vendor1/tag3"]);
        Assert.Equal("", msg.Tags["vendor2/tag4"]);
        Assert.Equal("irc.example.com", msg.Source!.Host);
        Assert.Null(msg.Source.Nick);
        Assert.Equal("COMMAND", msg.Command);
        Assert.Equal(["param1", "param2", "param3 param3"], msg.Parameters);
    }

    [Fact]
    public void Parse_CommandOnly()
    {
        var msg = IrcParser.Parse("COMMAND")!;
        Assert.Equal("COMMAND", msg.Command);
        Assert.Empty(msg.Parameters);
    }

    [Fact]
    public void Parse_ComplexTagEscaping()
    {
        // @foo=\\\\\\:\\\\s\\s\\r\\n COMMAND
        var msg = IrcParser.Parse("@foo=\\\\\\\\\\:\\\\s\\s\\r\\n COMMAND")!;
        Assert.Equal("\\\\;\\s \r\n", msg.Tags["foo"]);
    }

    [Fact]
    public void Parse_MozillaErroneousNickname()
    {
        var msg = IrcParser.Parse(":gravel.mozilla.org 432  #momo :Erroneous Nickname: Illegal characters")!;
        Assert.Equal("gravel.mozilla.org", msg.Source!.Host);
        Assert.Equal("432", msg.Command);
        Assert.Equal(["#momo", "Erroneous Nickname: Illegal characters"], msg.Parameters);
    }

    [Fact]
    public void Parse_ModeWithTrailingSpace()
    {
        var msg = IrcParser.Parse(":gravel.mozilla.org MODE #tckk +n ")!;
        Assert.Equal("MODE", msg.Command);
        Assert.Equal(["#tckk", "+n"], msg.Parameters);
    }

    [Fact]
    public void Parse_ModeMultipleSpaces()
    {
        // Multiple spaces between parameters should be handled
        var msg = IrcParser.Parse(":services.esper.net MODE #foo-bar +o foobar  ")!;
        Assert.Equal(["#foo-bar", "+o", "foobar"], msg.Parameters);
    }

    [Fact]
    public void Parse_TagBackslashNotEscaping()
    {
        // \n in the middle of other text: value\ntest -> value + newline + test? No, this is \\ntest
        // @tag1=value\\ntest means value\ntest (literal backslash followed by 'n' is \n)
        var msg = IrcParser.Parse("@tag1=value\\\\ntest COMMAND")!;
        Assert.Equal("value\\ntest", msg.Tags["tag1"]);
    }

    [Fact]
    public void Parse_TagInvalidEscape()
    {
        // \1 is an invalid escape — drop the backslash, keep the character
        var msg = IrcParser.Parse("@tag1=value\\1 COMMAND")!;
        Assert.Equal("value1", msg.Tags["tag1"]);
    }

    [Fact]
    public void Parse_TagTrailingBackslash()
    {
        // Trailing backslash produces no output character
        var msg = IrcParser.Parse("@tag1=value1\\ COMMAND")!;
        Assert.Equal("value1", msg.Tags["tag1"]);
    }

    [Fact]
    public void Parse_DuplicateTagsLastWins()
    {
        var msg = IrcParser.Parse("@tag1=1;tag2=3;tag3=4;tag1=5 COMMAND")!;
        Assert.Equal("5", msg.Tags["tag1"]);
        Assert.Equal("3", msg.Tags["tag2"]);
        Assert.Equal("4", msg.Tags["tag3"]);
    }

    [Fact]
    public void Parse_DuplicateTagsWithVendor()
    {
        var msg = IrcParser.Parse("@tag1=1;tag2=3;tag3=4;tag1=5;vendor/tag2=8 COMMAND")!;
        Assert.Equal("5", msg.Tags["tag1"]);
        Assert.Equal("3", msg.Tags["tag2"]);
        Assert.Equal("8", msg.Tags["vendor/tag2"]);
    }

    [Fact]
    public void Parse_ModeTrailing()
    {
        var msg = IrcParser.Parse(":SomeOp MODE #channel :+i")!;
        Assert.Equal(["#channel", "+i"], msg.Parameters);
    }

    [Fact]
    public void Parse_ModeMultipleParams()
    {
        var msg = IrcParser.Parse(":SomeOp MODE #channel +oo SomeUser :AnotherUser")!;
        Assert.Equal(["#channel", "+oo", "SomeUser", "AnotherUser"], msg.Parameters);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsNull()
    {
        Assert.Null(IrcParser.Parse(""));
        Assert.Null(IrcParser.Parse(null!));
    }

    [Fact]
    public void Parse_CommandUppercased()
    {
        var msg = IrcParser.Parse("privmsg #test :hello")!;
        Assert.Equal("PRIVMSG", msg.Command);
    }

    [Fact]
    public void Parse_SourceNickOnly()
    {
        var source = IrcParser.ParseSource("coolguy");
        Assert.Equal("coolguy", source.Nick);
        Assert.Null(source.User);
        Assert.Null(source.Host);
    }

    [Fact]
    public void Parse_SourceNickAndHost()
    {
        var source = IrcParser.ParseSource("coolguy@hostname");
        Assert.Equal("coolguy", source.Nick);
        Assert.Null(source.User);
        Assert.Equal("hostname", source.Host);
    }

    [Fact]
    public void Parse_SourceFull()
    {
        var source = IrcParser.ParseSource("coolguy!ident@hostname");
        Assert.Equal("coolguy", source.Nick);
        Assert.Equal("ident", source.User);
        Assert.Equal("hostname", source.Host);
    }

    [Fact]
    public void Parse_SourceServerName()
    {
        var source = IrcParser.ParseSource("irc.example.com");
        Assert.Null(source.Nick);
        Assert.Null(source.User);
        Assert.Equal("irc.example.com", source.Host);
    }
}
