using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class ListCommandTests
{
    [Fact]
    public void ExecuteEmitsOneNdjsonObjectPerAssetWithPathAndSize()
    {
        var (context, sw) = FixtureSupport.Context();

        var code = ListCommand.Execute(context, new ListOptions(
            new MatchCriteria(Globs: ["**/*.uasset"], Limit: 3), CountOnly: false));

        Assert.Equal((int)ExitCode.Success, code);

        var lines = FixtureSupport.Ndjson(sw);
        Assert.NotEmpty(lines);
        Assert.True(lines.Length <= 3, "Limit was not applied.");

        foreach (var parsed in lines)
        {
            Assert.EndsWith(".uasset", parsed["path"]?.Value<string>());
            Assert.NotNull(parsed["size"]);
        }
    }

    /// <summary>
    /// Regression guard for the bootstrap. This profile carries no AES key, so if
    /// ProviderFactory ever drops provider.Mount() again, nothing mounts and the
    /// count is zero rather than an error — a silent, total failure.
    /// </summary>
    [Fact]
    public void ExecuteWithCountOnlyEmitsASingleCountObject()
    {
        var (context, sw) = FixtureSupport.Context();

        var code = ListCommand.Execute(context, new ListOptions(new MatchCriteria(), CountOnly: true));

        Assert.Equal((int)ExitCode.Success, code);
        var parsed = JObject.Parse(sw.ToString());
        Assert.True(parsed["count"]?.Value<int>() > 0,
            "Nothing mounted. ProviderFactory must call provider.Mount(): Initialize() only registers readers.");
    }

    [Fact]
    public void ExecuteMountsAnEncryptedArchiveWhenTheKeyIsSupplied()
    {
        var (context, sw) = FixtureSupport.Context(FixtureSupport.EncryptedProfile());

        var code = ListCommand.Execute(context, new ListOptions(new MatchCriteria(), CountOnly: true));

        Assert.Equal((int)ExitCode.Success, code);
        Assert.True(JObject.Parse(sw.ToString())["count"]?.Value<int>() > 0);
    }

    [Fact]
    public void ExecuteEmitsEachPathOnlyOnce()
    {
        var (context, sw) = FixtureSupport.Context();

        ListCommand.Execute(context, new ListOptions(new MatchCriteria(Globs: ["**/*.uasset"]), CountOnly: false));

        // FileProviderDictionary.Keys concatenates every mounted index and can repeat.
        var paths = FixtureSupport.Ndjson(sw).Select(line => line["path"]!.Value<string>()).ToArray();

        Assert.Equal(paths.Length, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ExecuteWithANonMatchingGlobEmitsNothingAndStillSucceeds()
    {
        var (context, sw) = FixtureSupport.Context();

        var code = ListCommand.Execute(context, new ListOptions(
            new MatchCriteria(Globs: ["**/NoSuchFolder/**"]), CountOnly: false));

        Assert.Equal((int)ExitCode.Success, code);
        Assert.Empty(sw.ToString().Trim());
    }
}
