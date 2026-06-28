using Xunit;

namespace Marv.Core.Tests;

/// <summary>
/// Tests for <see cref="BotIdentity"/> and version resolution.
/// </summary>
public class BotIdentityTests
{
    [Fact]
    public void DefaultIdentity_UsesDefaultNameAndResolvedVersion()
    {
        var config = new MarvConfiguration();
        var identity = new BotIdentity(
            config.BotName,
            config.BotVersion ?? MarvServiceExtensions.ResolveVersion());

        Assert.Equal("Marv IRC Bot", identity.Name);
        Assert.NotNull(identity.Version);
        Assert.NotEmpty(identity.Version);
    }

    [Fact]
    public void ConfigOverride_UsesConfiguredNameAndVersion()
    {
        var config = new MarvConfiguration
        {
            BotName = "IdleRPG Bot",
            BotVersion = "2.5.0"
        };
        var identity = new BotIdentity(
            config.BotName,
            config.BotVersion ?? MarvServiceExtensions.ResolveVersion());

        Assert.Equal("IdleRPG Bot", identity.Name);
        Assert.Equal("2.5.0", identity.Version);
    }

    [Fact]
    public void FullIdentity_CombinesNameAndVersion()
    {
        var identity = new BotIdentity("MyBot", "1.2.3");

        Assert.Equal("MyBot 1.2.3", identity.FullIdentity);
    }

    [Fact]
    public void SourceUrl_IsOptional()
    {
        var withoutUrl = new BotIdentity("Bot", "1.0");
        Assert.Null(withoutUrl.SourceUrl);

        var withUrl = new BotIdentity("Bot", "1.0", "https://example.com");
        Assert.Equal("https://example.com", withUrl.SourceUrl);
    }

    [Fact]
    public void ResolveVersion_FallsBackToAssemblyVersion()
    {
        var version = MarvServiceExtensions.ResolveVersion();

        Assert.NotNull(version);
        Assert.NotEmpty(version);
        Assert.DoesNotContain("+", version);
    }
}
