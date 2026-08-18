using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class InfoCommandTests
{
    [Fact]
    public void ExecuteReportsGameVersionAndMountCountsAsJson()
    {
        var paks = Directory.CreateTempSubdirectory().FullName;
        var profile = new ResolvedProfile(paks, EGame.GAME_UE5_6, null, null, new Dictionary<string, string>());
        var (context, sw) = FixtureSupport.Context(profile);

        var code = InfoCommand.Execute(context);

        Assert.Equal((int)ExitCode.Success, code);
        var parsed = JObject.Parse(sw.ToString());
        Assert.Equal("GAME_UE5_6", parsed["game"]?.Value<string>());
        Assert.Equal(0, parsed["mountedVfs"]?.Value<int>());
        Assert.Equal(0, parsed["fileCount"]?.Value<int>());
        Assert.NotNull(parsed["missingAesGuids"]);
    }

    [Fact]
    public void ExecuteReportsNativeCapabilities()
    {
        var (context, sw) = FixtureSupport.Context();

        var code = InfoCommand.Execute(context);

        Assert.Equal((int)ExitCode.Success, code);
        var native = JObject.Parse(sw.ToString())["native"];
        Assert.NotNull(native);
        Assert.NotNull(native!["library"]);
        Assert.NotNull(native["acl"]);
        Assert.Contains(native["oodle"]!.Value<string>(), new[] { "native", "sidecar", "cached", "unavailable" });
    }

    /// <summary>
    /// A diagnostic command that goes dark when there is something to diagnose is
    /// backwards. The native block does not depend on the provider, so a bad paks
    /// directory must not hide it.
    /// </summary>
    [Fact]
    public void ExecuteStillReportsNativeCapabilitiesWhenTheProviderCannotBeBuilt()
    {
        var profile = new ResolvedProfile(
            "/definitely/not/a/paks/dir", EGame.GAME_UE5_6, null, null, new Dictionary<string, string>());
        var (context, sw) = FixtureSupport.Context(profile);

        var code = InfoCommand.Execute(context);

        Assert.Equal((int)ExitCode.Mount, code);
        var parsed = JObject.Parse(sw.ToString());
        Assert.NotNull(parsed["native"]);
        Assert.NotNull(parsed["error"]);
    }

    [Fact]
    public void VerboseListsEveryMountedArchiveByName()
    {
        var sw = new StringWriter();
        var context = new CommandContext(
            new Lazy<ResolvedProfile>(FixtureSupport.Profile()), new JsonOutput(sw), verbose: true);

        InfoCommand.Execute(context);

        var parsed = JObject.Parse(sw.ToString());
        var archives = (JArray)parsed["mountedArchives"]!;
        Assert.Equal(parsed["mountedVfs"]!.Value<int>(), archives.Count);
        Assert.All(archives, name => Assert.False(string.IsNullOrWhiteSpace(name.Value<string>())));
    }

    /// <summary>Without --verbose the two archive arrays must not appear at all.</summary>
    [Fact]
    public void TheArchiveListsAreAbsentWithoutVerbose()
    {
        var (context, sw) = FixtureSupport.Context();

        InfoCommand.Execute(context);

        var parsed = JObject.Parse(sw.ToString());
        Assert.Null(parsed["mountedArchives"]);
        Assert.Null(parsed["unloadedArchives"]);
    }
}
