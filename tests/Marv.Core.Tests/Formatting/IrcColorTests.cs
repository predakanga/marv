using Xunit;
using Marv.Core.Formatting;

namespace Marv.Core.Tests.Formatting;

public class IrcColorTests
{
    [Fact]
    public void ToString_SingleDigitCode_PadsToTwoDigits()
    {
        Assert.Equal("07", IrcColor.Orange.ToString());
    }

    [Fact]
    public void ToString_TwoDigitCode_NoExtraPadding()
    {
        Assert.Equal("10", IrcColor.Cyan.ToString());
    }

    [Fact]
    public void ToString_Default99_FormatsCorrectly()
    {
        Assert.Equal("99", IrcColor.Default.ToString());
    }

    [Fact]
    public void On_EmitsForegroundCommaBackground()
    {
        var result = IrcColor.Cyan.On(IrcColor.Black);
        Assert.Equal("10,01", result);
    }

    [Fact]
    public void On_BothSingleDigit_PadsBoth()
    {
        var result = IrcColor.Red.On(IrcColor.White);
        Assert.Equal("04,00", result);
    }

    [Fact]
    public void StringInterpolation_EmitsColorCode()
    {
        var result = $"{IrcColor.Green}hello";
        Assert.Equal("03hello", result);
    }

    [Fact]
    public void Constructor_ExtendedColor_Accepted()
    {
        var color = new IrcColor(42);
        Assert.Equal(42, color.Code);
        Assert.Equal("42", color.ToString());
    }

    [Fact]
    public void Constructor_NegativeCode_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IrcColor(-1));
    }

    [Fact]
    public void Constructor_Over99_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IrcColor(100));
    }

    [Fact]
    public void Equality_SameCode_AreEqual()
    {
        Assert.Equal(IrcColor.Red, new IrcColor(4));
        Assert.True(IrcColor.Red == new IrcColor(4));
    }

    [Fact]
    public void Equality_DifferentCode_AreNotEqual()
    {
        Assert.NotEqual(IrcColor.Red, IrcColor.Blue);
        Assert.True(IrcColor.Red != IrcColor.Blue);
    }
}
