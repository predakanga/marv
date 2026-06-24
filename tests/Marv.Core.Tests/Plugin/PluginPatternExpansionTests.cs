using Marv.Core.Plugin;
using Xunit;

namespace Marv.Core.Tests.Plugin;

/// <summary>
/// Tests for wildcard and negation pattern expansion in
/// <see cref="PluginManager.ExpandPluginPatterns"/>.
/// </summary>
public class PluginPatternExpansionTests
{
    private static PluginMetadata Meta(string name) =>
        new(name, $"/plugins/Marv.Plugins.{name}.dll", $"Marv.Plugins.{name}.dll");

    private static readonly IReadOnlyList<PluginMetadata> AllPlugins =
    [
        Meta("Auth"),
        Meta("Greet"),
        Meta("CannedResponses"),
        Meta("Moderation"),
        Meta("IdleRPG.Core"),
        Meta("IdleRPG.Combat"),
    ];

    [Fact]
    public void PlainNames_PassThroughUnchanged()
    {
        var result = PluginManager.ExpandPluginPatterns(["Auth", "Greet"], AllPlugins);

        Assert.Equal(2, result.Count);
        Assert.Equal("Auth", result[0]);
        Assert.Equal("Greet", result[1]);
    }

    [Fact]
    public void WildcardStar_ExpandsToAllPlugins()
    {
        var result = PluginManager.ExpandPluginPatterns(["*"], AllPlugins);

        Assert.Equal(AllPlugins.Count, result.Count);
        foreach (var meta in AllPlugins)
            Assert.Contains(meta.Name, result);
    }

    [Fact]
    public void GlobPattern_MatchesPrefix()
    {
        var result = PluginManager.ExpandPluginPatterns(["IdleRPG.*"], AllPlugins);

        Assert.Equal(2, result.Count);
        Assert.Contains("IdleRPG.Core", result);
        Assert.Contains("IdleRPG.Combat", result);
    }

    [Fact]
    public void QuestionMarkWildcard_MatchesSingleCharacter()
    {
        var result = PluginManager.ExpandPluginPatterns(["Gree?"], AllPlugins);

        Assert.Single(result);
        Assert.Equal("Greet", result[0]);
    }

    [Fact]
    public void Negation_ExcludesExactName()
    {
        var result = PluginManager.ExpandPluginPatterns(["*", "!Greet"], AllPlugins);

        Assert.Equal(AllPlugins.Count - 1, result.Count);
        Assert.DoesNotContain("Greet", result);
    }

    [Fact]
    public void Negation_ExcludesGlobPattern()
    {
        var result = PluginManager.ExpandPluginPatterns(["*", "!IdleRPG.*"], AllPlugins);

        Assert.Equal(AllPlugins.Count - 2, result.Count);
        Assert.DoesNotContain("IdleRPG.Core", result);
        Assert.DoesNotContain("IdleRPG.Combat", result);
    }

    [Fact]
    public void Negation_UnmatchedName_IsNoOp()
    {
        var result = PluginManager.ExpandPluginPatterns(["*", "!NonExistent"], AllPlugins);

        Assert.Equal(AllPlugins.Count, result.Count);
    }

    [Fact]
    public void UnmatchedGlob_MatchesNothing()
    {
        var result = PluginManager.ExpandPluginPatterns(["Foo.*"], AllPlugins);

        Assert.Empty(result);
    }

    [Fact]
    public void DuplicateNames_AreSuppressed()
    {
        var result = PluginManager.ExpandPluginPatterns(["Auth", "Auth"], AllPlugins);

        Assert.Single(result);
        Assert.Equal("Auth", result[0]);
    }

    [Fact]
    public void GlobAndPlainName_DuplicateSuppressed()
    {
        var result = PluginManager.ExpandPluginPatterns(["Auth", "*"], AllPlugins);

        Assert.Equal(AllPlugins.Count, result.Count);
        Assert.Equal("Auth", result[0]);
    }

    [Fact]
    public void EvaluationOrder_NegationBeforeWildcard_HasNoEffect()
    {
        var result = PluginManager.ExpandPluginPatterns(["!Greet", "*"], AllPlugins);

        Assert.Equal(AllPlugins.Count, result.Count);
        Assert.Contains("Greet", result);
    }

    [Fact]
    public void CaseInsensitive_GlobMatching()
    {
        var result = PluginManager.ExpandPluginPatterns(["auth"], AllPlugins);

        Assert.Single(result);
        Assert.Equal("auth", result[0]);
    }

    [Fact]
    public void CaseInsensitive_GlobWildcardMatching()
    {
        var result = PluginManager.ExpandPluginPatterns(["idlerpg.*"], AllPlugins);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void CaseInsensitive_NegationMatching()
    {
        var result = PluginManager.ExpandPluginPatterns(["*", "!greet"], AllPlugins);

        Assert.Equal(AllPlugins.Count - 1, result.Count);
        Assert.DoesNotContain("Greet", result);
    }

    [Fact]
    public void EmptyPatterns_ReturnsEmpty()
    {
        var result = PluginManager.ExpandPluginPatterns([], AllPlugins);

        Assert.Empty(result);
    }

    [Fact]
    public void MultipleNegations_AllApplied()
    {
        var result = PluginManager.ExpandPluginPatterns(
            ["*", "!Auth", "!Greet", "!IdleRPG.*"], AllPlugins);

        Assert.Equal(2, result.Count);
        Assert.Contains("CannedResponses", result);
        Assert.Contains("Moderation", result);
    }
}
