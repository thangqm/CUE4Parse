using CUE4Parse.Cli.Output;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace CUE4Parse.Cli.Services;

public sealed record AesConfig
{
    public string? Main { get; init; }
    public Dictionary<string, string>? Dynamic { get; init; }
}

public sealed record ProfileConfig
{
    public string? PaksDir { get; init; }
    public string? Game { get; init; }
    public string? Mappings { get; init; }
    public AesConfig? Aes { get; init; }
}

public sealed record CliConfig
{
    public string? DefaultProfile { get; init; }
    public Dictionary<string, ProfileConfig>? Profiles { get; init; }
}

public sealed record ProfileOverrides(
    string? PaksDir = null,
    string? Game = null,
    string? Mappings = null,
    string? Aes = null);

public sealed record ResolvedProfile(
    string PaksDir,
    EGame Game,
    string? Mappings,
    string? MainAesKey,
    IReadOnlyDictionary<string, string> DynamicKeys);

public static class ConfigLoader
{
    public const string FileName = "cue4.json";

    public static CliConfig Load(string path)
    {
        try
        {
            return JsonConvert.DeserializeObject<CliConfig>(File.ReadAllText(path)) ?? new CliConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            throw new CliException(ExitCode.Config, "BAD_CONFIG", $"Could not read config '{path}': {ex.Message}");
        }
    }

    /// <summary>Resolves the config file location: explicit path, then working dir, then app data.</summary>
    public static string? FindConfigFile(string? explicitPath, string workingDirectory, string appDataDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (!File.Exists(explicitPath))
                throw new CliException(ExitCode.Config, "CONFIG_NOT_FOUND", $"Config file not found: {explicitPath}");
            return explicitPath;
        }

        var local = Path.Combine(workingDirectory, FileName);
        if (File.Exists(local)) return local;

        var appData = Path.Combine(appDataDirectory, FileName);
        return File.Exists(appData) ? appData : null;
    }

    public static ResolvedProfile Resolve(CliConfig config, string? profileName, ProfileOverrides overrides)
    {
        var name = profileName ?? config.DefaultProfile;

        var profile = name is null
            ? new ProfileConfig()
            : config.Profiles?.GetValueOrDefault(name) ?? throw new CliException(
                ExitCode.Config, "UNKNOWN_PROFILE",
                $"Profile '{name}' not found. Known profiles: " +
                $"{(config.Profiles is null ? "none" : string.Join(", ", config.Profiles.Keys))}.");

        var paksDir = overrides.PaksDir ?? profile.PaksDir
            ?? throw new CliException(
                ExitCode.Config, "MISSING_PAKS_DIR",
                "No paks directory configured. Set 'paksDir' in the profile or pass --paks.");

        var gameText = overrides.Game ?? profile.Game
            ?? throw new CliException(
                ExitCode.Config, "MISSING_GAME",
                "No game version configured. Set 'game' in the profile or pass --game.");

        return new ResolvedProfile(
            paksDir,
            GameParser.Parse(gameText),
            overrides.Mappings ?? profile.Mappings,
            overrides.Aes ?? profile.Aes?.Main,
            profile.Aes?.Dynamic ?? new Dictionary<string, string>());
    }
}
