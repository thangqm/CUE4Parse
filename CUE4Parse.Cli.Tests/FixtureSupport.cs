using System.Text;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

/// <summary>
/// Points the CLI at the real fixture archives. The csproj links
/// CUE4Parse.Tests/Fixtures into this project's own output, so the paths are
/// configuration-independent.
/// </summary>
public static class FixtureSupport
{
    private const string AesKeyText = "CUE4ParseFixtureAESKey0123456789";

    public static string Dir(params string[] parts)
        => Path.GetFullPath(Path.Combine([AppContext.BaseDirectory, "Fixtures", "UE5_8", .. parts]));

    private static string Mappings() => Dir("Mappings", "CUE4ParseFixtures-Oodle.usmap");

    private static string RequireDir(params string[] parts)
    {
        var path = Dir(parts);
        Assert.True(Directory.Exists(path), $"Missing fixture directory: {path}");
        return path;
    }

    /// <summary>Unencrypted cooked packages. No AES key needed; mappings are.</summary>
    public static ResolvedProfile Profile() => new(
        RequireDir("LegacyPak", "Unversioned", "Oodle"),
        EGame.GAME_UE5_8,
        Mappings(),
        MainAesKey: null,
        new Dictionary<string, string>());

    /// <summary>Same content behind the fixture AES key, for the SubmitKeys path.</summary>
    public static ResolvedProfile EncryptedProfile() => new(
        RequireDir("LegacyPak", "Unversioned", "OodleEncrypted"),
        EGame.GAME_UE5_8,
        Mappings(),
        "0x" + Convert.ToHexString(Encoding.ASCII.GetBytes(AesKeyText)),
        new Dictionary<string, string>());

    /// <summary>A context writing to a capture buffer, which the caller reads back.</summary>
    public static (CommandContext Context, StringWriter Out) Context(ResolvedProfile? profile = null)
    {
        var sw = new StringWriter();
        return (new CommandContext(profile ?? Profile(), new JsonOutput(sw)), sw);
    }

    /// <summary>Parses captured NDJSON, one object per non-empty line.</summary>
    public static JObject[] Ndjson(StringWriter sw) =>
    [
        .. sw.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(JObject.Parse),
    ];
}
