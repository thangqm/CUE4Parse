using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class DumpCommandTests
{
    [Fact]
    public void DumpEmitsNdjsonWithOneObjectPerMatchedAsset()
    {
        var (context, sw) = FixtureSupport.Context();

        var code = DumpCommand.Execute(context, new DumpOptions(
            Paths: [],
            Criteria: new MatchCriteria(Globs: ["**/*.uasset"], Limit: 3),
            ExportName: null, ClassName: null, Output: null, Indent: false, Force: false));

        Assert.Equal((int)ExitCode.Success, code);

        var lines = FixtureSupport.Ndjson(sw);
        Assert.NotEmpty(lines);
        foreach (var parsed in lines)
        {
            Assert.NotNull(parsed["path"]);
            Assert.NotNull(parsed["status"]);
        }
    }

    [Fact]
    public void DumpDeserializesUnversionedPropertiesUsingTheFixtureMappings()
    {
        var (context, sw) = FixtureSupport.Context();

        var code = DumpCommand.Execute(context, new DumpOptions(
            Paths: ["CUE4ParseFixtures/Content/Fixtures/Properties/DA_AllProperties.uasset"],
            Criteria: new MatchCriteria(),
            ExportName: null, ClassName: null, Output: null, Indent: false, Force: false));

        Assert.Equal((int)ExitCode.Success, code);
        // 0x12345678 — the deterministic fixture value asserted by CUE4Parse.Tests.
        Assert.Contains("305419896", sw.ToString());
    }

    [Fact]
    public void DumpFiltersExportsByClassName()
    {
        var (context, sw) = FixtureSupport.Context();

        DumpCommand.Execute(context, new DumpOptions(
            Paths: [],
            Criteria: new MatchCriteria(Globs: ["**/*.uasset"]),
            ExportName: null, ClassName: "Texture2D", Output: null, Indent: false, Force: true));

        var lines = FixtureSupport.Ndjson(sw);
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Equal("ok", line["status"]?.Value<string>()));
    }

    /// <summary>
    /// Pinned to the legacy container, which has no encrypted archive. Under the default
    /// profile an unmounted OodleEncrypted sibling is present, so the CLI answers
    /// "may be behind an unsubmitted key" (exit 5) rather than "not found" (exit 7) —
    /// correctly, since it genuinely cannot tell. This test is about the unambiguous case.
    /// </summary>
    [Fact]
    public void DumpReturnsNotFoundForAnUnknownExplicitPath()
    {
        var (context, _) = FixtureSupport.Context(FixtureSupport.LegacyProfile());

        var ex = Assert.Throws<CliException>(() => DumpCommand.Execute(context, new DumpOptions(
            Paths: ["Nope/Does/Not/Exist.uasset"],
            Criteria: new MatchCriteria(),
            ExportName: null, ClassName: null, Output: null, Indent: false, Force: false)));

        Assert.Equal(ExitCode.NotFound, ex.ExitCode);
        Assert.Equal("ASSET_NOT_FOUND", ex.ErrorCode);
    }

    /// <summary>
    /// The whole reason exit 5 is separate from exit 7: when an archive is still
    /// encrypted, "supply a key" and "fix the path" are different remedies.
    /// </summary>
    [Fact]
    public void DumpReportsMissingAesKeysRatherThanNotFoundWhenArchivesAreStillEncrypted()
    {
        var (context, _) = FixtureSupport.Context(FixtureSupport.EncryptedProfile() with { MainAesKey = null });

        var ex = Assert.Throws<CliException>(() => DumpCommand.Execute(context, new DumpOptions(
            Paths: ["CUE4ParseFixtures/Content/Fixtures/Properties/DA_AllProperties.uasset"],
            Criteria: new MatchCriteria(),
            ExportName: null, ClassName: null, Output: null, Indent: false, Force: false)));

        Assert.Equal(ExitCode.AesKey, ex.ExitCode);
        Assert.Equal("AES_KEY_MISSING", ex.ErrorCode);
    }
}
