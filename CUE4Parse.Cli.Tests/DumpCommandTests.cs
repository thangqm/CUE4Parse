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
        var sw = new StringWriter();
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(sw), Verbose: false);

        var code = DumpCommand.Execute(context, new DumpOptions(
            Paths: [],
            Criteria: new MatchCriteria(Globs: ["**/*.uasset"], Limit: 3),
            ExportName: null, ClassName: null, Output: null, Indent: false, Force: false));

        Assert.Equal((int)ExitCode.Success, code);

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            var parsed = JObject.Parse(line);
            Assert.NotNull(parsed["path"]);
            Assert.NotNull(parsed["status"]);
        }
    }

    [Fact]
    public void DumpDeserializesUnversionedPropertiesUsingTheFixtureMappings()
    {
        var sw = new StringWriter();
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(sw), Verbose: false);

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
        var sw = new StringWriter();
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(sw), Verbose: false);

        DumpCommand.Execute(context, new DumpOptions(
            Paths: [],
            Criteria: new MatchCriteria(Globs: ["**/*.uasset"]),
            ExportName: null, ClassName: "Texture2D", Output: null, Indent: false, Force: true));

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Equal("ok", JObject.Parse(line)["status"]?.Value<string>()));
    }

    [Fact]
    public void DumpReturnsNotFoundForAnUnknownExplicitPath()
    {
        var sw = new StringWriter();
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(sw), Verbose: false);

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
        var profile = FixtureSupport.EncryptedProfile() with { MainAesKey = null };
        var context = new CommandContext(profile, new JsonOutput(new StringWriter()), Verbose: false);

        var ex = Assert.Throws<CliException>(() => DumpCommand.Execute(context, new DumpOptions(
            Paths: ["CUE4ParseFixtures/Content/Fixtures/Properties/DA_AllProperties.uasset"],
            Criteria: new MatchCriteria(),
            ExportName: null, ClassName: null, Output: null, Indent: false, Force: false)));

        Assert.Equal(ExitCode.AesKey, ex.ExitCode);
        Assert.Equal("AES_KEY_MISSING", ex.ErrorCode);
    }
}
