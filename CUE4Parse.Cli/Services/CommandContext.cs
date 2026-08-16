using System.CommandLine;
using CUE4Parse.Cli.Output;

namespace CUE4Parse.Cli.Services;

public sealed record CommandContext(ResolvedProfile Profile, JsonOutput Output, bool Verbose);

/// <summary>
/// Shared options available to every subcommand.
/// All are <c>Recursive</c>: System.CommandLine 2.0 does not propagate root options otherwise.
/// </summary>
public static class GlobalOptions
{
    public static readonly Option<string?> Profile =
        new("--profile", "-p") { Description = "Profile name from cue4.json", Recursive = true };

    public static readonly Option<string?> Config =
        new("--config") { Description = "Path to cue4.json", Recursive = true };

    public static readonly Option<string?> Paks =
        new("--paks") { Description = "Override the paks directory", Recursive = true };

    public static readonly Option<string?> Game =
        new("--game") { Description = "Override the game version (GAME_UE5_6 or 5.6)", Recursive = true };

    public static readonly Option<string?> Mappings =
        new("--mappings") { Description = "Override the .usmap path, or 'auto'", Recursive = true };

    public static readonly Option<string?> Aes =
        new("--aes") { Description = "Override the main AES key", Recursive = true };

    public static readonly Option<bool> Verbose =
        new("--verbose", "-v") { Description = "Verbose logging to stderr", Recursive = true };

    public static void AddTo(RootCommand root)
    {
        root.Options.Add(Profile);
        root.Options.Add(Config);
        root.Options.Add(Paks);
        root.Options.Add(Game);
        root.Options.Add(Mappings);
        root.Options.Add(Aes);
        root.Options.Add(Verbose);
    }
}

public static class ContextBuilder
{
    public static CommandContext Build(ParseResult parseResult)
    {
        var configPath = ConfigLoader.FindConfigFile(
            parseResult.GetValue(GlobalOptions.Config),
            Directory.GetCurrentDirectory(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cue4"));

        var config = configPath is null ? new CliConfig() : ConfigLoader.Load(configPath);

        var profile = ConfigLoader.Resolve(
            config,
            parseResult.GetValue(GlobalOptions.Profile),
            new ProfileOverrides(
                PaksDir: parseResult.GetValue(GlobalOptions.Paks),
                Game: parseResult.GetValue(GlobalOptions.Game),
                Mappings: parseResult.GetValue(GlobalOptions.Mappings),
                Aes: parseResult.GetValue(GlobalOptions.Aes)));

        return new CommandContext(
            profile,
            new JsonOutput(Console.Out),
            parseResult.GetValue(GlobalOptions.Verbose));
    }
}
