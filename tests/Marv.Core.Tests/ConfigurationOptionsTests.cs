using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using Marv;
using Marv.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
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

    /// <summary>
    /// Builds a <see cref="MarvConfiguration"/> by layering a base config dictionary
    /// with CLI overrides, mirroring the real layering in Program.cs.
    /// </summary>
    private static MarvConfiguration BuildConfig(Dictionary<string, string?> baseConfig, params string[] cliArgs)
    {
        var result = Parse(cliArgs);
        var overrides = ConfigurationOptions.GetOverrides(result);

        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(baseConfig);
        if (overrides.Count > 0)
            configBuilder.AddInMemoryCollection(overrides);

        var config = configBuilder.Build();
        var marvConfig = new MarvConfiguration();
        config.Bind(marvConfig);
        return marvConfig;
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

    // -- End-to-end config layering --------------------------------------------

    [Fact]
    public void ConfigFile_StringValues_NotOverriddenByAbsentCliArgs()
    {
        var baseConfig = new Dictionary<string, string?>
        {
            ["Server"] = "irc.libera.chat",
            ["Nick"] = "TestBot",
            ["SaslUser"] = "myuser",
            ["TlsCaCertFile"] = "/path/to/ca.pem",
        };

        var config = BuildConfig(baseConfig);

        Assert.Equal("irc.libera.chat", config.Server);
        Assert.Equal("TestBot", config.Nick);
        Assert.Equal("myuser", config.SaslUser);
        Assert.Equal("/path/to/ca.pem", config.TlsCaCertFile);
    }

    [Fact]
    public void NullableStringOption_NotProvided_IsNotInOverrides()
    {
        var result = Parse("--server", "irc.example.com");
        var overrides = ConfigurationOptions.GetOverrides(result);

        Assert.DoesNotContain("TlsCaCertFile", overrides.Keys);
        Assert.DoesNotContain("SaslUser", overrides.Keys);
        Assert.DoesNotContain("SaslPassword", overrides.Keys);
        Assert.DoesNotContain("ServerPassword", overrides.Keys);
        Assert.DoesNotContain("NickServPassword", overrides.Keys);
        Assert.DoesNotContain("OperName", overrides.Keys);
        Assert.DoesNotContain("OperPassword", overrides.Keys);
        Assert.DoesNotContain("SentryDsn", overrides.Keys);
    }

    [Fact]
    public void ConfigFile_BoolValues_NotOverriddenByAbsentCliArgs()
    {
        var baseConfig = new Dictionary<string, string?>
        {
            ["UseTls"] = "true",
            ["RateLimitEnabled"] = "false",
        };

        var config = BuildConfig(baseConfig);

        Assert.True(config.UseTls);
        Assert.False(config.RateLimitEnabled);
    }

    [Fact]
    public void ConfigFile_IntValues_NotOverriddenByAbsentCliArgs()
    {
        var baseConfig = new Dictionary<string, string?>
        {
            ["Port"] = "6697",
            ["RateLimitBurst"] = "10",
        };

        var config = BuildConfig(baseConfig);

        Assert.Equal(6697, config.Port);
        Assert.Equal(10, config.RateLimitBurst);
    }

    [Fact]
    public void ConfigFile_DoubleValues_NotOverriddenByAbsentCliArgs()
    {
        var baseConfig = new Dictionary<string, string?>
        {
            ["RateLimitRefillRate"] = "2.5",
        };

        var config = BuildConfig(baseConfig);

        Assert.Equal(2.5, config.RateLimitRefillRate);
    }

    [Fact]
    public void ConfigFile_NullableStringWithValue_NotOverriddenByAbsentCliArgs()
    {
        var baseConfig = new Dictionary<string, string?>
        {
            ["TlsCaCertFile"] = "/path/to/ca.pem",
            ["SaslUser"] = "testuser",
        };

        // Provide some other CLI arg but not --tls-ca-cert-file or --sasl-user
        var config = BuildConfig(baseConfig, "--server", "irc.example.com");

        Assert.Equal("/path/to/ca.pem", config.TlsCaCertFile);
        Assert.Equal("testuser", config.SaslUser);
    }

    [Fact]
    public void ConfigFile_NullableStringSetToNull_RemainsNull()
    {
        var baseConfig = new Dictionary<string, string?>
        {
            ["TlsCaCertFile"] = null,
            ["SaslUser"] = null,
        };

        var config = BuildConfig(baseConfig);

        Assert.Null(config.TlsCaCertFile);
        Assert.Null(config.SaslUser);
    }

    [Fact]
    public void JsonConfigFile_ExplicitNull_NotOverriddenByAbsentCliArgs()
    {
        // Reproduce the real config layering: JSON file with explicit null values,
        // then CLI overrides for unrelated options.
        var json = JsonSerializer.Serialize(new
        {
            Server = "irc.libera.chat",
            Port = 6697,
            UseTls = true,
            TlsCaCertFile = (string?)null,
            SaslUser = (string?)null,
            Nick = "TestBot",
        });

        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, json);
        try
        {
            var result = Parse("--nick", "CliBot");
            var overrides = ConfigurationOptions.GetOverrides(result);

            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddJsonFile(tempFile, optional: false, reloadOnChange: false);
            if (overrides.Count > 0)
                configBuilder.AddInMemoryCollection(overrides);

            var builtConfig = configBuilder.Build();
            var marvConfig = new MarvConfiguration();
            builtConfig.Bind(marvConfig);

            Assert.Equal("irc.libera.chat", marvConfig.Server);
            Assert.True(marvConfig.UseTls);
            Assert.Null(marvConfig.TlsCaCertFile);
            Assert.Null(marvConfig.SaslUser);
            Assert.Equal("CliBot", marvConfig.Nick);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void JsonConfigFile_StringValues_NotOverriddenByAbsentCliArgs()
    {
        // JSON file with real string values — verify CLI for other options doesn't clobber them.
        var json = JsonSerializer.Serialize(new
        {
            Server = "irc.libera.chat",
            TlsCaCertFile = "/path/to/ca.pem",
            SaslUser = "myuser",
            Nick = "TestBot",
        });

        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, json);
        try
        {
            var result = Parse("--nick", "CliBot");
            var overrides = ConfigurationOptions.GetOverrides(result);

            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddJsonFile(tempFile, optional: false, reloadOnChange: false);
            if (overrides.Count > 0)
                configBuilder.AddInMemoryCollection(overrides);

            var builtConfig = configBuilder.Build();
            var marvConfig = new MarvConfiguration();
            builtConfig.Bind(marvConfig);

            Assert.Equal("irc.libera.chat", marvConfig.Server);
            Assert.Equal("/path/to/ca.pem", marvConfig.TlsCaCertFile);
            Assert.Equal("myuser", marvConfig.SaslUser);
            Assert.Equal("CliBot", marvConfig.Nick);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CliArgs_OverrideConfigFileValues()
    {
        var baseConfig = new Dictionary<string, string?>
        {
            ["Server"] = "irc.libera.chat",
            ["Port"] = "6667",
            ["UseTls"] = "false",
            ["Nick"] = "OldBot",
        };

        var config = BuildConfig(baseConfig, "--server", "irc.efnet.org", "--use-tls", "true", "--port", "6697");

        Assert.Equal("irc.efnet.org", config.Server);
        Assert.Equal(6697, config.Port);
        Assert.True(config.UseTls);
        Assert.Equal("OldBot", config.Nick);
    }
}
