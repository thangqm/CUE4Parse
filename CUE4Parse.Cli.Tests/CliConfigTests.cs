using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.UE4.Versions;

namespace CUE4Parse.Cli.Tests;

public class CliConfigTests
{
    private static CliConfig SampleConfig() => new()
    {
        DefaultProfile = "fn",
        Profiles = new Dictionary<string, ProfileConfig>
        {
            ["fn"] = new()
            {
                PaksDir = "D:/Games/Fortnite/Paks",
                Game = "GAME_UE5_6",
                Mappings = "auto",
                Aes = new AesConfig { Main = "0xAABB" },
            },
        },
    };

    [Fact]
    public void ResolveUsesDefaultProfileWhenNoNameGiven()
    {
        var resolved = ConfigLoader.Resolve(SampleConfig(), null, new ProfileOverrides());

        Assert.Equal("D:/Games/Fortnite/Paks", resolved.PaksDir);
        Assert.Equal(EGame.GAME_UE5_6, resolved.Game);
        Assert.Equal("0xAABB", resolved.MainAesKey);
    }

    [Fact]
    public void CommandLineOverridesBeatProfileValues()
    {
        var resolved = ConfigLoader.Resolve(
            SampleConfig(), "fn",
            new ProfileOverrides(PaksDir: "E:/Other", Game: "5.3", Aes: "0xCCDD"));

        Assert.Equal("E:/Other", resolved.PaksDir);
        Assert.Equal(EGame.GAME_UE5_3, resolved.Game);
        Assert.Equal("0xCCDD", resolved.MainAesKey);
    }

    [Fact]
    public void ResolveThrowsConfigErrorForUnknownProfileName()
    {
        var ex = Assert.Throws<CliException>(
            () => ConfigLoader.Resolve(SampleConfig(), "nope", new ProfileOverrides()));

        Assert.Equal(ExitCode.Config, ex.ExitCode);
        Assert.Equal("UNKNOWN_PROFILE", ex.ErrorCode);
    }

    [Fact]
    public void ResolveThrowsConfigErrorWhenPaksDirIsMissingEverywhere()
    {
        var config = new CliConfig
        {
            DefaultProfile = "empty",
            Profiles = new Dictionary<string, ProfileConfig> { ["empty"] = new() { Game = "5.6" } },
        };

        var ex = Assert.Throws<CliException>(() => ConfigLoader.Resolve(config, null, new ProfileOverrides()));
        Assert.Equal(ExitCode.Config, ex.ExitCode);
        Assert.Equal("MISSING_PAKS_DIR", ex.ErrorCode);
    }

    [Fact]
    public void ResolveWorksWithNoConfigFileWhenAllValuesComeFromOverrides()
    {
        var resolved = ConfigLoader.Resolve(
            new CliConfig(), null,
            new ProfileOverrides(PaksDir: "E:/Only", Game: "5.6"));

        Assert.Equal("E:/Only", resolved.PaksDir);
        Assert.Equal(EGame.GAME_UE5_6, resolved.Game);
    }

    [Fact]
    public void FindConfigFilePrefersWorkingDirectoryOverAppData()
    {
        var work = Directory.CreateTempSubdirectory().FullName;
        var appData = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(work, "cue4.json"), "{}");
        File.WriteAllText(Path.Combine(appData, "cue4.json"), "{}");

        var found = ConfigLoader.FindConfigFile(null, work, appData);

        Assert.Equal(Path.Combine(work, "cue4.json"), found);
    }

    [Fact]
    public void FindConfigFileFallsBackToAppData()
    {
        var work = Directory.CreateTempSubdirectory().FullName;
        var appData = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(appData, "cue4.json"), "{}");

        var found = ConfigLoader.FindConfigFile(null, work, appData);

        Assert.Equal(Path.Combine(appData, "cue4.json"), found);
    }

    [Fact]
    public void LoadThrowsConfigErrorForMalformedJson()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "cue4.json");
        File.WriteAllText(path, "{ not json");

        var ex = Assert.Throws<CliException>(() => ConfigLoader.Load(path));
        Assert.Equal(ExitCode.Config, ex.ExitCode);
        Assert.Equal("BAD_CONFIG", ex.ErrorCode);
    }
}
