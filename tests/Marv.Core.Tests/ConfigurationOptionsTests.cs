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
/// round-trip correctly through the <see cref="CommandLineConfigurationProvider"/>.
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
    /// Builds configuration data from a <see cref="ParseResult"/> using the
    /// <see cref="CommandLineConfigurationProvider"/>, mirroring the real configuration source.
    /// </summary>
    private static Dictionary<string, string?> GetCliData(ParseResult result)
    {
        var configBuilder = new ConfigurationBuilder();
        configBuilder.Sources.Add(ConfigurationOptions.CreateSource(result));
        var config = configBuilder.Build();

        var data = new Dictionary<string, string?>();
        foreach (var kvp in config.AsEnumerable())
        {
            if (kvp.Value is not null)
                data[kvp.Key] = kvp.Value;
        }
        return data;
    }

    /// <summary>
    /// Builds a <see cref="MarvConfiguration"/> by layering a base config dictionary
    /// with CLI overrides via the <see cref="CommandLineConfigurationProvider"/>.
    /// </summary>
    private static MarvConfiguration BuildConfig(Dictionary<string, string?> baseConfig, params string[] cliArgs)
    {
        var result = Parse(cliArgs);

        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(baseConfig);
        configBuilder.Sources.Add(ConfigurationOptions.CreateSource(result));

        var config = configBuilder.Build();
        var marvConfig = new MarvConfiguration();
        config.Bind(marvConfig);
        return marvConfig;
    }

    [Fact]
    public void NoArguments_ProducesNoOverrides()
    {
        var result = Parse();
        var data = GetCliData(result);

        Assert.Empty(data);
    }

    // -- Bool options ----------------------------------------------------------

    [Fact]
    public void BoolOption_NotProvided_IsNotInOverrides()
    {
        var result = Parse("--server", "irc.example.com");
        var data = GetCliData(result);

        Assert.DoesNotContain("UseTls", data.Keys);
        Assert.DoesNotContain("TlsSkipCertificateValidation", data.Keys);
        Assert.DoesNotContain("RateLimitEnabled", data.Keys);
    }

    [Fact]
    public void BoolOption_ExplicitlyTrue_IsInOverrides()
    {
        var result = Parse("--use-tls", "true");
        var data = GetCliData(result);

        Assert.Equal("True", data["UseTls"]);
    }

    [Fact]
    public void BoolOption_ExplicitlyFalse_IsInOverrides()
    {
        var result = Parse("--rate-limit-enabled", "false");
        var data = GetCliData(result);

        Assert.Equal("False", data["RateLimitEnabled"]);
    }

    // -- String options --------------------------------------------------------

    [Fact]
    public void StringOption_NotProvided_IsNotInOverrides()
    {
        var result = Parse("--port", "6697");
        var data = GetCliData(result);

        Assert.DoesNotContain("Server", data.Keys);
        Assert.DoesNotContain("Nick", data.Keys);
    }

    [Fact]
    public void StringOption_Provided_IsInOverrides()
    {
        var result = Parse("--server", "irc.example.com");
        var data = GetCliData(result);

        Assert.Equal("irc.example.com", data["Server"]);
    }

    // -- Int options -----------------------------------------------------------

    [Fact]
    public void IntOption_NotProvided_IsNotInOverrides()
    {
        var result = Parse("--server", "irc.example.com");
        var data = GetCliData(result);

        Assert.DoesNotContain("Port", data.Keys);
    }

    [Fact]
    public void IntOption_Provided_IsInOverrides()
    {
        var result = Parse("--port", "6697");
        var data = GetCliData(result);

        Assert.Equal("6697", data["Port"]);
    }

    // -- Double options --------------------------------------------------------

    [Fact]
    public void DoubleOption_NotProvided_IsNotInOverrides()
    {
        var result = Parse("--server", "irc.example.com");
        var data = GetCliData(result);

        Assert.DoesNotContain("RateLimitRefillRate", data.Keys);
    }

    [Fact]
    public void DoubleOption_Provided_IsInOverrides()
    {
        var result = Parse("--rate-limit-refill-rate", "2.5");
        var data = GetCliData(result);

        Assert.Equal("2.5", data["RateLimitRefillRate"]);
    }

    // -- Collection options ----------------------------------------------------

    [Fact]
    public void CollectionOption_NotProvided_IsNotInOverrides()
    {
        var result = Parse("--server", "irc.example.com");
        var data = GetCliData(result);

        Assert.DoesNotContain("Channels:0", data.Keys);
        Assert.DoesNotContain("Plugins:0", data.Keys);
    }

    [Fact]
    public void CollectionOption_Provided_IsInOverrides()
    {
        var result = Parse("--channels", "#foo", "#bar");
        var data = GetCliData(result);

        Assert.Equal("#foo", data["Channels:0"]);
        Assert.Equal("#bar", data["Channels:1"]);
    }

    // -- Enum options ----------------------------------------------------------

    [Fact]
    public void EnumOption_NotProvided_IsNotInOverrides()
    {
        var result = Parse("--server", "irc.example.com");
        var data = GetCliData(result);

        Assert.DoesNotContain("LogLevel", data.Keys);
    }

    [Fact]
    public void EnumOption_Provided_IsInOverrides()
    {
        var result = Parse("--log-level", "Debug");
        var data = GetCliData(result);

        Assert.Equal("Debug", data["LogLevel"]);
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
        var data = GetCliData(result);

        Assert.DoesNotContain("TlsCaCertFile", data.Keys);
        Assert.DoesNotContain("SaslUser", data.Keys);
        Assert.DoesNotContain("SaslPassword", data.Keys);
        Assert.DoesNotContain("ServerPassword", data.Keys);
        Assert.DoesNotContain("NickServPassword", data.Keys);
        Assert.DoesNotContain("OperName", data.Keys);
        Assert.DoesNotContain("OperPassword", data.Keys);
        Assert.DoesNotContain("SentryDsn", data.Keys);
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

            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddJsonFile(tempFile, optional: false, reloadOnChange: false);
            configBuilder.Sources.Add(ConfigurationOptions.CreateSource(result));

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

            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddJsonFile(tempFile, optional: false, reloadOnChange: false);
            configBuilder.Sources.Add(ConfigurationOptions.CreateSource(result));

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
