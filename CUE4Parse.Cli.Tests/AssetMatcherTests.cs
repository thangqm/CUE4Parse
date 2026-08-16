using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;

namespace CUE4Parse.Cli.Tests;

public class AssetMatcherTests
{
    [Theory]
    [InlineData("Game/Content/Chars/A.uasset", "**/Chars/**", true)]
    [InlineData("Game/Content/Chars/A.uasset", "**/*.uasset", true)]
    [InlineData("Game/Content/Chars/A.uasset", "**/Props/**", false)]
    [InlineData("Game/Content/A.uasset", "Game/*/A.uasset", true)]
    [InlineData("Game/Content/Sub/A.uasset", "Game/*/A.uasset", false)]
    public void GlobMatchesAcrossAndWithinSegments(string path, string glob, bool expected)
        => Assert.Equal(expected, AssetMatcher.IsMatch(path, new MatchCriteria(Globs: [glob])));

    [Fact]
    public void GlobMatchingIsCaseInsensitive()
        => Assert.True(AssetMatcher.IsMatch("Game/Content/A.uasset", new MatchCriteria(Globs: ["**/content/**"])));

    [Fact]
    public void MultipleGlobsMatchIfAnyMatches()
    {
        var criteria = new MatchCriteria(Globs: ["**/Props/**", "**/Chars/**"]);
        Assert.True(AssetMatcher.IsMatch("Game/Chars/A.uasset", criteria));
    }

    [Fact]
    public void RegexCriteriaIsApplied()
        => Assert.True(AssetMatcher.IsMatch("Game/Chars/CID_001.uasset", new MatchCriteria(Regex: @"CID_\d+")));

    [Fact]
    public void ExtensionCriteriaIsAppliedWithoutRequiringLeadingDot()
    {
        Assert.True(AssetMatcher.IsMatch("A.uasset", new MatchCriteria(Extension: "uasset")));
        Assert.True(AssetMatcher.IsMatch("A.uasset", new MatchCriteria(Extension: ".uasset")));
        Assert.False(AssetMatcher.IsMatch("A.umap", new MatchCriteria(Extension: "uasset")));
    }

    [Fact]
    public void EmptyCriteriaMatchesEverything()
        => Assert.True(AssetMatcher.IsMatch("anything", new MatchCriteria()));

    [Fact]
    public void FilterAppliesLimitAsATruncationButStillReportsTheTrueTotal()
    {
        string[] paths = ["a.uasset", "b.uasset", "c.uasset"];
        var result = AssetMatcher.Filter(paths, new MatchCriteria(Limit: 2));
        Assert.Equal(2, result.Paths.Count);
        Assert.Equal(3, result.TotalMatched);
    }

    [Fact]
    public void FilterDeduplicatesPathsRepeatedAcrossMountedIndices()
    {
        string[] paths = ["a.uasset", "A.uasset", "b.uasset"];
        var result = AssetMatcher.Filter(paths, new MatchCriteria());
        Assert.Equal(2, result.TotalMatched);
    }

    [Fact]
    public void EnforceLimitThrowsUsageErrorWithMatchCountWhenExceeded()
    {
        var result = new MatchResult([], 1001);
        var ex = Assert.Throws<CliException>(
            () => AssetMatcher.EnforceLimit(result, new MatchCriteria(), force: false));

        Assert.Equal(ExitCode.Usage, ex.ExitCode);
        Assert.Equal("LIMIT_EXCEEDED", ex.ErrorCode);
        Assert.Contains("1001", ex.Message);
    }

    [Fact]
    public void EnforceLimitUsesTheTrueTotalNotTheTruncatedCount()
    {
        // The truncated list is small; the guard must still fire on the real total.
        var result = new MatchResult(["a", "b"], 50_000);
        Assert.Throws<CliException>(
            () => AssetMatcher.EnforceLimit(result, new MatchCriteria(), force: false));
    }

    [Fact]
    public void EnforceLimitTreatsAnExplicitLimitAsConsent()
        => AssetMatcher.EnforceLimit(
            new MatchResult(["a"], 50_000), new MatchCriteria(Limit: 1), force: false);

    [Fact]
    public void EnforceLimitAllowsExceedingWhenForced()
        => AssetMatcher.EnforceLimit(new MatchResult([], 5000), new MatchCriteria(), force: true);

    [Fact]
    public void EnforceLimitAllowsExactlyTheLimit()
        => AssetMatcher.EnforceLimit(new MatchResult([], 1000), new MatchCriteria(), force: false);
}
