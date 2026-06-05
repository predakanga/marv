using System.CommandLine;
using System.CommandLine.Parsing;
using Marv;
using Xunit;

namespace Marv.Core.Tests;

/// <summary>
/// Tests for <see cref="ConfigurationOptions"/> — verifies that CLI overrides are only
/// produced when the user explicitly provides an option, and that all property types
/// round-trip correctly.
/// </summary>
public class ConfigurationOptionsTests
{
    /// <summary>Builds a root command with all generated options and parses the given args.</summary>
    private static ParseResult Parse(params string[] args)
    {
        var root = new RootCommand("test");
        foreach (var opt in ConfigurationOptions.All)
            root.Add(opt);
        return root.Parse(args);
    }

    [Fact]
    public void NoArguments_ProducesNoOverrides()
    {
        var result = Parse();
        var overrides = ConfigurationOptions.GetOverrides(result);

        Assert.Empty(overrides);
    }

    // -- Bool options ----------------------------------------------------------

    [Fact]
    public void BoolOption_NotProvided_IsNotInOverrides()
    {
        var result = Parse("--server", "irc.example.com");
        var overrides = ConfigurationOptions.GetOverrides(result);

        Assert.DoesNotContain("UseTls", overrides.Keys);
        Assert.DoesNotContain("TlsSkipCertificateValidation", overrides.Keys);
        Assert.DoesNotContain("RateLimitEnabled", overrides.Keys);
    }

    [Fact]
    public void BoolOption_ExplicitlyTrue_IsInOverrides()
    {
        var result = Parse("--use-tls", "true");
        var overrides = ConfigurationOptions.GetOverrides(result);

        Assert.Equal("True", overrides["UseTls"]);
    }

    [Fact]
    public void BoolOption_ExplicitlyFalse_IsInOverrides()
    {
        var result = Parse("--rate-limit-enabled", "false");
        var overrides = ConfigurationOptions.GetOverrides(result);

        Assert.Equal("False", overrides["RateLimitEnabled"]);
    }

    // -- String options --------------------------------------------------------

    [Fact]
    public void StringOption_NotProvided_IsNotInOverrides()
    {
        var result = Parse("--port", "6697");
        var overrides = ConfigurationOptions.GetOverrides(result);

        Assert.DoesNotContain("Server", overrides.Keys);
        Assert.DoesNotContain("Nick", overrides.Keys);
    }

    [Fact]
    public void StringOption_Provided_IsInOverrides()
    {
        var result = Parse("--server", "irc.example.com");
        var overrides = ConfigurationOptions.GetOverrides(result);

        Assert.Equal("irc.example.com", overrides["Server"]);
    }

    // -- Int options -----------------------------------------------------------

    [Fact]
    public void IntOption_NotProvided_IsNotInOverrides()
    {
        var result = Parse("--server", "irc.example.com");
        var overrides = ConfigurationOptions.GetOverrides(result);

        Assert.DoesNotContain("Port", overrides.Keys);
    }

    [Fact]
    public void IntOption_Provided_IsInOverrides()
    {
        var result = Parse("--port", "6697");
        var overrides = ConfigurationOptions.GetOverrides(result);

        Assert.Equal("6697", overrides["Port"]);
    }

    // -- Double options --------------------------------------------------------

    [Fact]
    public void DoubleOption_NotProvided_IsNotInOverrides()
    {
        var result = Parse("--server", "irc.example.com");
        var overrides = ConfigurationOptions.GetOverrides(result);

        Assert.DoesNotContain("RateLimitRefillRate", overrides.Keys);
    }

    [Fact]
    public void DoubleOption_Provided_IsInOverrides()
    {
        var result = Parse("--rate-limit-refill-rate", "2.5");
        var overrides = ConfigurationOptions.GetOverrides(result);

        Assert.Equal("2.5", overrides["RateLimitRefillRate"]);
    }

    // -- Collection options ----------------------------------------------------

    [Fact]
    public void CollectionOption_NotProvided_IsNotInOverrides()
    {
        var result = Parse("--server", "irc.example.com");
        var overrides = ConfigurationOptions.GetOverrides(result);

        Assert.DoesNotContain("Channels:0", overrides.Keys);
        Assert.DoesNotContain("Plugins:0", overrides.Keys);
    }

    [Fact]
    public void CollectionOption_Provided_IsInOverrides()
    {
        var result = Parse("--channels", "#foo", "#bar");
        var overrides = ConfigurationOptions.GetOverrides(result);

        Assert.Equal("#foo", overrides["Channels:0"]);
        Assert.Equal("#bar", overrides["Channels:1"]);
    }

    // -- Enum options ----------------------------------------------------------

    [Fact]
    public void EnumOption_NotProvided_IsNotInOverrides()
    {
        var result = Parse("--server", "irc.example.com");
        var overrides = ConfigurationOptions.GetOverrides(result);

        Assert.DoesNotContain("LogLevel", overrides.Keys);
    }

    [Fact]
    public void EnumOption_Provided_IsInOverrides()
    {
        var result = Parse("--log-level", "Debug");
        var overrides = ConfigurationOptions.GetOverrides(result);

        Assert.Equal("Debug", overrides["LogLevel"]);
    }
}
