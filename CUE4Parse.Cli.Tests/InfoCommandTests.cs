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
        var sw = new StringWriter();

        var code = InfoCommand.Execute(new CommandContext(profile, new JsonOutput(sw), Verbose: false));

        Assert.Equal((int)ExitCode.Success, code);
        var parsed = JObject.Parse(sw.ToString());
        Assert.Equal("GAME_UE5_6", parsed["game"]?.Value<string>());
        Assert.Equal(0, parsed["mountedVfs"]?.Value<int>());
        Assert.Equal(0, parsed["fileCount"]?.Value<int>());
        Assert.NotNull(parsed["missingAesGuids"]);
    }
}
