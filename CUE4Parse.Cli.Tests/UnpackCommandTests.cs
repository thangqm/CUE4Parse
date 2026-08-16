using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class UnpackCommandTests
{
    [Fact]
    public void UnpackWritesFilesAndReportsEachAsNdjson()
    {
        var outDir = new DirectoryInfo(Directory.CreateTempSubdirectory().FullName);
        var sw = new StringWriter();
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(sw), Verbose: false);

        var code = UnpackCommand.Execute(context, new UnpackOptions(
            Paths: [],
            Criteria: new MatchCriteria(Globs: ["**/*.uasset"], Limit: 2),
            Output: outDir, Flat: false, Force: false));

        Assert.Equal((int)ExitCode.Success, code);
        Assert.NotEmpty(outDir.GetFiles("*", SearchOption.AllDirectories));

        foreach (var line in sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Assert.Equal("ok", JObject.Parse(line)["status"]?.Value<string>());
    }

    /// <summary>
    /// A .uasset without its .uexp is unopenable. SaveAsset returns only the one
    /// file; SavePackage returns every payload file for the package.
    /// </summary>
    [Fact]
    public void UnpackWritesTheUexpPayloadAlongsideTheUasset()
    {
        var outDir = new DirectoryInfo(Directory.CreateTempSubdirectory().FullName);
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(new StringWriter()), Verbose: false);

        UnpackCommand.Execute(context, new UnpackOptions(
            Paths: ["CUE4ParseFixtures/Content/Fixtures/Properties/DA_AllProperties.uasset"],
            Criteria: new MatchCriteria(),
            Output: outDir, Flat: false, Force: false));

        var written = outDir.GetFiles("*", SearchOption.AllDirectories).Select(f => f.Name).ToArray();
        Assert.Contains("DA_AllProperties.uasset", written);
        Assert.Contains("DA_AllProperties.uexp", written);
    }

    [Fact]
    public void UnpackFlatWritesAllFilesIntoTheOutputRoot()
    {
        var outDir = new DirectoryInfo(Directory.CreateTempSubdirectory().FullName);
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(new StringWriter()), Verbose: false);

        UnpackCommand.Execute(context, new UnpackOptions(
            Paths: ["CUE4ParseFixtures/Content/Fixtures/Properties/DA_AllProperties.uasset"],
            Criteria: new MatchCriteria(),
            Output: outDir, Flat: true, Force: false));

        Assert.Empty(outDir.GetDirectories());
        Assert.NotEmpty(outDir.GetFiles());
    }

    [Fact]
    public void FlatDestinationFailsWhenTwoDifferentAssetsShareALeafName()
    {
        var written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        UnpackCommand.ResolveDestination(@"C:\out", flat: true, "Game/Chars/A.uasset", written);

        var ex = Assert.Throws<CliException>(() => UnpackCommand.ResolveDestination(
            @"C:\out", flat: true, "Game/Props/A.uasset", written));

        Assert.Equal(ExitCode.Usage, ex.ExitCode);
        Assert.Equal("OUTPUT_COLLISION", ex.ErrorCode);
    }

    [Fact]
    public void FlatDestinationDoesNotTreatAPackageAndItsOwnPayloadFilesAsACollision()
    {
        var written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Differing extensions are distinct leaf names, and re-visiting the same
        // source path is a no-op — neither is a clash.
        UnpackCommand.ResolveDestination(@"C:\out", flat: true, "Game/A.uasset", written);
        UnpackCommand.ResolveDestination(@"C:\out", flat: true, "Game/A.uexp", written);
        UnpackCommand.ResolveDestination(@"C:\out", flat: true, "Game/A.uexp", written);

        Assert.Equal(2, written.Count);
    }

    /// <summary>
    /// A broad --flat unpack of a real archive must succeed: a glob matching both a
    /// package and its own .uexp must not be mistaken for a collision.
    /// </summary>
    [Fact]
    public void UnpackFlatOverAnEntireArchiveDoesNotFalselyReportACollision()
    {
        var outDir = new DirectoryInfo(Directory.CreateTempSubdirectory().FullName);
        var sw = new StringWriter();
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(sw), Verbose: false);

        var code = UnpackCommand.Execute(context, new UnpackOptions(
            Paths: [],
            Criteria: new MatchCriteria(Globs: ["**/*"]),
            Output: outDir, Flat: true, Force: true));

        Assert.Equal((int)ExitCode.Success, code);
        Assert.Empty(outDir.GetDirectories());
        Assert.All(
            sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => Assert.Equal("ok", JObject.Parse(line)["status"]?.Value<string>()));
    }
}
