using Xunit;
using Marv.Core.Formatting;

namespace Marv.Core.Tests.Formatting;

public class IrcFormatExtensionsTests
{
    [Fact]
    public void Bold_Extension_MatchesStaticMethod()
    {
        Assert.Equal(IrcFormat.Bold("hello"), "hello".Bold());
    }

    [Fact]
    public void Color_Extension_MatchesStaticMethod()
    {
        Assert.Equal(
            IrcFormat.Color("hello", IrcColor.Red),
            "hello".Color(IrcColor.Red));
    }

    [Fact]
    public void Color_WithBackground_Extension_MatchesStaticMethod()
    {
        Assert.Equal(
            IrcFormat.Color("hello", IrcColor.White, IrcColor.Black),
            "hello".Color(IrcColor.White, IrcColor.Black));
    }

    [Fact]
    public void Chaining_ProducesNestedFormatting()
    {
        var result = "hello".Bold().Underline();
        Assert.Equal("\x1F\x02hello\x02\x1F", result);
    }
}
