using Xunit;
using Marv.Core.Formatting;

namespace Marv.Core.Tests.Formatting;

public class IrcFormatTests
{
    [Fact]
    public void Bold_WrapsWithToggle()
    {
        Assert.Equal("\x02hello\x02", IrcFormat.Bold("hello"));
    }

    [Fact]
    public void Italic_WrapsWithToggle()
    {
        Assert.Equal("\x1Dhello\x1D", IrcFormat.Italic("hello"));
    }

    [Fact]
    public void Underline_WrapsWithToggle()
    {
        Assert.Equal("\x1Fhello\x1F", IrcFormat.Underline("hello"));
    }

    [Fact]
    public void Strikethrough_WrapsWithToggle()
    {
        Assert.Equal("\x1Ehello\x1E", IrcFormat.Strikethrough("hello"));
    }

    [Fact]
    public void Monospace_WrapsWithToggle()
    {
        Assert.Equal("\x11hello\x11", IrcFormat.Monospace("hello"));
    }

    [Fact]
    public void Reverse_WrapsWithToggle()
    {
        Assert.Equal("\x16hello\x16", IrcFormat.Reverse("hello"));
    }

    [Fact]
    public void Color_ForegroundOnly_WrapsWithColorReset()
    {
        var result = IrcFormat.Color("hello", IrcColor.Red);
        Assert.Equal("04hello", result);
    }

    [Fact]
    public void Color_ForegroundAndBackground_WrapsWithColorReset()
    {
        var result = IrcFormat.Color("hello", IrcColor.White, IrcColor.Black);
        Assert.Equal("00,01hello", result);
    }

    [Fact]
    public void Strip_RemovesBold()
    {
        Assert.Equal("hello", IrcFormat.Strip("\x02hello\x02"));
    }

    [Fact]
    public void Strip_RemovesColorWithDigits()
    {
        Assert.Equal("hello", IrcFormat.Strip("4hello"));
    }

    [Fact]
    public void Strip_RemovesColorWithFgAndBg()
    {
        Assert.Equal("hello", IrcFormat.Strip("4,2hello"));
    }

    [Fact]
    public void Strip_RemovesTwoDigitColor()
    {
        Assert.Equal("hello", IrcFormat.Strip("10hello"));
    }

    [Fact]
    public void Strip_RemovesReset()
    {
        Assert.Equal("hello", IrcFormat.Strip("hello\x0F"));
    }

    [Fact]
    public void Strip_RemovesAllFormattingFromComplexMessage()
    {
        var formatted = "10,01[7 Community 10] :: [3 Network: 7NBC 10] :: "
            + "[ 3Runtime:7 25 minutes 10] :: [3 Rating:7 \x02TV-PG\x0210 ] :: "
            + "[14 https://thetvdb.com/series/community 10]\x0F";
        var plain = IrcFormat.Strip(formatted);
        Assert.Equal("[ Community ] :: [ Network: NBC ] :: [ Runtime: 25 minutes ] :: "
            + "[ Rating: TV-PG ] :: [ https://thetvdb.com/series/community ]", plain);
    }

    [Fact]
    public void Strip_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", IrcFormat.Strip(""));
    }

    [Fact]
    public void Strip_Null_ReturnsNull()
    {
        Assert.Null(IrcFormat.Strip(null!));
    }

    [Fact]
    public void Strip_PlainText_ReturnsUnchanged()
    {
        Assert.Equal("no formatting here", IrcFormat.Strip("no formatting here"));
    }

    [Fact]
    public void Strip_RemovesHexColor()
    {
        Assert.Equal("hello", IrcFormat.Strip("FF0000hello"));
    }

    [Fact]
    public void Strip_RemovesHexColorWithBackground()
    {
        Assert.Equal("hello", IrcFormat.Strip("FF0000,00FF00hello"));
    }

    [Fact]
    public void StatefulPattern_ProducesExpectedOutput()
    {
        var msg = $"{IrcColor.Cyan.On(IrcColor.Black)}[{IrcColor.Orange} Community "
            + $"{IrcColor.Cyan}] :: [{IrcColor.Green} Network: {IrcColor.Orange}NBC "
            + $"{IrcColor.Cyan}] :: [ {IrcColor.Green}Runtime:{IrcColor.Orange} 25 minutes "
            + $"{IrcColor.Cyan}] :: [{IrcColor.Green} Rating:{IrcColor.Orange} "
            + $"{IrcFormat.Bold("TV-PG")}{IrcColor.Cyan} ] :: "
            + $"[{IrcColor.Grey} https://thetvdb.com/series/community {IrcColor.Cyan}]{IrcFormat.Reset}";

        var plain = IrcFormat.Strip(msg);
        Assert.Equal("[ Community ] :: [ Network: NBC ] :: [ Runtime: 25 minutes ] :: "
            + "[ Rating: TV-PG ] :: [ https://thetvdb.com/series/community ]", plain);
    }
}
